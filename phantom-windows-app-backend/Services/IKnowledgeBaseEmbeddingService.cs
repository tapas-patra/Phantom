using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Services;

public interface IKnowledgeBaseEmbeddingService
{
    bool IsConfigured { get; }
    KnowledgeBaseEmbeddingProfile ActiveProfile { get; }
    HostedKnowledgeBaseEmbeddingConfigDto GetAdminConfiguration();
    HostedKnowledgeBaseEmbeddingConfigDto UpdateAdminConfiguration(HostedKnowledgeBaseEmbeddingConfigUpdateRequestDto request);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
    Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken);
    Task<float[]> GenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken);
}
