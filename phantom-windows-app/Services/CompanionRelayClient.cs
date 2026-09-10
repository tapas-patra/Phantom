using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SecureOverlay.Services
{
    /// <summary>
    /// WebSocket relay client for Companion Mode. Connects to the hosted relay with a
    /// short-lived ticket, sends/receases JSON envelopes, and reconnects with a capped
    /// backoff. All inbound frames are surfaced via <see cref="FrameReceived"/>; the
    /// caller (CompanionCommandHost) decides how to react.
    /// </summary>
    public sealed class CompanionRelayClient : IAsyncDisposable
    {
        private const string SubProtocol = "phantom.companion.v1";
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan[] Backoff =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(15)
        };

        private readonly string _relayUrl;
        private readonly string _ticket;
        private ClientWebSocket? _socket;
        private CancellationTokenSource _cts = new();
        private Task? _loopTask;
        private int _backoffIndex;

        public string PairingId { get; }
        public string Role { get; }
        public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;

        public event Action<CompanionRelayFrame>? FrameReceived;
        public event Action<CompanionRelayState>? StateChanged;

        public CompanionRelayClient(string relayUrl, string ticket, string pairingId, string role)
        {
            _relayUrl = relayUrl;
            _ticket = ticket;
            PairingId = pairingId;
            Role = role;
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stopping", CancellationToken.None); } catch { }
            }
            if (_loopTask != null)
            {
                try { await _loopTask; } catch { }
            }
            _loopTask = null;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _cts.Dispose();
        }

        public async Task SendAsync(CompanionRelayFrame frame, CancellationToken cancellationToken = default)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            var payload = JsonSerializer.SerializeToUtf8Bytes(frame);
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }

        public async Task SendRawAsync(string json, CancellationToken cancellationToken = default)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            var payload = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EmitState(CompanionRelayState.Connecting);
                try
                {
                    using var socket = new ClientWebSocket();
                    socket.Options.AddSubProtocol(SubProtocol);
                    var uriBuilder = new UriBuilder(_relayUrl)
                    {
                        Query = $"ticket={Uri.EscapeDataString(_ticket)}"
                    };
                    await socket.ConnectAsync(uriBuilder.Uri, cancellationToken);
                    _socket = socket;
                    _backoffIndex = 0;
                    EmitState(CompanionRelayState.Connected);
                    await ReceiveLoopAsync(socket, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"CompanionRelayClient connection error: {ex.Message}");
                }
                finally
                {
                    _socket = null;
                    EmitState(CompanionRelayState.Disconnected);
                }

                if (cancellationToken.IsCancellationRequested) break;
                var delay = Backoff[Math.Min(_backoffIndex, Backoff.Length - 1)];
                _backoffIndex = Math.Min(_backoffIndex + 1, Backoff.Length - 1);
                try { await Task.Delay(delay, cancellationToken); } catch { break; }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ReceiveTimeout);

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                cts.CancelAfter(ReceiveTimeout);
                WebSocketReceiveResult result;
                using var ms = new System.IO.MemoryStream();
                do
                {
                    result = await socket.ReceiveAsync(buffer, cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (ms.Length == 0) continue;

                CompanionRelayFrame? frame;
                try
                {
                    frame = JsonSerializer.Deserialize<CompanionRelayFrame>(ms.ToArray());
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"CompanionRelayClient failed to parse frame: {ex.Message}");
                    continue;
                }

                if (frame != null)
                {
                    FrameReceived?.Invoke(frame);
                }
            }
        }

        private void EmitState(CompanionRelayState state) => StateChanged?.Invoke(state);
    }

    public enum CompanionRelayState { Connecting, Connected, Disconnected }

    public sealed class CompanionRelayFrame
    {
        public int V { get; set; } = 1;
        public string? Id { get; set; }
        public string? Type { get; set; }
        public DateTime? Ts { get; set; }
        public string? PairingId { get; set; }
        public string? Role { get; set; }
        public JsonElement? Body { get; set; }

        public string? ReadString(string key)
            => Body.HasValue && Body.Value.ValueKind == JsonValueKind.Object
                && Body.Value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;

        public bool? ReadBool(string key)
            => Body.HasValue && Body.Value.ValueKind == JsonValueKind.Object
                && Body.Value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True
                ? true
                : (Body.HasValue && Body.Value.ValueKind == JsonValueKind.Object
                    && Body.Value.TryGetProperty(key, out var q) && q.ValueKind == JsonValueKind.False ? false : null);
    }
}
