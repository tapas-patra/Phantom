using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted;

namespace SecureOverlay.Infrastructure.Context
{
    public sealed class HostedKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        private static readonly TimeSpan HostedKnowledgeRefreshTimeout = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan HostedKnowledgeSearchTimeout = TimeSpan.FromMilliseconds(600);

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
            int maxSnippets = 3,
            CancellationToken cancellationToken = default)
        {
            if (!KnowledgeRetrievalQueryRouter.ShouldRetrieve(pack, query, out var routingReason))
            {
                Log.WriteLine($"Skipping knowledge retrieval for query. Reason={routingReason}");
                return Array.Empty<RetrievedContextSnippet>();
            }

            var accountSnapshot = _accounts.Load();
            var session = _sessions.Load();
            var hostedKnowledgeBase = accountSnapshot?.HostedKnowledgeBase;

            if (accountSnapshot != null
                && session != null
                && !string.IsNullOrWhiteSpace(session.AccessToken)
                && (hostedKnowledgeBase == null
                    || (!hostedKnowledgeBase.CanUseInInterview
                        && string.Equals(accountSnapshot.AccessTier, "premium", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    hostedKnowledgeBase = await RunWithDeadlineAsync(
                        ct => _hostedAccountClient.GetKnowledgeBaseAsync(session.AccessToken, ct),
                        HostedKnowledgeRefreshTimeout,
                        cancellationToken);
                    accountSnapshot.HostedKnowledgeBase = hostedKnowledgeBase;
                    _accounts.Save(accountSnapshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Log.WriteLine("Hosted KB status refresh hit the live-search deadline.");
                }
                catch (HostedServiceException ex)
                {
                    Log.WriteLine($"Hosted KB status refresh failed: {ex.Message}");
                }
            }

            if (hostedKnowledgeBase == null
                || !hostedKnowledgeBase.CanUseInInterview
                || string.IsNullOrWhiteSpace(session?.AccessToken))
            {
                return await _localFallback.RetrieveForPromptAsync(pack, query, maxSnippets, cancellationToken);
            }

            try
            {
                var result = await RunWithDeadlineAsync(
                    ct => _hostedAccountClient.SearchKnowledgeBaseAsync(session.AccessToken, query, maxSnippets, ct),
                    HostedKnowledgeSearchTimeout,
                    cancellationToken);
                if (result.Snippets == null || result.Snippets.Count == 0)
                {
                    return await _localFallback.RetrieveForPromptAsync(pack, query, maxSnippets, cancellationToken);
                }

                return result.Snippets
                    .Select(item => new RetrievedContextSnippet
                    {
                        DocumentTitle = item.DocumentTitle,
                        SourceType = "hosted_kb",
                        Text = item.Text,
                        Score = (int)Math.Round(item.Score * 100d)
                    })
                    .ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.WriteLine("Hosted KB retrieval hit the live-search deadline. Falling back to local context.");
                return await _localFallback.RetrieveForPromptAsync(pack, query, maxSnippets, cancellationToken);
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted KB retrieval failed, using local fallback: {ex.Message}");
                return await _localFallback.RetrieveForPromptAsync(pack, query, maxSnippets, cancellationToken);
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
    }
}
