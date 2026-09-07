import AppKit

@MainActor
final class ProtectedWindow: NSPanel {
    private let fakeCursor = FakeCursorCoordinator()
    private static let resizeMargin: CGFloat = 8
    private var resizeSession: (frame: NSRect, cursor: NSPoint, edges: ResizeEdges)?

    struct ResizeEdges: OptionSet {
        let rawValue: Int

        static let left = ResizeEdges(rawValue: 1 << 0)
        static let right = ResizeEdges(rawValue: 1 << 1)
        static let bottom = ResizeEdges(rawValue: 1 << 2)
        static let top = ResizeEdges(rawValue: 1 << 3)
    }

    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 920, height: 640),
            styleMask: [.titled, .closable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        title = "Phantom"
        titleVisibility = .hidden
        titlebarAppearsTransparent = true
        isReleasedWhenClosed = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isFloatingPanel = true
        hidesOnDeactivate = false
        level = .statusBar
        minSize = NSSize(width: 820, height: 560)
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        isMovableByWindowBackground = true
        acceptsMouseMovedEvents = true
        animationBehavior = .none

        // Requested legacy AppKit exclusion. Apple no longer guarantees that
        // capture clients on current macOS releases will honor this value.
        sharingType = .none
        standardWindowButton(.closeButton)?.toolTip = nil
        standardWindowButton(.miniaturizeButton)?.isHidden = true
        standardWindowButton(.zoomButton)?.isHidden = true
        fakeCursor.attach(to: self)
    }

    func apply(opacity: Double, clickThrough: Bool) {
        alphaValue = max(0.35, min(opacity, 1.0))
        ignoresMouseEvents = clickThrough
    }

    func installCursorTracking() {
        fakeCursor.attach(to: self)
    }

    func configureFakeCursor(enabled: Bool, clickThrough: Bool, scale: Double) {
        fakeCursor.configure(enabled: enabled, clickThrough: clickThrough, scale: scale)
    }

    func stopFakeCursor() {
        fakeCursor.stop()
    }

    override func orderOut(_ sender: Any?) {
        fakeCursor.windowHidden()
        super.orderOut(sender)
    }

    override func sendEvent(_ event: NSEvent) {
        switch event.type {
        case .leftMouseDown:
            if let edges = resizeEdges(at: event.locationInWindow) {
                resizeSession = (frame, NSEvent.mouseLocation, edges)
                NSCursor.arrow.set()
                return
            }
        case .leftMouseDragged:
            if let resizeSession {
                let cursor = NSEvent.mouseLocation
                let delta = NSPoint(
                    x: cursor.x - resizeSession.cursor.x,
                    y: cursor.y - resizeSession.cursor.y
                )
                setFrame(Self.resizedFrame(
                    resizeSession.frame,
                    delta: delta,
                    edges: resizeSession.edges,
                    minimumSize: minSize
                ), display: true)
                NSCursor.arrow.set()
                return
            }
        case .leftMouseUp:
            if resizeSession != nil {
                resizeSession = nil
                NSCursor.arrow.set()
                return
            }
        default:
            break
        }

        super.sendEvent(event)
        switch event.type {
        case .cursorUpdate, .mouseMoved, .leftMouseDragged:
            NSCursor.arrow.set()
        default:
            break
        }
    }

    private func resizeEdges(at point: NSPoint) -> ResizeEdges? {
        var edges: ResizeEdges = []
        if point.x <= Self.resizeMargin { edges.insert(.left) }
        if point.x >= frame.width - Self.resizeMargin { edges.insert(.right) }
        if point.y <= Self.resizeMargin { edges.insert(.bottom) }
        if point.y >= frame.height - Self.resizeMargin { edges.insert(.top) }
        return edges.isEmpty ? nil : edges
    }

    static func resizedFrame(
        _ start: NSRect,
        delta: NSPoint,
        edges: ResizeEdges,
        minimumSize: NSSize
    ) -> NSRect {
        var result = start

        if edges.contains(.left) {
            result.size.width = max(minimumSize.width, start.width - delta.x)
            result.origin.x = start.maxX - result.width
        } else if edges.contains(.right) {
            result.size.width = max(minimumSize.width, start.width + delta.x)
        }

        if edges.contains(.bottom) {
            result.size.height = max(minimumSize.height, start.height - delta.y)
            result.origin.y = start.maxY - result.height
        } else if edges.contains(.top) {
            result.size.height = max(minimumSize.height, start.height + delta.y)
        }

        return result
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

}
