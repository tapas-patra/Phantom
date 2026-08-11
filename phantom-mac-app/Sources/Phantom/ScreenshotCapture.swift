import AppKit
import CoreGraphics

@MainActor
enum ScreenshotCapture {
    private static var activeSelection: AreaSelectionController?

    static func requestAccess() -> Bool {
        CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess()
    }

    static func openSettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") else { return }
        NSWorkspace.shared.open(url)
    }

    static func selectArea(screen: NSScreen?) async throws -> Data {
        guard CGPreflightScreenCaptureAccess() else { throw ScreenshotError.permissionDenied }
        guard let screen = screen ?? NSScreen.main,
              let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber,
              let image = CGDisplayCreateImage(CGDirectDisplayID(number.uint32Value)) else {
            throw ScreenshotError.captureFailed
        }

        return try await withCheckedThrowingContinuation { continuation in
            let controller = AreaSelectionController(screen: screen, image: image) { result in
                activeSelection = nil
                continuation.resume(with: result)
            }
            activeSelection = controller
            controller.present()
        }
    }

    fileprivate static func encode(_ image: CGImage) throws -> Data {
        let maxEdge: CGFloat = 1_600
        let scale = min(1, maxEdge / CGFloat(max(image.width, image.height)))
        let size = NSSize(width: CGFloat(image.width) * scale, height: CGFloat(image.height) * scale)
        let source = NSImage(cgImage: image, size: size)
        let resized = NSImage(size: size)
        resized.lockFocus()
        NSGraphicsContext.current?.imageInterpolation = .high
        source.draw(in: NSRect(origin: .zero, size: size))
        resized.unlockFocus()

        guard let tiff = resized.tiffRepresentation,
              let bitmap = NSBitmapImageRep(data: tiff),
              let png = bitmap.representation(using: .png, properties: [:]) else {
            throw ScreenshotError.encodingFailed
        }
        return png
    }
}

@MainActor
private final class AreaSelectionController: NSObject {
    private let screen: NSScreen
    private let image: CGImage
    private let completion: (Result<Data, Error>) -> Void
    private var panel: NSPanel?

    init(screen: NSScreen, image: CGImage, completion: @escaping (Result<Data, Error>) -> Void) {
        self.screen = screen
        self.image = image
        self.completion = completion
    }

    func present() {
        let selectionView = AreaSelectionView(frame: NSRect(origin: .zero, size: screen.frame.size))
        selectionView.onCancel = { [weak self] in self?.finish(.failure(ScreenshotError.cancelled)) }
        selectionView.onSelection = { [weak self] rect in self?.capture(rect) }

        let panel = NSPanel(
            contentRect: screen.frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        panel.contentView = selectionView
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .screenSaver
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        panel.sharingType = .none
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(selectionView)
        NSApp.activate(ignoringOtherApps: true)
        self.panel = panel
    }

    private func capture(_ selection: NSRect?) {
        let points = selection ?? NSRect(origin: .zero, size: screen.frame.size)
        let scaleX = CGFloat(image.width) / screen.frame.width
        let scaleY = CGFloat(image.height) / screen.frame.height
        let pixels = CGRect(
            x: points.minX * scaleX,
            y: (screen.frame.height - points.maxY) * scaleY,
            width: points.width * scaleX,
            height: points.height * scaleY
        ).integral
        guard pixels.width >= 1, pixels.height >= 1,
              let cropped = image.cropping(to: pixels) else {
            finish(.failure(ScreenshotError.captureFailed))
            return
        }
        do {
            finish(.success(try ScreenshotCapture.encode(cropped)))
        } catch {
            finish(.failure(error))
        }
    }

    private func finish(_ result: Result<Data, Error>) {
        panel?.orderOut(nil)
        panel?.contentView = nil
        panel = nil
        completion(result)
    }
}

private final class AreaSelectionView: NSView {
    var onSelection: ((NSRect?) -> Void)?
    var onCancel: (() -> Void)?
    private var startPoint: NSPoint?
    private var selection = NSRect.zero

    override var acceptsFirstResponder: Bool { true }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    override func mouseDown(with event: NSEvent) {
        startPoint = convert(event.locationInWindow, from: nil)
        selection = .zero
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard let startPoint else { return }
        let current = convert(event.locationInWindow, from: nil)
        selection = NSRect(
            x: min(startPoint.x, current.x),
            y: min(startPoint.y, current.y),
            width: abs(startPoint.x - current.x),
            height: abs(startPoint.y - current.y)
        )
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard selection.width >= 10, selection.height >= 10 else {
            startPoint = nil
            selection = .zero
            needsDisplay = true
            return
        }
        onSelection?(selection)
    }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 36, 76:
            onSelection?(nil)
        case 53:
            onCancel?()
        default:
            super.keyDown(with: event)
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.black.withAlphaComponent(0.46).setFill()
        bounds.fill()

        if !selection.isEmpty {
            NSGraphicsContext.current?.saveGraphicsState()
            NSGraphicsContext.current?.compositingOperation = .clear
            selection.fill()
            NSGraphicsContext.current?.restoreGraphicsState()
            NSColor.systemBlue.setStroke()
            let path = NSBezierPath(rect: selection)
            path.lineWidth = 2
            path.stroke()
        }

        let text = "Drag to capture an area  •  Return: full display  •  Esc: cancel"
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 14, weight: .semibold),
            .foregroundColor: NSColor.white,
            .backgroundColor: NSColor.black.withAlphaComponent(0.78)
        ]
        let size = text.size(withAttributes: attributes)
        text.draw(
            at: NSPoint(x: bounds.midX - size.width / 2, y: bounds.maxY - size.height - 28),
            withAttributes: attributes
        )
    }
}

enum ScreenshotError: LocalizedError {
    case permissionDenied
    case captureFailed
    case encodingFailed
    case cancelled

    var errorDescription: String? {
        switch self {
        case .permissionDenied:
            return "Allow Screen Recording for Phantom in System Settings, reopen Phantom, then try again."
        case .captureFailed:
            return "Phantom could not capture the selected display area."
        case .encodingFailed:
            return "Phantom could not encode the screenshot."
        case .cancelled:
            return "Screenshot cancelled."
        }
    }
}
