import Foundation

enum ProviderFailureKind: String {
    case rateLimited = "rate_limited"
    case authentication = "authentication_failed"
    case transient = "provider_transient"
    case terminal = "provider_error"
    case cancelled
}

struct ProviderFailureDecision {
    let kind: ProviderFailureKind
    let canRotateCredential: Bool
    let cooldown: TimeInterval
}

enum ProviderResiliencePolicy {
    static let managedDesktopMaxAttempts = 1
    static let byoDesktopMaxAttempts = 2

    static func classify(statusCode: Int? = nil, message: String = "") -> ProviderFailureDecision {
        let value = message.lowercased()
        if statusCode == 429 || contains(value, ["rate_limit", "ratelimit", "too many requests", "quota", "resource_exhausted"]) {
            return ProviderFailureDecision(kind: .rateLimited, canRotateCredential: true, cooldown: 5 * 60)
        }
        if statusCode == 401 || statusCode == 403 || contains(value, ["invalid api key", "invalid key", "unauthorized", "authentication"]) {
            return ProviderFailureDecision(kind: .authentication, canRotateCredential: true, cooldown: 24 * 60 * 60)
        }
        if contains(value, ["cancelled", "canceled"]) {
            return ProviderFailureDecision(kind: .cancelled, canRotateCredential: false, cooldown: 0)
        }
        if [408, 500, 502, 503, 504].contains(statusCode ?? 0)
            || contains(value, ["timeout", "timed out", "overloaded", "capacity", "network", "transport", "connection", "stream ended unexpectedly", "empty response"]) {
            return ProviderFailureDecision(kind: .transient, canRotateCredential: true, cooldown: 30)
        }
        return ProviderFailureDecision(kind: .terminal, canRotateCredential: false, cooldown: 0)
    }

    static func canRetry(_ failure: ProviderFailureDecision, attempt: Int, maxAttempts: Int, hasOutput: Bool) -> Bool {
        !hasOutput && attempt < maxAttempts && failure.canRotateCredential
    }

    static func canCrossLane(from: String, to: String, explicitlyOptedIn: Bool, hasOutput: Bool) -> Bool {
        from.caseInsensitiveCompare("byo") == .orderedSame
            && to.caseInsensitiveCompare("managed_extension") == .orderedSame
            && explicitlyOptedIn
            && !hasOutput
    }

    private static func contains(_ value: String, _ terms: [String]) -> Bool {
        terms.contains(where: value.contains)
    }
}
