import Foundation

enum UserFacingText {
    /// Strips URLs and `/api/` paths from strings shown in the UI (not Diagnostics logs).
    static func sanitize(_ raw: String) -> String {
        var value = raw
        if let detector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.link.rawValue) {
            let range = NSRange(value.startIndex..<value.endIndex, in: value)
            value = detector.stringByReplacingMatches(
                in: value,
                options: [],
                range: range,
                withTemplate: ""
            )
        }
        value = value.replacingOccurrences(
            of: #"(?i)/api/[A-Za-z0-9._~:/?#\[\]@!$&'()*+,;=%-]+"#,
            with: "",
            options: .regularExpression
        )
        value = value.replacingOccurrences(of: #"\s{2,}"#, with: " ", options: .regularExpression)
        return value.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

enum CatalogRefreshQuota {
    static let maxAutomaticPerDay = 2
    private static let dayKey = "catalog.refresh.day"
    private static let countKey = "catalog.refresh.autoCount"

    static func canAutoRefresh(now: Date = Date()) -> Bool {
        let day = dayString(now)
        guard UserDefaults.standard.string(forKey: dayKey) == day else { return true }
        return UserDefaults.standard.integer(forKey: countKey) < maxAutomaticPerDay
    }

    static func recordAutoRefresh(now: Date = Date()) {
        let day = dayString(now)
        if UserDefaults.standard.string(forKey: dayKey) != day {
            UserDefaults.standard.set(day, forKey: dayKey)
            UserDefaults.standard.set(1, forKey: countKey)
        } else {
            UserDefaults.standard.set(UserDefaults.standard.integer(forKey: countKey) + 1, forKey: countKey)
        }
    }

    private static func dayString(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.calendar = Calendar.current
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone.current
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter.string(from: date)
    }
}
