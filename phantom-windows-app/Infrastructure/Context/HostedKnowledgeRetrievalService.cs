using System;
using System.Linq;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted;

namespace SecureOverlay.Infrastructure.Context
{
    public sealed class HostedKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
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

        public System.Collections.Generic.IReadOnlyList<RetrievedContextSnippet> RetrieveForPrompt(ContextPack pack, string query, int maxSnippets = 3)
        {
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
                    hostedKnowledgeBase = _hostedAccountClient.GetKnowledgeBase(session.AccessToken);
                    accountSnapshot.HostedKnowledgeBase = hostedKnowledgeBase;
                    _accounts.Save(accountSnapshot);
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
                return _localFallback.RetrieveForPrompt(pack, query, maxSnippets);
            }

            try
            {
                var result = _hostedAccountClient.SearchKnowledgeBase(session.AccessToken, query, maxSnippets);
                if (result.Snippets == null || result.Snippets.Count == 0)
                {
                    return _localFallback.RetrieveForPrompt(pack, query, maxSnippets);
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
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted KB retrieval failed, using local fallback: {ex.Message}");
                return _localFallback.RetrieveForPrompt(pack, query, maxSnippets);
            }
        }
    }
}
