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
        private static readonly TimeSpan RefreshTimeout = TimeSpan.FromMilliseconds(500);
        // Live interview search budget: embedding + lexical/hybrid query.
        private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(8);
        private readonly IKnowledgeRetrievalService _localFallback;
        private readonly IAuthSessionRepository _sessions;
        private readonly IAccountCacheRepository _accounts;
        private readonly IHostedAccountClient _hosted;

        public HostedKnowledgeRetrievalService(
            IKnowledgeRetrievalService localFallback, IAuthSessionRepository sessions,
            IAccountCacheRepository accounts, IHostedAccountClient hosted)
        {
            _localFallback = localFallback;
            _sessions = sessions;
            _accounts = accounts;
            _hosted = hosted;
        }

        public async Task<System.Collections.Generic.IReadOnlyList<RetrievedContextSnippet>> RetrieveForPromptAsync(
            ContextPack pack, string query, System.Collections.Generic.IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3, CancellationToken cancellationToken = default)
        {
            pack ??= new ContextPack();
            var account = _accounts.Load();
            var session = _sessions.Load();
            var knowledge = account?.HostedKnowledgeBase;

            if (account != null && session != null && !string.IsNullOrWhiteSpace(session.AccessToken)
                && (knowledge == null || (!knowledge.CanUseInInterview && account.AccessTier.Equals("premium", StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    knowledge = await Deadline(
                        token => _hosted.GetKnowledgeBaseAsync(session.AccessToken, token), RefreshTimeout, cancellationToken);
                    account.HostedKnowledgeBase = knowledge;
                    _accounts.Save(account);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { /* Local fallback remains available. */ }
            }

            if (knowledge == null || !knowledge.CanUseInInterview || string.IsNullOrWhiteSpace(session?.AccessToken))
                return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);

            try
            {
                var result = await Deadline(
                    token => _hosted.SearchKnowledgeBaseAsync(session.AccessToken, query, preferredDocumentIds, maxSnippets, token),
                    SearchTimeout, cancellationToken);
                if (result.Snippets == null || result.Snippets.Count == 0)
                    return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);
                return result.Snippets.Select(item => new RetrievedContextSnippet
                {
                    DocumentId = item.DocumentId,
                    DocumentTitle = item.DocumentTitle,
                    SourceType = "hosted_kb",
                    Text = item.Text,
                    Score = (int)Math.Round(item.Score * 100d)
                }).ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return await _localFallback.RetrieveForPromptAsync(pack, query, preferredDocumentIds, maxSnippets, cancellationToken);
            }
        }

        private static async Task<T> Deadline<T>(Func<CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken outer)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            cts.CancelAfter(timeout);
            return await action(cts.Token).ConfigureAwait(false);
        }
    }
}
