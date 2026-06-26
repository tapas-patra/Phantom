using SecureOverlay.Infrastructure.Hosted.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        public Task<HostedKnowledgeBaseSummaryDto> GetKnowledgeBaseAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            return GetJsonAsync<HostedKnowledgeBaseSummaryDto>("/api/desktop/kb", accessToken, cancellationToken);
        }

        public HostedKnowledgeBaseSearchResultDto SearchKnowledgeBase(
            string accessToken,
            string query,
            IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3)
        {
            var encodedQuery = Uri.EscapeDataString(query ?? string.Empty);
            var preferredDocs = BuildPreferredDocumentQuery(preferredDocumentIds);
            return GetJson<HostedKnowledgeBaseSearchResultDto>(
                $"/api/desktop/kb/search?query={encodedQuery}&maxSnippets={Math.Max(1, maxSnippets)}{preferredDocs}",
                accessToken);
        }

        public Task<HostedKnowledgeBaseSearchResultDto> SearchKnowledgeBaseAsync(
            string accessToken,
            string query,
            IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3,
            CancellationToken cancellationToken = default)
        {
            var encodedQuery = Uri.EscapeDataString(query ?? string.Empty);
            var preferredDocs = BuildPreferredDocumentQuery(preferredDocumentIds);
            return GetJsonAsync<HostedKnowledgeBaseSearchResultDto>(
                $"/api/desktop/kb/search?query={encodedQuery}&maxSnippets={Math.Max(1, maxSnippets)}{preferredDocs}",
                accessToken,
                cancellationToken);
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

        private static string BuildPreferredDocumentQuery(IReadOnlyList<string>? preferredDocumentIds)
        {
            if (preferredDocumentIds == null || preferredDocumentIds.Count == 0)
            {
                return string.Empty;
            }

            var joined = string.Join(
                ",",
                preferredDocumentIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(Uri.EscapeDataString));
            return string.IsNullOrWhiteSpace(joined)
                ? string.Empty
                : $"&preferredDocumentIds={joined}";
        }
    }
}
