using SecureOverlay.Infrastructure.Hosted.Contracts;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedAccountClient
    {
        StartupAccountCheckResultDto GetStartupAccountCheck(AuthSessionDto session);
        StartupAccountCheckResultDto GetStartupAccountCheck(AuthCallbackResultDto callbackResult);
        ManagedAiCatalogDto GetManagedCatalog(string accessToken);
        HostedKnowledgeBaseSummaryDto GetKnowledgeBase(string accessToken);
        HostedKnowledgeBaseSearchResultDto SearchKnowledgeBase(
            string accessToken,
            string query,
            IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3);
        HostedKnowledgeBaseProfileCardDto GetKnowledgeBaseProfile(string accessToken);
        HostedKnowledgeBaseProjectCardDto[] GetKnowledgeBaseProjects(string accessToken);
        HostedKnowledgeBaseProjectCardDto GetKnowledgeBaseProject(string accessToken, string projectCardId);
        Task<HostedKnowledgeBaseSummaryDto> GetKnowledgeBaseAsync(string accessToken, CancellationToken cancellationToken = default);
        Task<HostedKnowledgeBaseSearchResultDto> SearchKnowledgeBaseAsync(
            string accessToken,
            string query,
            IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3,
            CancellationToken cancellationToken = default);
        Task<HostedKnowledgeBaseProfileCardDto> GetKnowledgeBaseProfileAsync(string accessToken, CancellationToken cancellationToken = default);
        Task<HostedKnowledgeBaseProjectCardDto[]> GetKnowledgeBaseProjectsAsync(string accessToken, CancellationToken cancellationToken = default);
        Task<HostedKnowledgeBaseProjectCardDto> GetKnowledgeBaseProjectAsync(string accessToken, string projectCardId, CancellationToken cancellationToken = default);
        DesktopContextPackDto[] GetContextPacks(string accessToken);
        DesktopContextPackDto SaveContextPack(string accessToken, DesktopContextPackUpsertRequestDto request);
        void DeleteContextPack(string accessToken, string packId);
    }
}
