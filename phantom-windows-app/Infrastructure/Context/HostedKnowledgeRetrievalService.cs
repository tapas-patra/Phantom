using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Context
{
    public sealed class HostedKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        private static readonly TimeSpan HostedKnowledgeRefreshTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan HostedKnowledgeSearchTimeout = TimeSpan.FromMilliseconds(4000);

        private readonly IKnowledgeRetrievalService _localFallback;
        private readonly IAuthSessionRepository _sessions;
        private readonly IAccountCacheRepository _accounts;
        private readonly IHostedAccountClient _hostedAccountClient;

        public HostedKnowledgeRetrievalService(
            IKnowledgeRetrievalService localFallback,
            IAuthSessionRepository sessions,
            IAccountCacheRepository accounts,
            IHostedAccountClient hostedAccountClient)
        {
            _localFallback = localFallback;
            _sessions = sessions;
            _accounts = accounts;
            _hostedAccountClient = hostedAccountClient;
        }

        public async Task<System.Collections.Generic.IReadOnlyList<RetrievedContextSnippet>> RetrieveForPromptAsync(
            ContextPack pack,
            string query,
            System.Collections.Generic.IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3,
            CancellationToken cancellationToken = default)
        {
            // ponytail: planner owns the routing decision; retrieval just executes the rewritten query.
            var accountSnapshot = _accounts.Load();
            var session = _sessions.Load();
            var hostedKnowledgeBase = accountSnapshot?.HostedKnowledgeBase;
            RagTraceLogger.WriteLine(
                $"hosted_retrieval:start query='{TrimForLog(query, 240)}' preferred_docs={FormatDocumentIds(preferredDocumentIds)} kb_present={hostedKnowledgeBase != null} kb_status='{hostedKnowledgeBase?.Status ?? "(none)"}' can_use={hostedKnowledgeBase?.CanUseInInterview ?? false} local_docs={pack?.Documents?.Count ?? 0}");

            if (accountSnapshot != null
                && session != null
                && !string.IsNullOrWhiteSpace(session.AccessToken)
                && (hostedKnowledgeBase == null
                    || (!hostedKnowledgeBase.CanUseInInterview
                        && string.Equals(accountSnapshot.AccessTier, "premium", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    RagTraceLogger.WriteLine("hosted_retrieval:refreshing_kb_status");
                    hostedKnowledgeBase = await RunWithDeadlineAsync(
                        ct => _hostedAccountClient.GetKnowledgeBaseAsync(session.AccessToken, ct),
                        HostedKnowledgeRefreshTimeout,
                        cancellationToken);
                    accountSnapshot.HostedKnowledgeBase = hostedKnowledgeBase;
                    _accounts.Save(accountSnapshot);
                    RagTraceLogger.WriteLine(
                        $"hosted_retrieval:refresh_success status='{hostedKnowledgeBase?.Status ?? "(none)"}' can_use={hostedKnowledgeBase?.CanUseInInterview ?? false} docs={hostedKnowledgeBase?.DocumentCount ?? 0} chunks={hostedKnowledgeBase?.ChunkCount ?? 0}");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Log.WriteLine("Hosted KB status refresh hit the live-search deadline.");
                    RagTraceLogger.WriteLine("hosted_retrieval:refresh_timeout");
                }
                catch (HostedServiceException ex)
                {
                    Log.WriteLine($"Hosted KB status refresh failed: {ex.Message}");
                    RagTraceLogger.WriteLine($"hosted_retrieval:refresh_error message='{TrimForLog(ex.Message, 240)}'");
                }
            }

            if (hostedKnowledgeBase == null
                || !hostedKnowledgeBase.CanUseInInterview
                || string.IsNullOrWhiteSpace(session?.AccessToken))
            {
                RagTraceLogger.WriteLine(
                    $"hosted_retrieval:fallback_local reason='{BuildLocalFallbackReason(hostedKnowledgeBase, session?.AccessToken)}'");
                return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);
            }

            try
            {
                RagTraceLogger.WriteLine("hosted_retrieval:searching_hosted_kb");
                var result = await RunWithDeadlineAsync(
                    ct => _hostedAccountClient.SearchKnowledgeBaseAsync(session.AccessToken, query, preferredDocumentIds, maxSnippets, ct),
                    HostedKnowledgeSearchTimeout,
                    cancellationToken);
                if (result.Snippets == null || result.Snippets.Count == 0)
                {
                    RagTraceLogger.WriteLine("hosted_retrieval:no_hosted_hits fallback_local=true");
                    return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);
                }

                var snippets = result.Snippets
                    .Select(item => new RetrievedContextSnippet
                    {
                        DocumentId = item.DocumentId,
                        DocumentTitle = item.DocumentTitle,
                        SourceType = "hosted_kb",
                        Text = item.Text,
                        Score = (int)Math.Round(item.Score * 100d)
                    })
                    .ToList();
                RagTraceLogger.WriteLine(
                    $"hosted_retrieval:hosted_hits count={snippets.Count} docs={string.Join(", ", snippets.Select(snippet => TrimForLog(snippet.DocumentTitle, 80)))}");
                return snippets;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.WriteLine("Hosted KB retrieval hit the live-search deadline. Falling back to local context.");
                RagTraceLogger.WriteLine("hosted_retrieval:search_timeout fallback_local=true");
                return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted KB retrieval failed, using local fallback: {ex.Message}");
                RagTraceLogger.WriteLine($"hosted_retrieval:search_error message='{TrimForLog(ex.Message, 240)}' fallback_local=true");
                return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);
            }
        }

        private static async Task<T> RunWithDeadlineAsync<T>(
            Func<CancellationToken, Task<T>> action,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            return await action(cts.Token).ConfigureAwait(false);
        }

        private static string BuildLocalFallbackReason(HostedKnowledgeBaseSummaryDto? hostedKnowledgeBase, string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return "missing_access_token";
            }

            if (hostedKnowledgeBase == null)
            {
                return "missing_kb_snapshot";
            }

            if (!hostedKnowledgeBase.CanUseInInterview)
            {
                return $"kb_not_usable:{hostedKnowledgeBase.Status}";
            }

            return "unknown";
        }

        private static string FormatDocumentIds(System.Collections.Generic.IReadOnlyList<string>? documentIds)
        {
            if (documentIds == null || documentIds.Count == 0)
            {
                return "(none)";
            }

            return string.Join(", ", documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Take(6));
        }

        private static string TrimForLog(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = string.Join(" ", value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return normalized.Length <= maxLength
                ? normalized
                : normalized.Substring(0, maxLength) + "...";
        }
    }
}
