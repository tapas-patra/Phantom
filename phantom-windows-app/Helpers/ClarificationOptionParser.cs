using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SecureOverlay.Helpers
{
    public static class ClarificationOptionParser
    {
        public readonly record struct ParsedOption(string Label, string Question);
        public readonly record struct ParseResult(string DisplayText, IReadOnlyList<ParsedOption> Options);

        private static readonly Regex OptionsHeader = new(
            @"^(?<body>[\s\S]*?)\r?\nOptions:\s*\r?\n(?<list>[\s\S]+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ListItem = new(
            @"^\s*(?:[-*•]|\d+[.)])\s+(.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly string[] ChoicePrefixes =
        {
            "did you mean ",
            "do you mean ",
            "are you asking about ",
            "would you like ",
            "should i "
        };

        private static readonly string[] Connectors =
        {
            ", or were you asking about ",
            " or were you asking about ",
            ", or did you mean ",
            " or did you mean ",
            ", or "
        };

        public static ParseResult Parse(string? answer)
        {
            var text = (answer ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return new ParseResult(text, Array.Empty<ParsedOption>());
            }

            var listed = ExtractListedOptions(text);
            if (listed.Options.Count >= 2)
            {
                return listed;
            }

            var inferred = InferFromProse(listed.DisplayText);
            return new ParseResult(listed.DisplayText, inferred);
        }

        private static ParseResult ExtractListedOptions(string text)
        {
            var match = OptionsHeader.Match(text);
            if (match.Success)
            {
                var options = ReadListItems(match.Groups["list"].Value);
                if (options.Count >= 2)
                {
                    return new ParseResult(match.Groups["body"].Value.Trim(), options);
                }
            }

            var trailing = ExtractTrailingList(text);
            return trailing.Options.Count >= 2 ? trailing : new ParseResult(text, Array.Empty<ParsedOption>());
        }

        private static ParseResult ExtractTrailingList(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var items = new List<string>();
            var consumed = 0;
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) && items.Count == 0)
                {
                    consumed++;
                    continue;
                }

                var itemMatch = ListItem.Match(line);
                if (!itemMatch.Success)
                {
                    break;
                }

                items.Insert(0, itemMatch.Groups[1].Value.Trim());
                consumed++;
            }

            if (items.Count < 2 || consumed >= lines.Length)
            {
                return new ParseResult(text, Array.Empty<ParsedOption>());
            }

            var body = string.Join("\n", lines[..^consumed]).Trim();
            return new ParseResult(string.IsNullOrEmpty(body) ? text : body, ToOptions(items));
        }

        private static IReadOnlyList<ParsedOption> ReadListItems(string list)
        {
            var items = new List<string>();
            foreach (Match match in ListItem.Matches(list))
            {
                var value = match.Groups[1].Value.Trim().TrimEnd('.', '?');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    items.Add(value);
                }
            }

            return ToOptions(items);
        }

        private static IReadOnlyList<ParsedOption> InferFromProse(string text)
        {
            var lowered = text.ToLowerInvariant();
            var start = -1;
            var prefixLength = 0;
            foreach (var prefix in ChoicePrefixes)
            {
                var at = lowered.LastIndexOf(prefix, StringComparison.Ordinal);
                if (at >= 0 && at >= start)
                {
                    start = at;
                    prefixLength = prefix.Length;
                }
            }

            var remainder = start >= 0
                ? text[(start + prefixLength)..]
                : text;
            var parts = SplitChoices(remainder);
            return parts.Count >= 2 ? ToOptions(parts) : Array.Empty<ParsedOption>();
        }

        private static List<string> SplitChoices(string text)
        {
            var parts = new List<string>();
            var remaining = text.Trim();
            while (parts.Count < 3 && remaining.Length > 0)
            {
                var connector = FindConnector(remaining);
                if (connector == null)
                {
                    var last = CleanChoice(remaining);
                    if (!string.IsNullOrEmpty(last))
                    {
                        parts.Add(last);
                    }
                    break;
                }

                var head = CleanChoice(remaining[..connector.Value.Index]);
                if (!string.IsNullOrEmpty(head))
                {
                    parts.Add(head);
                }

                remaining = remaining[(connector.Value.Index + connector.Value.Length)..].Trim();
            }

            return parts;
        }

        private static (int Index, int Length)? FindConnector(string text)
        {
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '(')
                {
                    depth++;
                    continue;
                }

                if (ch == ')' && depth > 0)
                {
                    depth--;
                    continue;
                }

                if (depth != 0)
                {
                    continue;
                }

                foreach (var connector in Connectors)
                {
                    if (i + connector.Length <= text.Length
                        && text.AsSpan(i, connector.Length).Equals(connector.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        return (i, connector.Length);
                    }
                }
            }

            return null;
        }

        private static string CleanChoice(string value)
        {
            var trimmed = value.Trim().Trim(',', ';', '—', '-', ' ');
            while (trimmed.EndsWith('?') || trimmed.EndsWith('.'))
            {
                trimmed = trimmed[..^1].Trim();
            }

            if (trimmed.StartsWith("something else, like ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed["something else, like ".Length..].Trim();
            }
            else if (trimmed.StartsWith("something else like ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed["something else like ".Length..].Trim();
            }

            return trimmed;
        }

        private static IReadOnlyList<ParsedOption> ToOptions(IReadOnlyList<string> items)
        {
            var options = new List<ParsedOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var question = item.Trim();
                if (question.Length == 0 || !seen.Add(question))
                {
                    continue;
                }

                options.Add(new ParsedOption(MakeLabel(question), question));
                if (options.Count == 3)
                {
                    break;
                }
            }

            return options.Count >= 2 ? options : Array.Empty<ParsedOption>();
        }

        private static string MakeLabel(string question)
        {
            var label = question;
            if (label.StartsWith("installing ", StringComparison.OrdinalIgnoreCase) && label.Length > 12)
            {
                label = char.ToUpperInvariant(label[11]) + label[12..];
            }

            if (label.Length <= 72)
            {
                return label;
            }

            var cut = label.LastIndexOf(' ', 72);
            if (cut < 24)
            {
                cut = 72;
            }

            return label[..cut].TrimEnd(',', ';', ' ') + "…";
        }
    }
}
