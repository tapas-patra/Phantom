import AppKit

@MainActor
final class ProtectedWindow: NSPanel {
    private let fakeCursor = FakeCursorCoordinator()

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
        fakeCursor.attach(to: self)

        // Cursor rectangles only control pointer appearance; native edge hit-testing
        // remains active, so the window is still resizable with a stable arrow.
        NSCursor.arrow.set()
        discardCursorRects()
        disableCursorRects()
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
        super.sendEvent(event)
        switch event.type {
        case .cursorUpdate, .mouseMoved, .leftMouseDragged:
            NSCursor.arrow.set()
        default:
            break
        }
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

}
