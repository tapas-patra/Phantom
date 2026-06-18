using SecureOverlay.Infrastructure.Hosted.Contracts;
using System;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedAccountClient : HttpHostedClientBase, IHostedAccountClient
    {
        public HttpHostedAccountClient(HostedRuntimeOptions options)
            : base(options)
        {
        }

        public StartupAccountCheckResultDto GetStartupAccountCheck(AuthSessionDto session)
        {
            return PostJson<AuthSessionDto, StartupAccountCheckResultDto>(
                "/api/desktop/account/startup-check/session",
                session);
        }

        public StartupAccountCheckResultDto GetStartupAccountCheck(AuthCallbackResultDto callbackResult)
        {
            return PostJson<AuthCallbackResultDto, StartupAccountCheckResultDto>(
                "/api/desktop/account/startup-check/callback",
                callbackResult);
        }

        public ManagedAiCatalogDto GetManagedCatalog(string accessToken)
        {
            return GetJson<ManagedAiCatalogDto>("/api/desktop/ai/catalog", accessToken);
        }

        public HostedKnowledgeBaseSummaryDto GetKnowledgeBase(string accessToken)
        {
            return GetJson<HostedKnowledgeBaseSummaryDto>("/api/desktop/kb", accessToken);
        }

        public HostedKnowledgeBaseSearchResultDto SearchKnowledgeBase(string accessToken, string query, int maxSnippets = 3)
        {
            var encodedQuery = Uri.EscapeDataString(query ?? string.Empty);
            return GetJson<HostedKnowledgeBaseSearchResultDto>(
                $"/api/desktop/kb/search?query={encodedQuery}&maxSnippets={Math.Max(1, maxSnippets)}",
                accessToken);
        }
    }
}
