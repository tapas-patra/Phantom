using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    /// <summary>
    /// WebSocket relay client for Companion Mode. Connects to the hosted relay with a
    /// short-lived ticket, sends/receives JSON envelopes, and reconnects with a capped
    /// backoff. A fresh ticket is fetched from <see cref="_ticketProvider"/> on every
    /// connect attempt (spec §9) so reconnects after a drop do not fail on a consumed
    /// ticket. All inbound frames are surfaced via <see cref="FrameReceived"/>; the
    /// caller (CompanionCommandHost) decides how to react.
    /// </summary>
    public sealed class CompanionRelayClient : IAsyncDisposable
    {
        private const string SubProtocol = "phantom.companion.v1";
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
        private const int MaxAssemblyBytes = 128 * 1024;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private static readonly TimeSpan[] Backoff =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(15)
        };

        private readonly Func<CancellationToken, Task<CompanionRelayTicketDto>> _ticketProvider;
        private ClientWebSocket? _socket;
        private CancellationTokenSource _cts = new();
        private Task? _loopTask;
        private int _backoffIndex;

        public string PairingId { get; }
        public string Role { get; }
        public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;

        public event Action<CompanionRelayFrame>? FrameReceived;
        public event Action<CompanionRelayState>? StateChanged;

        public CompanionRelayClient(Func<CancellationToken, Task<CompanionRelayTicketDto>> ticketProvider, string pairingId, string role)
        {
            _ticketProvider = ticketProvider;
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
                    // Fetch a fresh ticket on every connect (spec §9). A ticket is
                    // single-use and consumed by the backend on upgrade, so reusing a
                    // stored ticket would permanently break reconnects.
                    CompanionRelayTicketDto ticket;
                    try
                    {
                        ticket = await _ticketProvider(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"CompanionRelayClient ticket fetch failed: {ex.Message}");
                        throw;
                    }

                    using var socket = new ClientWebSocket();
                    socket.Options.AddSubProtocol(SubProtocol);
                    var uriBuilder = new UriBuilder(ticket.RelayUrl)
                    {
                        Query = $"ticket={Uri.EscapeDataString(ticket.Ticket)}"
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
                    if (IsTerminalPairingFailure(ex.Message))
                    {
                        FrameReceived?.Invoke(new CompanionRelayFrame
                        {
                            V = 1,
                            Type = "relay.error",
                            Code = "pairing_revoked",
                            Message = ex.Message
                        });
                        break;
                    }
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
            var buffer = new byte[MaxAssemblyBytes];
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Outbound ping every 20s (spec §5: client may ping; server drops after 45s silence).
            var ping = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        await Task.Delay(PingInterval, cts.Token);
                        if (socket.State == WebSocketState.Open)
                        {
                            var pingPayload = JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                v = 1, type = "relay.ping", ts = DateTime.UtcNow
                            });
                            await socket.SendAsync(pingPayload, WebSocketMessageType.Text, endOfMessage: true, cts.Token);
                        }
                    }
                }
                catch { }
            }, cts.Token);

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    // 45s receive timeout — drop silent sockets.
                    cts.CancelAfter(ReceiveTimeout);
                    WebSocketReceiveResult result;
                    using var ms = new System.IO.MemoryStream();
                    var tooLarge = false;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                        if (ms.Length > MaxAssemblyBytes)
                        {
                            tooLarge = true;
                            while (!result.EndOfMessage)
                            {
                                result = await socket.ReceiveAsync(buffer, cts.Token);
                            }
                            break;
                        }
                    }
                    while (!result.EndOfMessage);

                    if (tooLarge)
                    {
                        Log.WriteLine("CompanionRelayClient: inbound frame exceeded size limit; ignored.");
                        continue;
                    }

                    if (ms.Length == 0) continue;

                    CompanionRelayFrame? frame;
                    try
                    {
                        frame = JsonSerializer.Deserialize<CompanionRelayFrame>(ms.ToArray(), JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"CompanionRelayClient failed to parse frame: {ex.Message}");
                        continue;
                    }

                    if (frame != null)
                    {
                        if (string.Equals(frame.Type, "relay.ping", StringComparison.Ordinal))
                        {
                            var pongPayload = JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                v = 1, type = "relay.pong", ts = DateTime.UtcNow,
                                pairingId = PairingId, role = Role
                            });
                            try
                            {
                                await socket.SendAsync(pongPayload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
                            }
                            catch { }
                            continue;
                        }
                        FrameReceived?.Invoke(frame);
                    }
                }
            }
            finally
            {
                cts.Cancel();
                try { await ping; } catch { }
            }
        }

        private void EmitState(CompanionRelayState state) => StateChanged?.Invoke(state);

        private static bool IsTerminalPairingFailure(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            return message.Contains("revoked", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Pairing not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("no longer available", StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum CompanionRelayState { Connecting, Connected, Disconnected }

    /// <summary>Outcome of a companion-driven chat turn, used to emit the correct
    /// chat.* frame from the SendMessage finally block (C5).</summary>
    public enum CompanionTurnOutcome { Pending, Succeeded, Cancelled, Failed }

    public sealed class CompanionRelayFrame
    {
        public int V { get; set; } = 1;
        public string? Id { get; set; }
        public string? Type { get; set; }
        public DateTime? Ts { get; set; }
        public string? PairingId { get; set; }
        public string? Role { get; set; }
        // Top-level fields used by relay.error (code/message are sent at the envelope top
        // level, not under body). Bound via case-insensitive deserialization.
        public string? Code { get; set; }
        public string? Message { get; set; }
        public JsonElement? Body { get; set; }

        // Reads a string field from the envelope BODY (where phone→desktop commands live).
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

        public int? ReadInt(string key)
        {
            if (!Body.HasValue || Body.Value.ValueKind != JsonValueKind.Object) return null;
            if (!Body.Value.TryGetProperty(key, out var p)) return null;
            return p.ValueKind switch
            {
                JsonValueKind.Number when p.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(p.GetString(), out var parsed) => parsed,
                _ => null
            };
        }
    }
}
