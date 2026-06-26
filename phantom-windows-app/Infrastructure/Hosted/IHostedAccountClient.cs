using SecureOverlay.Infrastructure.Hosted.Contracts;
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
        HostedKnowledgeBaseSearchResultDto SearchKnowledgeBase(string accessToken, string query, int maxSnippets = 3);
        Task<HostedKnowledgeBaseSummaryDto> GetKnowledgeBaseAsync(string accessToken, CancellationToken cancellationToken = default);
        Task<HostedKnowledgeBaseSearchResultDto> SearchKnowledgeBaseAsync(
            string accessToken,
            string query,
            int maxSnippets = 3,
            CancellationToken cancellationToken = default);
        DesktopContextPackDto[] GetContextPacks(string accessToken);
        DesktopContextPackDto SaveContextPack(string accessToken, DesktopContextPackUpsertRequestDto request);
        void DeleteContextPack(string accessToken, string packId);
    }
}
