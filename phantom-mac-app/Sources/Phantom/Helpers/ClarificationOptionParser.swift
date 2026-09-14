import Foundation

enum ClarificationOptionParser {
    struct ParseResult {
        let displayText: String
        let options: [ClarificationOption]
    }

    private static let choicePrefixes = [
        "did you mean ",
        "do you mean ",
        "are you asking about ",
        "would you like ",
        "should i "
    ]

    private static let connectors = [
        ", or were you asking about ",
        " or were you asking about ",
        ", or did you mean ",
        " or did you mean ",
        ", or "
    ]

    static func parse(_ answer: String) -> ParseResult {
        let text = answer.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return ParseResult(displayText: text, options: []) }
        let listed = extractListedOptions(text)
        if listed.options.count >= 2 { return listed }
        return ParseResult(displayText: listed.displayText, options: inferFromProse(listed.displayText))
    }

    private static func extractListedOptions(_ text: String) -> ParseResult {
        if let split = splitOptionsHeader(text) {
            let options = readListItems(split.list)
            if options.count >= 2 {
                return ParseResult(displayText: split.body, options: options)
            }
        }
        let trailing = extractTrailingList(text)
        return trailing.options.count >= 2 ? trailing : ParseResult(displayText: text, options: [])
    }

    private static func splitOptionsHeader(_ text: String) -> (body: String, list: String)? {
        let pattern = #"([\s\S]*?)\r?\nOptions:\s*\r?\n([\s\S]+)$"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
              let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              let bodyRange = Range(match.range(at: 1), in: text),
              let listRange = Range(match.range(at: 2), in: text) else { return nil }
        return (
            String(text[bodyRange]).trimmingCharacters(in: .whitespacesAndNewlines),
            String(text[listRange])
        )
    }

    private static func extractTrailingList(_ text: String) -> ParseResult {
        var lines = text.replacingOccurrences(of: "\r\n", with: "\n").split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
        var items: [String] = []
        var consumed = 0
        while let line = lines.last {
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.isEmpty, items.isEmpty {
                lines.removeLast()
                consumed += 1
                continue
            }
            guard let item = listItem(from: trimmed) else { break }
            items.insert(item, at: 0)
            lines.removeLast()
            consumed += 1
        }
        guard items.count >= 2, consumed < text.split(separator: "\n", omittingEmptySubsequences: false).count else {
            return ParseResult(displayText: text, options: [])
        }
        let body = lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        return ParseResult(displayText: body.isEmpty ? text : body, options: toOptions(items))
    }

    private static func readListItems(_ list: String) -> [ClarificationOption] {
        toOptions(list.replacingOccurrences(of: "\r\n", with: "\n").split(separator: "\n").compactMap { listItem(from: String($0)) })
    }

    private static func listItem(from line: String) -> String? {
        let pattern = #"^\s*(?:[-*•]|\d+[.)])\s+(.+?)\s*$"#
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: line, range: NSRange(line.startIndex..., in: line)),
              let range = Range(match.range(at: 1), in: line) else { return nil }
        let value = String(line[range]).trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: ".?"))
        return value.isEmpty ? nil : value
    }

    private static func inferFromProse(_ text: String) -> [ClarificationOption] {
        let lowered = text.lowercased()
        var start: String.Index?
        var prefixLength = 0
        for prefix in choicePrefixes {
            if let range = lowered.range(of: prefix, options: [.backwards]) {
                if start == nil || range.lowerBound >= start! {
                    start = range.lowerBound
                    prefixLength = prefix.count
                }
            }
        }
        let remainder: String
        if let start {
            remainder = String(text[text.index(start, offsetBy: prefixLength)...])
        } else {
            remainder = text
        }
        let parts = splitChoices(remainder)
        return parts.count >= 2 ? toOptions(parts) : []
    }

    private static func splitChoices(_ text: String) -> [String] {
        var parts: [String] = []
        var remaining = text.trimmingCharacters(in: .whitespacesAndNewlines)
        while parts.count < 3, !remaining.isEmpty {
            guard let connector = findConnector(remaining) else {
                let last = cleanChoice(remaining)
                if !last.isEmpty { parts.append(last) }
                break
            }
            let head = cleanChoice(String(remaining[..<connector.index]))
            if !head.isEmpty { parts.append(head) }
            remaining = String(remaining[remaining.index(connector.index, offsetBy: connector.length)...])
                .trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return parts
    }

    private static func findConnector(_ text: String) -> (index: String.Index, length: Int)? {
        var depth = 0
        var index = text.startIndex
        while index < text.endIndex {
            let character = text[index]
            if character == "(" {
                depth += 1
            } else if character == ")", depth > 0 {
                depth -= 1
            } else if depth == 0 {
                for connector in connectors {
                    if text[index...].lowercased().hasPrefix(connector.lowercased()) {
                        return (index, connector.count)
                    }
                }
            }
            index = text.index(after: index)
        }
        return nil
    }

    private static func cleanChoice(_ value: String) -> String {
        var trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: ",;—- "))
        while trimmed.hasSuffix("?") || trimmed.hasSuffix(".") {
            trimmed = String(trimmed.dropLast()).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        let prefixes = ["something else, like ", "something else like "]
        for prefix in prefixes where trimmed.lowercased().hasPrefix(prefix) {
            trimmed = String(trimmed.dropFirst(prefix.count)).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return trimmed
    }

    private static func toOptions(_ items: [String]) -> [ClarificationOption] {
        var options: [ClarificationOption] = []
        var seen = Set<String>()
        for item in items {
            let question = item.trimmingCharacters(in: .whitespacesAndNewlines)
            let key = question.lowercased()
            guard !question.isEmpty, seen.insert(key).inserted else { continue }
            options.append(ClarificationOption(label: makeLabel(question), question: question))
            if options.count == 3 { break }
        }
        return options.count >= 2 ? options : []
    }

    private static func makeLabel(_ question: String) -> String {
        var label = question
        if label.lowercased().hasPrefix("installing "), label.count > 12 {
            let remainder = label.dropFirst(11)
            label = remainder.prefix(1).uppercased() + String(remainder.dropFirst())
        }
        guard label.count > 72 else { return label }
        let end = label.index(label.startIndex, offsetBy: 72)
        let slice = label[..<end]
        if let space = slice.lastIndex(of: " "), label.distance(from: label.startIndex, to: space) >= 24 {
            return String(label[..<space]).trimmingCharacters(in: CharacterSet(charactersIn: ",; ")) + "…"
        }
        return String(slice).trimmingCharacters(in: CharacterSet(charactersIn: ",; ")) + "…"
    }
}
