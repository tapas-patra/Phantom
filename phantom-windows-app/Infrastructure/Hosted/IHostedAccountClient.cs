using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedAccountClient
    {
        StartupAccountCheckResultDto GetStartupAccountCheck(AuthSessionDto session);
        StartupAccountCheckResultDto GetStartupAccountCheck(AuthCallbackResultDto callbackResult);
        ManagedAiCatalogDto GetManagedCatalog(string accessToken);
        HostedKnowledgeBaseSummaryDto GetKnowledgeBase(string accessToken);
        HostedKnowledgeBaseSearchResultDto SearchKnowledgeBase(string accessToken, string query, int maxSnippets = 3);
    }
}
