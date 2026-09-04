import Foundation

@MainActor
final class APIRotationManager {
    private var conversationModelIndex: [String: Int] = [:]

    func keys(for provider: String) -> [String] {
        let modern = (0..<2).compactMap { index -> String? in
            guard let data = Keychain.load(keyName(provider, index)),
                  let value = String(data: data, encoding: .utf8),
                  !value.isEmpty else { return nil }
            return value
        }
        if !modern.isEmpty { return modern }
        guard let legacy = Keychain.load("provider.\(provider.lowercased()).apiKey"),
              let value = String(data: legacy, encoding: .utf8), !value.isEmpty else { return [] }
        try? Keychain.save(legacy, key: keyName(provider, 0))
        Keychain.delete("provider.\(provider.lowercased()).apiKey")
        return [value]
    }

    func save(provider: String, keys: [String]) throws {
        let clean = Array(keys.map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }.prefix(2))
        let alreadyConfigured = BYOCatalog.providers.filter { !self.keys(for: $0.providerId).isEmpty }.map(\.providerId)
        guard !clean.isEmpty || alreadyConfigured.contains(provider) else { return }
        guard alreadyConfigured.contains(provider) || alreadyConfigured.count < 3 else {
            throw RotationError.providerLimit
        }
        for index in 0..<2 {
            if index < clean.count { try Keychain.save(Data(clean[index].utf8), key: keyName(provider, index)) }
            else { Keychain.delete(keyName(provider, index)) }
        }
        clearFailures(provider)
    }

    func remove(provider: String) {
        for index in 0..<2 { Keychain.delete(keyName(provider, index)) }
        clearFailures(provider)
    }

    func currentKey(provider: String) -> (index: Int, value: String)? {
        let values = keys(for: provider)
        guard !values.isEmpty else { return nil }
        let invalid = invalidIndexes(provider)
        let temporary = cooldownIndexes(provider)
        let saved = UserDefaults.standard.integer(forKey: indexName(provider))
        if values.indices.contains(saved), !invalid.contains(saved), !temporary.contains(saved) {
            return (saved, values[saved])
        }
        return nextKey(provider: provider)
    }

    func nextKey(provider: String) -> (index: Int, value: String)? {
        let values = keys(for: provider)
        guard !values.isEmpty else { return nil }
        let invalid = invalidIndexes(provider)
        let temporary = cooldownIndexes(provider)
        let last = UserDefaults.standard.object(forKey: indexName(provider)) == nil
            ? -1
            : UserDefaults.standard.integer(forKey: indexName(provider))
        for offset in 1...values.count {
            let index = (last + offset) % values.count
            if !invalid.contains(index), !temporary.contains(index) {
                UserDefaults.standard.set(index, forKey: indexName(provider))
                return (index, values[index])
            }
        }
        return nil
    }

    func markRateLimited(provider: String, index: Int) {
        markCooldown(provider: provider, index: index, seconds: 5 * 60)
    }
    func clearRateLimits(provider: String) {
        UserDefaults.standard.removeObject(forKey: cooldownName(provider))
        UserDefaults.standard.removeObject(forKey: rateLimitName(provider))
        UserDefaults.standard.removeObject(forKey: rateLimitDateName(provider))
    }

    func markInvalid(provider: String, index: Int) {
        var values = invalidIndexes(provider)
        values.insert(index)
        UserDefaults.standard.set(Array(values), forKey: invalidName(provider))
    }

    func availableKeyCount(provider: String) -> Int {
        let count = keys(for: provider).count
        return (0..<count).filter {
            !invalidIndexes(provider).contains($0) && !cooldownIndexes(provider).contains($0)
        }.count
    }

    func keyPosition(provider: String) -> String {
        let total = keys(for: provider).count
        guard total > 0 else { return "No key" }
        return "Key \((currentKey(provider: provider)?.index ?? 0) + 1)/\(total)"
    }

    func model(provider: String, models: [String], selected: String) -> String {
        guard !models.isEmpty else { return selected }
        if let index = conversationModelIndex[provider], models.indices.contains(index) { return models[index] }
        let index = models.firstIndex(of: selected) ?? 0
        conversationModelIndex[provider] = index
        return models[index]
    }

    func nextModel(provider: String, models: [String], selected: String) -> String? {
        guard models.count > 1 else { return nil }
        let current = conversationModelIndex[provider] ?? models.firstIndex(of: selected) ?? 0
        let next = (current + 1) % models.count
        conversationModelIndex[provider] = next
        return models[next]
    }

    func resetConversation() {
        conversationModelIndex.removeAll()
    }

    func classify(_ error: Error) -> ProviderFailureDecision {
        let status: Int? = {
            if let value = (error as? BYOError)?.statusCode { return value }
            if let backend = error as? BackendError, case .http(let value, _) = backend { return value }
            return nil
        }()
        return ProviderResiliencePolicy.classify(statusCode: status, message: error.localizedDescription)
    }

    func recordFailure(provider: String, index: Int, decision: ProviderFailureDecision) {
        switch decision.kind {
        case .authentication: markInvalid(provider: provider, index: index)
        case .rateLimited, .transient: markCooldown(provider: provider, index: index, seconds: decision.cooldown)
        case .terminal, .cancelled: break
        }
    }

    func recordSuccess(provider: String, index: Int) {
        var values = cooldowns(provider)
        if values.removeValue(forKey: String(index)) != nil {
            UserDefaults.standard.set(values, forKey: cooldownName(provider))
        }
    }

    private func clearFailures(_ provider: String) {
        UserDefaults.standard.removeObject(forKey: cooldownName(provider))
        UserDefaults.standard.removeObject(forKey: rateLimitName(provider))
        UserDefaults.standard.removeObject(forKey: rateLimitDateName(provider))
        UserDefaults.standard.removeObject(forKey: invalidName(provider))
        UserDefaults.standard.removeObject(forKey: indexName(provider))
    }

    private func invalidIndexes(_ provider: String) -> Set<Int> {
        Set(UserDefaults.standard.array(forKey: invalidName(provider)) as? [Int] ?? [])
    }

    private func cooldownIndexes(_ provider: String) -> Set<Int> {
        var values = cooldowns(provider)
        if values.isEmpty,
           let legacy = UserDefaults.standard.array(forKey: rateLimitName(provider)) as? [Int],
           let date = UserDefaults.standard.object(forKey: rateLimitDateName(provider)) as? Date {
            let expiry = date.addingTimeInterval(5 * 60).timeIntervalSince1970
            legacy.forEach { values[String($0)] = expiry }
            UserDefaults.standard.removeObject(forKey: rateLimitName(provider))
            UserDefaults.standard.removeObject(forKey: rateLimitDateName(provider))
        }
        let now = Date().timeIntervalSince1970
        values = values.filter { $0.value > now }
        UserDefaults.standard.set(values, forKey: cooldownName(provider))
        return Set(values.keys.compactMap(Int.init))
    }

    private func markCooldown(provider: String, index: Int, seconds: TimeInterval) {
        var values = cooldowns(provider)
        values[String(index)] = Date().addingTimeInterval(seconds).timeIntervalSince1970
        UserDefaults.standard.set(values, forKey: cooldownName(provider))
    }

    private func cooldowns(_ provider: String) -> [String: Double] {
        UserDefaults.standard.dictionary(forKey: cooldownName(provider)) as? [String: Double] ?? [:]
    }

    private func keyName(_ provider: String, _ index: Int) -> String { "provider.\(provider.lowercased()).apiKey.\(index)" }
    private func indexName(_ provider: String) -> String { "rotation.\(provider.lowercased()).lastKey" }
    private func invalidName(_ provider: String) -> String { "rotation.\(provider.lowercased()).invalidKeys" }
    private func cooldownName(_ provider: String) -> String { "rotation.\(provider.lowercased()).cooldowns" }
    private func rateLimitName(_ provider: String) -> String { "rotation.\(provider.lowercased()).rateLimitedKeys" }
    private func rateLimitDateName(_ provider: String) -> String { "rotation.\(provider.lowercased()).last429" }
}

enum RotationError: LocalizedError {
    case providerLimit
    var errorDescription: String? { "Pro BYO supports up to three configured providers." }
}
