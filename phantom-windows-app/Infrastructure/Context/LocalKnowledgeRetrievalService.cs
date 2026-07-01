using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Application.Context;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Context
{
    public sealed class LocalKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        public Task<IReadOnlyList<RetrievedContextSnippet>> RetrieveForPromptAsync(
            ContextPack pack,
            string query,
            IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3,
            CancellationToken cancellationToken = default)
        {
            if (pack == null || pack.Documents.Count == 0 || string.IsNullOrWhiteSpace(query))
            {
                RagTraceLogger.WriteLine(
                    $"local_retrieval:skip query='{TrimForLog(query, 180)}' doc_count={pack?.Documents?.Count ?? 0}");
                return Task.FromResult<IReadOnlyList<RetrievedContextSnippet>>(Array.Empty<RetrievedContextSnippet>());
            }

            var terms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (terms.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<RetrievedContextSnippet>>(Array.Empty<RetrievedContextSnippet>());
            }

            IReadOnlyList<RetrievedContextSnippet> snippets = pack.Documents
                .SelectMany(document => document.Chunks.Select(chunk => new RetrievedContextSnippet
                {
                    DocumentId = document.DocumentId,
                    DocumentTitle = document.Title,
                    SourceType = document.SourceType,
                    Text = chunk.Text,
                    Score = ScoreChunk(chunk.SearchText, terms)
                }))
                .Where(snippet => snippet.Score > 0)
                .OrderByDescending(snippet => snippet.Score)
                .ThenBy(snippet => snippet.DocumentTitle)
                .Take(Math.Max(1, maxSnippets))
                .ToList();

            RagTraceLogger.WriteLine(
                $"local_retrieval:result query='{TrimForLog(query, 180)}' terms={terms.Count} doc_count={pack.Documents.Count} hits={snippets.Count} docs={string.Join(", ", snippets.Select(snippet => TrimForLog(snippet.DocumentTitle, 80)))}");

            return Task.FromResult(snippets);
        }

        private static int ScoreChunk(string searchText, List<string> terms)
        {
            var score = 0;
            foreach (var term in terms)
            {
                if (searchText.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                }
            }

            return score;
        }

        private static IEnumerable<string> Tokenize(string value)
        {
            return Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9+#.]+" )
                .Where(token => token.Length >= 3);
        }

        private static string TrimForLog(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value, "\\s+", " ").Trim();
            return normalized.Length <= maxLength
                ? normalized
                : normalized.Substring(0, maxLength) + "...";
        }
    }
}
