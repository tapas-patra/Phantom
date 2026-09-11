using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    /// <summary>
    /// Maps inbound relay frames to existing MainWindow behavior and exposes helpers
    /// to publish the outbound desktop frames defined in the companion WebSocket contract.
    /// All callbacks are invoked on a background thread; the owner must marshal to the UI
    /// dispatcher as needed.
    /// </summary>
    public sealed class CompanionCommandHost : IDisposable
    {
        // Contract §5.2: all body field names are camelCase on the wire. The DTOs use
        // PascalCase properties, so every outbound serialization must apply CamelCase
        // naming or the phone (kotlinx camelCase decoder with ignoreUnknownKeys) reads
        // nulls/defaults for every field (C1).
        private static readonly JsonSerializerOptions WireJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = false
        };

        private readonly CompanionRelayClient _client;
        private readonly Func<CompanionDesktopStatus> _statusProvider;
        private readonly Func<CompanionSessionSnapshotDto> _snapshotProvider;

        public CompanionCommandHost(
            CompanionRelayClient client,
            Func<CompanionDesktopStatus> statusProvider,
            Func<CompanionSessionSnapshotDto> snapshotProvider)
        {
            _client = client;
            _statusProvider = statusProvider;
            _snapshotProvider = snapshotProvider;
            _client.FrameReceived += OnFrame;
        }

        // Inbound callbacks (set by MainWindow).
        public Action<string, string?, string?>? OnCaptureAsk { get; set; }
        public Action<string, string?, bool>? OnCaptureFull { get; set; }
        public Action<string, string>? OnChatSend { get; set; }
        public Action<string?>? OnChatCancel { get; set; }
        public Action? OnChatNewTopic { get; set; }
        public Action<string?>? OnDisplaySelect { get; set; }
        public Action? OnVoiceStart { get; set; }
        public Action? OnVoiceStop { get; set; }

        public CompanionRelayState RelayState { get; private set; } = CompanionRelayState.Disconnected;
        public event Action<CompanionRelayState>? RelayStateChanged;
        public event Action<string, string>? RelayError;

        public void Start()
        {
            _client.StateChanged += state =>
            {
                RelayState = state;
                RelayStateChanged?.Invoke(state);
            };
            _client.Start();
        }

        public async Task StopAsync()
        {
            _client.FrameReceived -= OnFrame;
            await _client.DisposeAsync();
        }

        public void Dispose() => StopAsync().GetAwaiter().GetResult();

        private void OnFrame(CompanionRelayFrame frame)
        {
            if (frame.Type == "relay.ping" || frame.Type == "relay.peer_joined" || frame.Type == "relay.peer_left")
            {
                return;
            }
            if (frame.Type == "relay.error")
            {
                var code = frame.Code ?? frame.ReadString("code") ?? string.Empty;
                var message = frame.Message ?? frame.ReadString("message") ?? string.Empty;
                Log.WriteLine($"Companion relay error: {code} {message}");
                RelayError?.Invoke(code, message);
                return;
            }

            var requestId = frame.ReadString("requestId") ?? frame.Id ?? Guid.NewGuid().ToString("N");

            switch (frame.Type)
            {
                case "session.hello":
                    _ = SendDesktopHelloAsync();
                    _ = PublishSnapshotAsync();
                    break;
                case "capture.full":
                    var fullDisplayId = frame.ReadString("displayId");
                    var attachOnly = frame.ReadBool("attachOnly") ?? false;
                    OnCaptureFull?.Invoke(requestId, fullDisplayId, attachOnly);
                    break;
                case "capture.ask":
                    var askDisplayId = frame.ReadString("displayId");
                    var prompt = frame.ReadString("prompt");
                    OnCaptureAsk?.Invoke(requestId, askDisplayId, prompt);
                    break;
                case "chat.send":
                    var text = frame.ReadString("text") ?? string.Empty;
                    OnChatSend?.Invoke(requestId, text);
                    break;
                case "chat.cancel":
                    OnChatCancel?.Invoke(frame.ReadString("requestId"));
                    break;
                case "chat.new_topic":
                    OnChatNewTopic?.Invoke();
                    break;
                case "display.select":
                    var displayId = frame.ReadString("displayId");
                    OnDisplaySelect?.Invoke(displayId);
                    break;
                case "voice.start":
                    OnVoiceStart?.Invoke();
                    break;
                case "voice.stop":
                    OnVoiceStop?.Invoke();
                    break;
            }
        }

        public async Task SendDesktopHelloAsync()
        {
            var status = _statusProvider();
            var envelope = new
            {
                v = 1,
                id = Guid.NewGuid().ToString("N"),
                type = "desktop.hello",
                ts = DateTime.UtcNow,
                pairingId = _client.PairingId,
                role = "desktop",
                body = new
                {
                    status = status.Status,
                    model = status.Model,
                    provider = status.Provider,
                    vision = status.Vision,
                    displays = status.Displays ?? new List<CompanionDisplayDto>(),
                    lockExpiresAtUtc = status.LockExpiresAtUtc
                }
            };
            await _client.SendRawAsync(JsonSerializer.Serialize(envelope, WireJsonOptions));
        }

        public async Task SendDesktopStatusAsync(string statusText)
        {
            var status = _statusProvider();
            var envelope = new
            {
                v = 1,
                id = Guid.NewGuid().ToString("N"),
                type = "desktop.status",
                ts = DateTime.UtcNow,
                pairingId = _client.PairingId,
                role = "desktop",
                body = new
                {
                    status = statusText,
                    model = status.Model,
                    provider = status.Provider,
                    vision = status.Vision,
                    lockExpiresAtUtc = status.LockExpiresAtUtc
                }
            };
            await _client.SendRawAsync(JsonSerializer.Serialize(envelope, WireJsonOptions));
        }

        public async Task SendCaptureStartedAsync(string requestId, string? displayId)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "capture.started",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId, displayId = displayId ?? string.Empty }
            }));
        }

        public async Task SendCaptureCompletedAsync(string requestId, int width, int height, string? thumbnailJpegBase64)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "capture.completed",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId, width, height, thumbnailJpegBase64 }
            }));
        }

        public async Task SendCaptureFailedAsync(string requestId, string code, string message)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "capture.failed",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId, code, message }
            }));
        }

        public async Task SendChatStartedAsync(string requestId, string turnId)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "chat.started",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId, turnId }
            }));
        }

        public async Task SendChatDeltaAsync(string requestId, string text)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "chat.delta",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId, text }
            }));
        }

        public async Task SendChatCompletedAsync(string requestId)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "chat.completed",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId }
            }));
        }

        public async Task SendChatFailedAsync(string requestId, string code, string message)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "chat.failed",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId, code, message }
            }));
        }

        public async Task SendChatCancelledAsync(string requestId)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "chat.cancelled",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { requestId }
            }));
        }

        public async Task SendVoiceTranscriptAsync(string text)
        {
            await _client.SendRawAsync(JsonSerializer.Serialize(new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "voice.transcript",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new { text }
            }, WireJsonOptions));
        }

        public async Task PublishSnapshotAsync()
        {
            var snapshot = _snapshotProvider();
            if (snapshot == null) return;
            // Contract §5.2: session.snapshot body is the same shape as /sessions/current
            // MINUS pairingId (pairingId lives at the envelope top level only). Build an
            // explicit camelCase body so the DTO's PairingId is never echoed into the body (C1).
            var envelope = new
            {
                v = 1, id = Guid.NewGuid().ToString("N"), type = "session.snapshot",
                ts = DateTime.UtcNow, pairingId = _client.PairingId, role = "desktop",
                body = new
                {
                    desktopStatus = snapshot.DesktopStatus,
                    provider = snapshot.Provider,
                    model = snapshot.Model,
                    vision = snapshot.Vision,
                    displays = snapshot.Displays ?? new List<CompanionDisplayDto>(),
                    selectedDisplayId = snapshot.SelectedDisplayId,
                    turns = snapshot.Turns ?? new List<CompanionTurnDto>()
                }
            };
            await _client.SendRawAsync(JsonSerializer.Serialize(envelope, WireJsonOptions));
        }
    }

    public sealed class CompanionDesktopStatus
    {
        public string Status { get; set; } = "ready";
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool Vision { get; set; }
        public List<CompanionDisplayDto> Displays { get; set; } = new();
        public DateTime? LockExpiresAtUtc { get; set; }
    }
}
