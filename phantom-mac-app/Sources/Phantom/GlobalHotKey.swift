import Carbon
import Foundation

@MainActor
final class GlobalHotKey {
    private var hotKey: EventHotKeyRef?
    private var eventHandler: EventHandlerRef?
    private let id: UInt32
    private let action: () -> Void

    init(keyCode: UInt32 = UInt32(kVK_ANSI_Grave), modifiers: UInt32 = UInt32(cmdKey | controlKey), id: UInt32 = 1, action: @escaping () -> Void) {
        self.id = id
        self.action = action
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )
        InstallEventHandler(
            GetApplicationEventTarget(),
            { _, event, userData in
                guard let event, let userData else { return OSStatus(eventNotHandledErr) }
                let monitor = Unmanaged<GlobalHotKey>.fromOpaque(userData).takeUnretainedValue()
                var pressed = EventHotKeyID()
                guard GetEventParameter(
                    event,
                    EventParamName(kEventParamDirectObject),
                    EventParamType(typeEventHotKeyID),
                    nil,
                    MemoryLayout<EventHotKeyID>.size,
                    nil,
                    &pressed
                ) == noErr, GlobalHotKey.handles(registeredID: monitor.id, eventID: pressed.id) else {
                    return OSStatus(eventNotHandledErr)
                }
                DispatchQueue.main.async { monitor.action() }
                return noErr
            },
            1,
            &eventType,
            Unmanaged.passUnretained(self).toOpaque(),
            &eventHandler
        )

        let identifier = EventHotKeyID(signature: OSType(0x50484E54), id: id) // PHNT
        RegisterEventHotKey(
            keyCode,
            modifiers,
            identifier,
            GetApplicationEventTarget(),
            0,
            &hotKey
        )
    }

    static func handles(registeredID: UInt32, eventID: UInt32) -> Bool {
        registeredID == eventID
    }

    deinit {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        if let eventHandler { RemoveEventHandler(eventHandler) }
    }
}
