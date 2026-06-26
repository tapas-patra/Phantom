using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Context
{
    public static class KnowledgeRetrievalQueryRouter
    {
        private static readonly string[] ExplicitContextPhrases =
        {
            "based on my resume",
            "from my resume",
            "from the resume",
            "using my resume",
            "using my experience",
            "using my background",
            "my experience",
            "my background",
            "my project",
            "my projects",
            "my notes",
            "my document",
            "my documents",
            "knowledge base",
            "this company",
            "the company i am interviewing",
            "this role",
            "this job",
            "job description",
            "according to my"
        };

        private static readonly string[] GenericCodeMarkers =
        {
            "write code",
            "write a program",
            "python code",
            "java code",
            "c++ code",
            "javascript code",
            "sql query",
            "leetcode",
            "algorithm",
            "implement ",
            "debug this code",
            "solve this",
            "syntax"
        };

        private static readonly string[] GenericConceptPrefixes =
        {
            "what is ",
            "explain ",
            "difference between ",
            "how does ",
            "compare ",
            "pros and cons ",
            "when should ",
            "why does ",
            "define "
        };

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "about", "after", "again", "against", "also", "because", "before", "being",
            "between", "could", "first", "from", "have", "into", "itself", "other",
            "should", "their", "there", "these", "those", "under", "using", "what",
            "when", "where", "which", "while", "with", "would", "your", "this", "that",
            "they", "them", "then", "than", "just", "like", "into", "role", "company",
            "resume", "interview", "project", "projects", "notes", "document", "documents"
        };

        public static bool ShouldRetrieve(ContextPack pack, string query, out string reason)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                reason = "empty-query";
                return false;
            }

            var normalizedQuery = Normalize(query);
            if (ContainsAny(normalizedQuery, ExplicitContextPhrases))
            {
                reason = "explicit-context";
                return true;
            }

            if (LooksLikeGenericCodingQuestion(normalizedQuery) || LooksLikeGenericConceptQuestion(normalizedQuery))
            {
                reason = "generic-direct";
                return false;
            }

            if (MatchesContextDocumentTitle(pack, normalizedQuery))
            {
                reason = "document-title-match";
                return true;
            }

            if (MatchesDistinctContextToken(pack, normalizedQuery))
            {
                reason = "context-token-match";
                return true;
            }

            reason = "default-skip";
            return false;
        }

        private static bool LooksLikeGenericCodingQuestion(string normalizedQuery)
        {
            return ContainsAny(normalizedQuery, GenericCodeMarkers);
        }

        private static bool LooksLikeGenericConceptQuestion(string normalizedQuery)
        {
            var trimmedQuery = normalizedQuery.TrimStart();
            return GenericConceptPrefixes.Any(prefix => trimmedQuery.StartsWith(prefix, StringComparison.Ordinal))
                && !normalizedQuery.Contains(" my ", StringComparison.Ordinal);
        }

        private static bool MatchesContextDocumentTitle(ContextPack pack, string normalizedQuery)
        {
            if (pack == null)
            {
                return false;
            }

            return pack.Documents
                .Select(document => Normalize(document.Title))
                .Where(title => title.Length >= 4)
                .Any(title => normalizedQuery.Contains(title, StringComparison.Ordinal));
        }

        private static bool MatchesDistinctContextToken(ContextPack pack, string normalizedQuery)
        {
            if (pack == null)
            {
                return false;
            }

            var queryTokens = Tokenize(normalizedQuery);
            if (queryTokens.Count == 0)
            {
                return false;
            }

            var contextTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddContextTokens(contextTokens, pack.ResumeSummary);
            AddContextTokens(contextTokens, pack.JobDescriptionSummary);
            foreach (var document in pack.Documents.Take(8))
            {
                AddContextTokens(contextTokens, document.Title);
            }

            return queryTokens.Any(contextTokens.Contains);
        }

        private static void AddContextTokens(HashSet<string> target, string value)
        {
            foreach (var token in Tokenize(value))
            {
                if (!StopWords.Contains(token))
                {
                    target.Add(token);
                }
            }
        }

        private static List<string> Tokenize(string value)
        {
            return Regex.Split(value ?? string.Empty, @"[^a-z0-9+#.]+")
                .Where(token => token.Length >= 5)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ContainsAny(string value, IEnumerable<string> candidates)
        {
            return candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
        }

        private static string Normalize(string value)
        {
            return $" {Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"\s+", " ").Trim()} ";
        }
    }
}
