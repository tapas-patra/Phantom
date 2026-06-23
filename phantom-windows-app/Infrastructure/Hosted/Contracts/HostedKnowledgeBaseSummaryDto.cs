using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class HostedKnowledgeBaseSummaryDto
    {
        public string KnowledgeBaseId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "not_created";
        public string EmbeddingModel { get; set; } = string.Empty;
        public int EmbeddingVersion { get; set; }
        public int DocumentCount { get; set; }
        public int ChunkCount { get; set; }
        public bool CanManage { get; set; }
        public bool CanUseInInterview { get; set; }
        public string BlockedReason { get; set; } = string.Empty;
        public DateTime? LastProcessedAtUtc { get; set; }
        public HostedKnowledgeBaseReindexJobDto LatestReindexJob { get; set; }
    }
}
