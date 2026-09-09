using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecureOverlay.Services
{
    public interface IAIService
    {
        Task<string> SendMessageAsync(List<ConversationMessage> messages, IReadOnlyList<string>? imagesBase64 = null);

        Task<string> SendMessageStreamAsync(
            List<ConversationMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            IReadOnlyList<string>? imagesBase64 = null);

        string GetProviderName();
        bool IsConfigured();
    }
}
