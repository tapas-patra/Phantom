import Foundation

@MainActor
final class APIRotationManager {
    enum FailureKind: Equatable { case rateLimited, authentication, retryable, terminal }

    private var rateLimited: [String: Set<Int>] = [:]
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
        let temporary = rateLimitedIndexes(provider)
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
        var temporary = rateLimitedIndexes(provider)
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
        if !temporary.isEmpty {
            temporary.removeAll()
            rateLimited[provider] = temporary
            UserDefaults.standard.removeObject(forKey: rateLimitName(provider))
            UserDefaults.standard.removeObject(forKey: rateLimitDateName(provider))
            return nextKey(provider: provider)
        }
        return nil
    }

    func markRateLimited(provider: String, index: Int) {
        rateLimited[provider, default: []].insert(index)
        UserDefaults.standard.set(Array(rateLimited[provider, default: []]), forKey: rateLimitName(provider))
        UserDefaults.standard.set(Date(), forKey: rateLimitDateName(provider))
    }
    func clearRateLimits(provider: String) {
        rateLimited[provider] = []
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
            !invalidIndexes(provider).contains($0) && !rateLimitedIndexes(provider).contains($0)
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
        rateLimited.removeAll()
    }

    func classify(_ error: Error) -> FailureKind {
        let message = error.localizedDescription.lowercased()
        let status: Int? = {
            if let value = (error as? BYOError)?.statusCode { return value }
            if let backend = error as? BackendError, case .http(let value, _) = backend { return value }
            return nil
        }()
        if status == 429 || ["rate_limit", "ratelimit", "too many requests", "quota", "resource_exhausted"].contains(where: message.contains) { return .rateLimited }
        if status == 401 || status == 403 || (message.contains("invalid") && message.contains("key")) { return .authentication }
        if [408, 500, 502, 503, 504].contains(status ?? 0)
            || ["timeout", "overloaded", "capacity", "network"].contains(where: message.contains) { return .retryable }
        return .terminal
    }

    private func clearFailures(_ provider: String) {
        rateLimited[provider] = []
        UserDefaults.standard.removeObject(forKey: invalidName(provider))
        UserDefaults.standard.removeObject(forKey: indexName(provider))
    }

    private func invalidIndexes(_ provider: String) -> Set<Int> {
        Set(UserDefaults.standard.array(forKey: invalidName(provider)) as? [Int] ?? [])
    }

    private func rateLimitedIndexes(_ provider: String) -> Set<Int> {
        if let date = UserDefaults.standard.object(forKey: rateLimitDateName(provider)) as? Date,
           Date().timeIntervalSince(date) < 60 * 60 {
            return rateLimited[provider] ?? Set(UserDefaults.standard.array(forKey: rateLimitName(provider)) as? [Int] ?? [])
        }
        clearRateLimits(provider: provider)
        return []
    }

    private func keyName(_ provider: String, _ index: Int) -> String { "provider.\(provider.lowercased()).apiKey.\(index)" }
    private func indexName(_ provider: String) -> String { "rotation.\(provider.lowercased()).lastKey" }
    private func invalidName(_ provider: String) -> String { "rotation.\(provider.lowercased()).invalidKeys" }
    private func rateLimitName(_ provider: String) -> String { "rotation.\(provider.lowercased()).rateLimitedKeys" }
    private func rateLimitDateName(_ provider: String) -> String { "rotation.\(provider.lowercased()).last429" }
}

enum RotationError: LocalizedError {
    case providerLimit
    var errorDescription: String? { "Pro BYO supports up to three configured providers." }
}
