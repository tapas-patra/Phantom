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
                session,
                session.AccessToken);
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

        public DesktopContextPackDto[] GetContextPacks(string accessToken)
        {
            return GetJson<DesktopContextPackDto[]>("/api/desktop/context-packs", accessToken);
        }

        public DesktopContextPackDto SaveContextPack(string accessToken, DesktopContextPackUpsertRequestDto request)
        {
            return PostJson<DesktopContextPackUpsertRequestDto, DesktopContextPackDto>(
                "/api/desktop/context-packs",
                request,
                accessToken);
        }

        public void DeleteContextPack(string accessToken, string packId)
        {
            PostJson<DesktopContextPackDeleteRequestDto, DeleteResult>(
                "/api/desktop/context-packs/delete",
                new DesktopContextPackDeleteRequestDto { PackId = packId ?? string.Empty },
                accessToken);
        }

        private sealed class DeleteResult
        {
            public bool Deleted { get; set; }
        }
    }
}
