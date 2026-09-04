import AppKit
import QuartzCore

@MainActor
final class FakeCursorCoordinator {
    private let fakePanel = FakeCursorPanel(sharingType: .readOnly)
    private let livePanel = FakeCursorPanel(sharingType: .none)
    private var enabled = false
    private var active = false
    private var systemCursorHidden = false
    private var pendingHide: DispatchWorkItem?

    func configure(enabled: Bool, clickThrough: Bool) {
        self.enabled = enabled && !clickThrough
        if !self.enabled { deactivate(at: NSEvent.mouseLocation, animated: false) }
    }

    func entered(at screenPoint: NSPoint) {
        guard enabled, !active else { return }
        pendingHide?.cancel()
        active = true
        fakePanel.moveHotspot(to: screenPoint)
        livePanel.moveHotspot(to: screenPoint)
        fakePanel.orderFrontRegardless()
        livePanel.orderFrontRegardless()
        if !systemCursorHidden {
            NSCursor.hide()
            systemCursorHidden = true
        }
    }

    func moved(to screenPoint: NSPoint) {
        guard enabled else { return }
        if !active { entered(at: screenPoint) }
        livePanel.moveHotspot(to: screenPoint)
    }

    func exited(at screenPoint: NSPoint) {
        deactivate(at: screenPoint, animated: true)
    }

    func stop() {
        enabled = false
        deactivate(at: NSEvent.mouseLocation, animated: false)
    }

    func windowHidden() {
        deactivate(at: NSEvent.mouseLocation, animated: false)
    }

    private func deactivate(at screenPoint: NSPoint, animated: Bool) {
        pendingHide?.cancel()
        guard active || systemCursorHidden else { return }
        active = false
        livePanel.orderOut(nil)
        if systemCursorHidden {
            NSCursor.unhide()
            systemCursorHidden = false
        }

        guard animated, fakePanel.isVisible else {
            fakePanel.orderOut(nil)
            return
        }

        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.12
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            fakePanel.animator().setFrameOrigin(FakeCursorPanel.origin(for: screenPoint))
        }
        let hide = DispatchWorkItem { [weak panel = fakePanel] in panel?.orderOut(nil) }
        pendingHide = hide
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.14, execute: hide)
    }
}

@MainActor
private final class FakeCursorPanel: NSPanel {
    private static let panelSize = NSSize(width: 28, height: 28)

    init(sharingType: NSWindow.SharingType) {
        super.init(
            contentRect: NSRect(origin: .zero, size: Self.panelSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        isReleasedWhenClosed = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        ignoresMouseEvents = true
        hidesOnDeactivate = false
        level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 2)
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        self.sharingType = sharingType

        let imageView = NSImageView(frame: NSRect(origin: .zero, size: Self.panelSize))
        imageView.image = NSCursor.arrow.image
        imageView.imageScaling = .scaleProportionallyDown
        imageView.imageAlignment = .alignTopLeft
        contentView = imageView
    }

    func moveHotspot(to point: NSPoint) {
        setFrameOrigin(Self.origin(for: point))
    }

    static func origin(for point: NSPoint) -> NSPoint {
        NSPoint(x: point.x, y: point.y - panelSize.height)
    }
}
