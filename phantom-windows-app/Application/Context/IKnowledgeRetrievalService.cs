using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Context
{
    public interface IKnowledgeRetrievalService
    {
        Task<IReadOnlyList<RetrievedContextSnippet>> RetrieveForPromptAsync(
            ContextPack pack,
            string query,
            IReadOnlyList<string>? preferredDocumentIds = null,
            int maxSnippets = 3,
            CancellationToken cancellationToken = default);
    }
}
