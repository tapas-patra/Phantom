using System.Collections.Generic;
using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Application.Context
{
    public interface IKnowledgeRetrievalService
    {
        IReadOnlyList<RetrievedContextSnippet> RetrieveForPrompt(ContextPack pack, string query, int maxSnippets = 3);
    }
}
