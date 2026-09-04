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
    }

    func apply(opacity: Double, clickThrough: Bool) {
        alphaValue = max(0.35, min(opacity, 1.0))
        ignoresMouseEvents = clickThrough
        fakeCursor.configure(enabled: fakeCursorEnabled, clickThrough: clickThrough, scale: fakeCursorScale)
    }

    private var fakeCursorEnabled = false
    private var fakeCursorScale = 1.0

    func installCursorTracking() {
        fakeCursor.attach(to: self)
    }

    func configureFakeCursor(enabled: Bool, clickThrough: Bool, scale: Double) {
        fakeCursorEnabled = enabled
        fakeCursorScale = scale
        fakeCursor.configure(enabled: enabled, clickThrough: clickThrough, scale: scale)
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

}
