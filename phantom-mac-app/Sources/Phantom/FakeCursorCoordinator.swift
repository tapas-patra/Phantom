import AppKit

@MainActor
final class FakeCursorCoordinator {
    private let fakePanel = FakeCursorPanel(sharingType: .readOnly)
    private let livePanel = FakeCursorPanel(sharingType: .none)
    private weak var protectedWindow: NSWindow?
    private var boundaryTimer: Timer?
    private var transitionTimer: Timer?
    private var transitionGeneration = 0
    private var enabled = false
    private var active = false
    private var systemCursorHidden = false
    private var scale = 1.0
    private var clonedCursor = NSCursor.arrow

    func attach(to window: NSWindow) {
        protectedWindow = window
    }

    func configure(enabled: Bool, clickThrough: Bool, scale: Double) {
        self.scale = Self.clampedScale(scale)
        self.enabled = enabled && !clickThrough

        guard self.enabled else {
            stopBoundaryTracking()
            deactivate(at: NSEvent.mouseLocation, animated: false)
            return
        }

        if active {
            let fakePosition = fakePanel.hotspotPosition
            fakePanel.apply(cursor: clonedCursor, scale: self.scale)
            livePanel.apply(cursor: clonedCursor, scale: 1.0)
            fakePanel.moveHotspot(to: fakePosition)
            livePanel.moveHotspot(to: NSEvent.mouseLocation)
        }
        startBoundaryTracking()
        synchronizeWithWindowBoundary()
    }

    func stop() {
        enabled = false
        stopBoundaryTracking()
        deactivate(at: NSEvent.mouseLocation, animated: false)
    }

    func windowHidden() {
        deactivate(at: NSEvent.mouseLocation, animated: false)
    }

    static func clampedScale(_ value: Double) -> Double {
        min(2.0, max(0.5, value))
    }

    static func transitionDuration(distance: CGFloat) -> TimeInterval {
        min(0.8, max(0.1, TimeInterval(distance / 600.0)))
    }

    static func canActivate(
        enabled: Bool,
        windowIsVisible: Bool,
        windowIgnoresMouse: Bool,
        applicationIsActive: Bool
    ) -> Bool {
        enabled && windowIsVisible && !windowIgnoresMouse && applicationIsActive
    }

    private func startBoundaryTracking() {
        guard boundaryTimer == nil else { return }
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.synchronizeWithWindowBoundary() }
        }
        RunLoop.main.add(timer, forMode: .common)
        boundaryTimer = timer
    }

    private func stopBoundaryTracking() {
        boundaryTimer?.invalidate()
        boundaryTimer = nil
    }

    private func synchronizeWithWindowBoundary() {
        guard let window = protectedWindow,
              Self.canActivate(
                enabled: enabled,
                windowIsVisible: window.isVisible,
                windowIgnoresMouse: window.ignoresMouseEvents,
                applicationIsActive: NSApp.isActive
              ) else {
            if active || transitionTimer != nil || systemCursorHidden {
                deactivate(at: NSEvent.mouseLocation, animated: false)
            }
            return
        }

        let point = NSEvent.mouseLocation
        if window.frame.contains(point) {
            if !active { activate(at: point) }
            livePanel.moveHotspot(to: point)
        } else if active {
            deactivate(at: point, animated: true)
        }
    }

    private func activate(at screenPoint: NSPoint) {
        guard enabled else { return }
        transitionGeneration += 1
        transitionTimer?.invalidate()
        transitionTimer = nil

        clonedCursor = NSCursor.currentSystem ?? NSCursor.current
        fakePanel.apply(cursor: clonedCursor, scale: scale)
        livePanel.apply(cursor: clonedCursor, scale: 1.0)
        fakePanel.moveHotspot(to: screenPoint)
        livePanel.moveHotspot(to: screenPoint)
        fakePanel.orderFrontRegardless()
        livePanel.orderFrontRegardless()
        hideSystemCursor()
        active = true
    }

    private func deactivate(at screenPoint: NSPoint, animated: Bool) {
        transitionGeneration += 1
        let generation = transitionGeneration
        transitionTimer?.invalidate()
        transitionTimer = nil
        active = false
        livePanel.orderOut(nil)

        guard animated, fakePanel.isVisible else {
            fakePanel.orderOut(nil)
            showSystemCursor()
            return
        }

        let start = fakePanel.hotspotPosition
        let dx = screenPoint.x - start.x
        let dy = screenPoint.y - start.y
        let distance = hypot(dx, dy)
        let duration = Self.transitionDuration(distance: distance)
        let startedAt = Date.timeIntervalSinceReferenceDate

        // Keep the real cursor hidden until the decoy reaches the exact exit point.
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
            Task { @MainActor [weak self] in
                guard let self else { timer.invalidate(); return }
                guard self.transitionGeneration == generation, !self.active else {
                    timer.invalidate()
                    return
                }
                let elapsed = Date.timeIntervalSinceReferenceDate - startedAt
                let progress = min(1.0, elapsed / duration)
                let eased = 1.0 - pow(1.0 - progress, 3.0)
                self.fakePanel.moveHotspot(to: NSPoint(
                    x: start.x + dx * eased,
                    y: start.y + dy * eased
                ))
                if progress >= 1.0 {
                    timer.invalidate()
                    self.transitionTimer = nil
                    self.fakePanel.orderOut(nil)
                    self.showSystemCursor()
                }
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        transitionTimer = timer
    }

    private func hideSystemCursor() {
        guard !systemCursorHidden else { return }
        NSCursor.hide()
        systemCursorHidden = true
    }

    private func showSystemCursor() {
        guard systemCursorHidden else { return }
        NSCursor.unhide()
        systemCursorHidden = false
    }
}

@MainActor
private final class FakeCursorPanel: NSPanel {
    private let imageView = NSImageView()
    private var hotSpot = NSPoint.zero

    init(sharingType: NSWindow.SharingType) {
        super.init(
            contentRect: NSRect(origin: .zero, size: NSSize(width: 24, height: 24)),
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

        imageView.imageScaling = .scaleAxesIndependently
        imageView.imageAlignment = .alignTopLeft
        contentView = imageView
        apply(cursor: .arrow, scale: 1.0)
    }

    var hotspotPosition: NSPoint {
        NSPoint(
            x: frame.minX + hotSpot.x,
            y: frame.minY + frame.height - hotSpot.y
        )
    }

    func apply(cursor: NSCursor, scale: Double) {
        let image = cursor.image
        let fallbackSize = NSCursor.arrow.image.size
        let sourceSize = image.size.width > 0 && image.size.height > 0 ? image.size : fallbackSize
        let factor = FakeCursorCoordinator.clampedScale(scale)
        let size = NSSize(
            width: max(1, ceil(sourceSize.width * factor)),
            height: max(1, ceil(sourceSize.height * factor))
        )
        hotSpot = NSPoint(x: cursor.hotSpot.x * factor, y: cursor.hotSpot.y * factor)
        imageView.image = image
        setContentSize(size)
        imageView.frame = NSRect(origin: .zero, size: size)
    }

    func moveHotspot(to point: NSPoint) {
        setFrameOrigin(NSPoint(
            x: point.x - hotSpot.x,
            y: point.y - (frame.height - hotSpot.y)
        ))
    }
}
