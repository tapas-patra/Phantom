import AppKit

@MainActor
final class ProtectedWindow: NSPanel {
    private let fakeCursor = FakeCursorCoordinator()
    private var cursorTrackingArea: NSTrackingArea?

    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 920, height: 640),
            styleMask: [.titled, .closable, .resizable, .fullSizeContentView],
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
    }

    func apply(opacity: Double, clickThrough: Bool) {
        alphaValue = max(0.35, min(opacity, 1.0))
        ignoresMouseEvents = clickThrough
        fakeCursor.configure(enabled: fakeCursorEnabled, clickThrough: clickThrough)
    }

    private var fakeCursorEnabled = false

    func installCursorTracking() {
        guard let contentView else { return }
        if let cursorTrackingArea { contentView.removeTrackingArea(cursorTrackingArea) }
        let area = NSTrackingArea(
            rect: .zero,
            options: [.activeAlways, .inVisibleRect, .mouseEnteredAndExited, .mouseMoved],
            owner: self,
            userInfo: nil
        )
        contentView.addTrackingArea(area)
        cursorTrackingArea = area
    }

    func configureFakeCursor(enabled: Bool, clickThrough: Bool) {
        fakeCursorEnabled = enabled
        fakeCursor.configure(enabled: enabled, clickThrough: clickThrough)
    }

    func stopFakeCursor() {
        fakeCursor.stop()
    }

    override func orderOut(_ sender: Any?) {
        fakeCursor.windowHidden()
        super.orderOut(sender)
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    override func sendEvent(_ event: NSEvent) {
        super.sendEvent(event)
        switch event.type {
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged, .cursorUpdate:
            NSCursor.arrow.set()
        default:
            break
        }
    }

    override func mouseEntered(with event: NSEvent) {
        fakeCursor.entered(at: convertPoint(toScreen: event.locationInWindow))
    }

    override func mouseMoved(with event: NSEvent) {
        fakeCursor.moved(to: convertPoint(toScreen: event.locationInWindow))
    }

    override func mouseExited(with event: NSEvent) {
        fakeCursor.exited(at: NSEvent.mouseLocation)
    }
}
