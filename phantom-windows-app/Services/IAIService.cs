using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecureOverlay.Services
{
    public interface IAIService
    {
        // Original method
        Task<string> SendMessageAsync(List<ConversationMessage> messages, string? imageBase64 = null);
        
        // Streaming method with cancellation support
        Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages, 
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            string? imageBase64 = null
        );
        
        string GetProviderName();
        bool IsConfigured();
    }
}
