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
            ContextPack pack, string query, IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3, CancellationToken cancellationToken = default)
        {
            if (pack == null || pack.Documents.Count == 0 || string.IsNullOrWhiteSpace(query))
                return Task.FromResult<IReadOnlyList<RetrievedContextSnippet>>(Array.Empty<RetrievedContextSnippet>());
            var terms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (terms.Count == 0)
                return Task.FromResult<IReadOnlyList<RetrievedContextSnippet>>(Array.Empty<RetrievedContextSnippet>());
            var preferred = preferredDocumentIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var result = pack.Documents
                .Where(document => preferred.Count == 0 || preferred.Contains(document.DocumentId))
                .SelectMany(document => document.Chunks.Select(chunk => new RetrievedContextSnippet
                {
                    DocumentId = document.DocumentId,
                    DocumentTitle = document.Title,
                    SourceType = document.SourceType,
                    Text = chunk.Text,
                    Score = terms.Count(term => chunk.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))
                }))
                .Where(snippet => snippet.Score > 0)
                .OrderByDescending(snippet => snippet.Score)
                .ThenBy(snippet => snippet.DocumentTitle)
                .Take(Math.Max(1, maxSnippets))
                .ToList();
            return Task.FromResult<IReadOnlyList<RetrievedContextSnippet>>(result);
        }

        private static IEnumerable<string> Tokenize(string value) =>
            Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9+#.]+" ).Where(token => token.Length >= 3);
    }
}
