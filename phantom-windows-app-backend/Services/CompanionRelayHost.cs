using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

/// <summary>
/// In-memory relay host for companion pairings. Holds one desktop socket and one phone
/// socket per pairing, forwards frames between them, caches the last desktop snapshot,
/// and tracks online presence. State is lost on process restart (acceptable for v1).
/// </summary>
public sealed class CompanionRelayHost
{
    private const string SubProtocol = "phantom.companion.v1";
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
    private const int MaxFrameBytes = 64 * 1024;
    private const int MaxCaptureCompletedFrameBytes = 128 * 1024; // spec §5: capture.completed thumbnail up to 80 KB decoded
    // Reassembly buffer must accommodate the largest allowed frame (capture.completed),
    // so it is sized to the capture.completed limit, not the 64 KB default (M2).
    private const int MaxAssemblyBytes = MaxCaptureCompletedFrameBytes;
    // spec §5: the capture.completed thumbnail itself may be up to 80 KB DECODED. The
    // frame-size check above only bounds the whole frame; a peer could still ship an
    // 85 KB-decoded thumbnail (~113 KB base64) that fits the 128 KB frame cap. Enforce
    // the decoded limit explicitly (M1).
    private const int MaxThumbnailDecodedBytes = 80 * 1024;
    private const int CaptureRatePerMinute = 10;
    private const int ChatSendRatePerMinute = 30;
    private const int MaxSnapshotTurns = 20;
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CompanionRelayTicketService _tickets;
    private readonly CompanionPairingRepository _pairings;
    private readonly LockRepository _locks;
    private readonly CompanionAuditRepository _audit;
    private readonly ILogger<CompanionRelayHost> _logger;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    public CompanionRelayHost(
        CompanionRelayTicketService tickets,
        CompanionPairingRepository pairings,
        LockRepository locks,
        CompanionAuditRepository audit,
        ILoggerFactory loggerFactory)
    {
        _tickets = tickets;
        _pairings = pairings;
        _locks = locks;
        _audit = audit;
        _logger = loggerFactory.CreateLogger<CompanionRelayHost>();
    }

    public bool IsDesktopOnline(string pairingId) =>
        _rooms.TryGetValue(pairingId, out var room) && room.DesktopSocket.IsOpen();

    public bool IsPhoneOnline(string pairingId) =>
        _rooms.TryGetValue(pairingId, out var room) && room.PhoneSocket.IsOpen();

    public CompanionSessionSnapshotDto? GetSnapshot(string pairingId)
    {
        return _rooms.TryGetValue(pairingId, out var room) ? room.Snapshot : null;
    }

    /// <summary>Drops both sockets in a room with a relay.error frame. Used by pairing revoke.</summary>
    public void DropRoom(string pairingId, string code)
    {
        if (!_rooms.TryGetValue(pairingId, out var room)) return;
        room.SendRelayErrorToBoth(code, "Pairing closed.");
        room.DesktopSocket.Abort();
        room.PhoneSocket.Abort();
    }

    /// <summary>
    /// Gracefully closes every room on process shutdown (spec §6.4). Sends a relay.error
    /// to both peers before aborting so clients can surface a clean disconnect.
    /// </summary>
    public void Shutdown()
    {
        foreach (var pair in _rooms)
        {
            pair.Value.SendRelayErrorToBoth("server_shutdown", "Relay is shutting down.");
            pair.Value.DesktopSocket.Abort();
            pair.Value.PhoneSocket.Abort();
        }
        _rooms.Clear();
    }

    public async Task AcceptAsync(HttpContext httpContext)
    {
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new { error = "WebSocket upgrade required." });
            return;
        }

        var ticket = httpContext.Request.Query["ticket"].FirstOrDefault();
        CompanionRelayTicketRecord ticketRecord;
        try
        {
            ticketRecord = _tickets.Consume(ticket);
        }
        catch (BackendValidationException ex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsJsonAsync(new { error = ex.Message });
            return;
        }

        var pairing = _pairings.FindById(ticketRecord.PairingId);
        if (pairing == null || pairing.RevokedAtUtc.HasValue
            || !string.Equals(pairing.UserId, ticketRecord.UserId, StringComparison.Ordinal))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Pairing is no longer available." });
            return;
        }

        var expectedDeviceId = ticketRecord.Role switch
        {
            "desktop" => pairing.DesktopDeviceId,
            "phone" => pairing.CompanionDeviceId,
            _ => string.Empty
        };
        if (!string.Equals(expectedDeviceId, ticketRecord.DeviceId, StringComparison.Ordinal))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Ticket does not match this device." });
            return;
        }

        var acceptSubProtocol = httpContext.WebSockets.WebSocketRequestedProtocols.Contains(SubProtocol)
            ? SubProtocol
            : null;
        using var socket = await httpContext.WebSockets.AcceptWebSocketAsync(acceptSubProtocol);

        var room = _rooms.GetOrAdd(pairing.PairingId, _ => new Room(pairing.PairingId, pairing.UserId, pairing.DesktopDeviceId));
        var role = ticketRecord.Role;

        var replaced = room.SetSocket(role, socket);
        if (replaced != null)
        {
            _ = SendRelayErrorAsync(replaced, "replaced", "Another connection replaced this one.");
        }

        _audit.Record(ticketRecord.UserId, pairing.PairingId, "relay_connected", role);
        room.NotifyPeerJoined(role);

        try
        {
            await ReceiveLoopAsync(room, role, socket, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            _logger.LogDebug("companion relay socket closed pairing_id={PairingId} role={Role}: {Message}", pairing.PairingId, role, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "companion relay loop failed pairing_id={PairingId} role={Role}", pairing.PairingId, role);
        }
        finally
        {
            room.ClearSocket(role, socket);
            room.NotifyPeerLeft(role);
            _audit.Record(ticketRecord.UserId, pairing.PairingId, "relay_disconnected", role);
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None); } catch { }
            if (!room.DesktopSocket.IsOpen() && !room.PhoneSocket.IsOpen())
            {
                _rooms.TryRemove(pairing.PairingId, out _);
            }
        }
    }

    private async Task ReceiveLoopAsync(Room room, string role, WebSocket socket, CancellationToken externalToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var buffer = new byte[MaxFrameBytes];

        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested && IsOpen(socket))
                {
                    await Task.Delay(HeartbeatInterval, cts.Token);
                    if (IsOpen(socket))
                    {
                        await socket.SendAsync(RelayPingFrame, WebSocketMessageType.Text, endOfMessage: true, cts.Token);
                    }
                }
            }
            catch { }
        }, cts.Token);

        try
        {
            while (IsOpen(socket) && !cts.IsCancellationRequested)
            {
                cts.CancelAfter(ReceiveTimeout);
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                byte[] frame;
                if (result.EndOfMessage)
                {
                    frame = buffer.AsMemory(0, result.Count).ToArray();
                }
                else
                {
                    // Multi-fragment reassembly with a hard cap to prevent unbounded
                    // memory growth from a malicious peer (spec §5 max frame, C9).
                    using var ms = new System.IO.MemoryStream();
                    ms.Write(buffer, 0, result.Count);
                    var tooLarge = ms.Length > MaxAssemblyBytes;
                    while (!result.EndOfMessage)
                    {
                        result = await socket.ReceiveAsync(buffer, cts.Token);
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
                    if (tooLarge)
                    {
                        await SendRelayErrorAsync(socket, "payload_too_large", "Frame exceeds size limit.");
                        continue;
                    }
                    frame = ms.ToArray();
                }

                // Per-type size enforcement after we know the envelope type (M12):
                // capture.completed may carry an up-to-80 KB thumbnail (base64-encoded).
                string? frameType = PeekFrameType(frame);
                var limit = frameType == "capture.completed" ? MaxCaptureCompletedFrameBytes : MaxFrameBytes;
                if (frame.Length > limit)
                {
                    await SendRelayErrorAsync(socket, "payload_too_large", "Frame exceeds size limit.");
                    continue;
                }

                await HandleFrameAsync(room, role, socket, frame, cts.Token);
            }
        }
        finally
        {
            cts.Cancel();
            try { await heartbeat; } catch { }
        }
    }

    private static string? PeekFrameType(byte[] frame)
    {
        try
        {
            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            {
                return typeEl.GetString();
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Returns true when a capture.completed frame's body.thumbnailJpegBase64 decodes to
    /// more than MaxThumbnailDecodedBytes (spec §5: thumbnail up to 80 KB decoded). Used to
    /// enforce the decoded-size limit that the whole-frame size check cannot (M1).
    /// </summary>
    private static bool ThumbnailExceedsLimit(byte[] frame)
    {
        try
        {
            using var doc = JsonDocument.Parse(frame);
            if (!doc.RootElement.TryGetProperty("body", out var bodyEl) || bodyEl.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!bodyEl.TryGetProperty("thumbnailJpegBase64", out var thumbEl)
                || thumbEl.ValueKind != JsonValueKind.String)
            {
                return false; // no thumbnail (attach-only capture) — nothing to check
            }
            var base64 = thumbEl.GetString();
            if (string.IsNullOrEmpty(base64)) return false;
            // base64 decodes to ~3/4 of its length; an exact byte count is cheapest and
            // avoids allocating the decoded bytes just to measure them.
            var decodedLen = (base64!.Length * 3) / 4;
            // Account for base64 padding so the estimate is conservative.
            if (base64.EndsWith("==", StringComparison.Ordinal)) decodedLen -= 2;
            else if (base64.EndsWith("=", StringComparison.Ordinal)) decodedLen -= 1;
            return decodedLen > MaxThumbnailDecodedBytes;
        }
        catch
        {
            // Malformed body — let the downstream snapshot/hello handling reject it; do not
            // block the frame here on a parse error.
            return false;
        }
    }

    private async Task HandleFrameAsync(Room room, string role, WebSocket socket, byte[] frame, CancellationToken cancellationToken)
    {
        RelayEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RelayEnvelope>(frame, JsonOptions);
        }
        catch
        {
            await SendRelayErrorAsync(socket, "malformed_frame", "Frame is not valid JSON.");
            return;
        }

        if (envelope == null || envelope.V != 1 || string.IsNullOrWhiteSpace(envelope.Type))
        {
            await SendRelayErrorAsync(socket, "malformed_frame", "Envelope missing required fields.");
            return;
        }

        // spec §5.1: the envelope top level MUST carry pairingId and role. Previously these
        // were only checked when non-empty, so a frame omitting them passed validation (M3).
        // relay.ping / relay.pong are keepalives and are exempt — the server's own ping is
        // {v,type,ts} with no pairingId/role, and clients send minimal pings too (spec §5).
        if (string.IsNullOrWhiteSpace(envelope.PairingId)
            && envelope.Type is not ("relay.ping" or "relay.pong"))
        {
            await SendRelayErrorAsync(socket, "malformed_frame", "Envelope missing pairingId.");
            return;
        }
        if (string.IsNullOrWhiteSpace(envelope.Role)
            && envelope.Type is not ("relay.ping" or "relay.pong"))
        {
            await SendRelayErrorAsync(socket, "malformed_frame", "Envelope missing role.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.PairingId)
            && !string.Equals(envelope.PairingId, room.PairingId, StringComparison.Ordinal))
        {
            await SendRelayErrorAsync(socket, "pairing_mismatch", "Envelope pairingId does not match room.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Role)
            && !string.Equals(envelope.Role, role, StringComparison.Ordinal))
        {
            await SendRelayErrorAsync(socket, "role_mismatch", "Envelope role does not match connection.");
            return;
        }

        // spec §5: capture.completed thumbnail may be up to 80 KB DECODED. The frame-size
        // check above only bounds the whole frame; enforce the decoded thumbnail limit here
        // so a peer cannot ship an oversized image that fits the 128 KB frame cap (M1).
        if (envelope.Type == "capture.completed" && ThumbnailExceedsLimit(frame))
        {
            await SendRelayErrorAsync(socket, "payload_too_large", "capture.completed thumbnail exceeds 80 KB decoded limit.");
            return;
        }

        if (role == "desktop")
        {
            if (envelope.Type == "session.snapshot")
            {
                room.UpdateSnapshot(frame);
                await room.SendToPhoneAsync(frame, cancellationToken);
                return;
            }
            if (envelope.Type is "desktop.hello" or "desktop.status")
            {
                room.UpdateHello(frame);
            }
            await room.SendToPhoneAsync(frame, cancellationToken);
            return;
        }

        // Phone → desktop.
        if (envelope.Type is "capture.full" or "capture.ask")
        {
            if (!room.TryConsumeCaptureSlot())
            {
                await SendRelayErrorAsync(socket, "rate_limited", "Capture rate limit exceeded.");
                return;
            }
            if (!await PairingDesktopHoldsLockAsync(room))
            {
                await SendRelayErrorAsync(socket, "lock_missing", "Desktop does not hold an active interview lock.");
                return;
            }
            _audit.Record(room.UserId ?? string.Empty, room.PairingId, "capture_requested", "phone");
        }
        else if (envelope.Type == "chat.send")
        {
            if (!room.TryConsumeChatSlot())
            {
                await SendRelayErrorAsync(socket, "rate_limited", "Chat rate limit exceeded.");
                return;
            }
        }

        await room.SendToDesktopAsync(frame, cancellationToken);
    }

    private async Task<bool> PairingDesktopHoldsLockAsync(Room room)
    {
        try
        {
            if (room.UserId == null || room.DesktopDeviceId == null) return false;
            var active = _locks.FindActiveByUser(room.UserId);
            return active != null && active.ExpiresAtUtc > DateTime.UtcNow
                && string.Equals(active.DeviceId, room.DesktopDeviceId, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static bool IsOpen(WebSocket socket) => socket.State == WebSocketState.Open;

    private static async Task SendRelayErrorAsync(WebSocket socket, string code, string message)
    {
        if (!IsOpen(socket)) return;
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                v = 1,
                type = "relay.error",
                code,
                message,
                ts = DateTime.UtcNow
            });
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
        catch { }
    }

    private static readonly byte[] RelayPingFrame = JsonSerializer.SerializeToUtf8Bytes(new
    {
        v = 1,
        type = "relay.ping",
        ts = DateTime.UtcNow
    });

    private sealed class RelayEnvelope
    {
        public int V { get; set; }
        public string? Id { get; set; }
        public string? Type { get; set; }
        public DateTime? Ts { get; set; }
        public string? PairingId { get; set; }
        public string? Role { get; set; }
    }

    private sealed class Room
    {
        private readonly object _gate = new();
        private WebSocket? _desktop;
        private WebSocket? _phone;
        private readonly SemaphoreSlim _desktopSendLock = new(1, 1);
        private readonly SemaphoreSlim _phoneSendLock = new(1, 1);
        private readonly List<DateTime> _captureSlots = new();
        private readonly List<DateTime> _chatSlots = new();

        public Room(string pairingId, string userId, string desktopDeviceId)
        {
            PairingId = pairingId;
            UserId = userId;
            DesktopDeviceId = desktopDeviceId;
        }

        public string PairingId { get; }
        public string? UserId { get; }
        public string? DesktopDeviceId { get; }
        public CompanionSessionSnapshotDto? Snapshot { get; private set; }
        public string? LastHello { get; private set; }

        public PeerSocket DesktopSocket => new(_desktop);
        public PeerSocket PhoneSocket => new(_phone);

        public WebSocket? SetSocket(string role, WebSocket socket)
        {
            lock (_gate)
            {
                if (role == "desktop") { var existing = _desktop; _desktop = socket; return existing; }
                var old = _phone; _phone = socket; return old;
            }
        }

        public void ClearSocket(string role, WebSocket socket)
        {
            lock (_gate)
            {
                if (role == "desktop" && ReferenceEquals(_desktop, socket)) _desktop = null;
                else if (role == "phone" && ReferenceEquals(_phone, socket)) _phone = null;
            }
        }

        public void UpdateSnapshot(byte[] frame)
        {
            try
            {
                // Desktops send the snapshot nested under `body` (spec §5.2). Unwrap it
                // before deserializing so /sessions/current returns real data (H3).
                CompanionSessionSnapshotDto? snapshot;
                using (var doc = JsonDocument.Parse(frame))
                {
                    if (doc.RootElement.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.Object)
                    {
                        snapshot = JsonSerializer.Deserialize<CompanionSessionSnapshotDto>(bodyEl.GetRawText(), JsonOptions);
                    }
                    else
                    {
                        snapshot = JsonSerializer.Deserialize<CompanionSessionSnapshotDto>(frame, JsonOptions);
                    }
                }
                if (snapshot == null) return;
                snapshot.PairingId = PairingId;
                // Keep the LAST 20 turns, not the first 20 (spec §4.6).
                if (snapshot.Turns.Count > MaxSnapshotTurns)
                {
                    snapshot.Turns = snapshot.Turns
                        .Skip(snapshot.Turns.Count - MaxSnapshotTurns)
                        .ToList();
                }
                lock (_gate) { Snapshot = snapshot; }
            }
            catch { }
        }

        public void UpdateHello(byte[] frame)
        {
            try
            {
                using var doc = JsonDocument.Parse(frame);
                if (doc.RootElement.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.Object)
                {
                    lock (_gate) { LastHello = bodyEl.GetRawText(); }
                }
            }
            catch { }
        }

        public bool TryConsumeCaptureSlot()
        {
            lock (_gate) { return TryConsumeSlot(_captureSlots, CaptureRatePerMinute); }
        }

        public bool TryConsumeChatSlot()
        {
            lock (_gate) { return TryConsumeSlot(_chatSlots, ChatSendRatePerMinute); }
        }

        private bool TryConsumeSlot(List<DateTime> slots, int limit)
        {
            var now = DateTime.UtcNow;
            var cutoff = now - RateWindow;
            slots.RemoveAll(slot => slot < cutoff);
            if (slots.Count >= limit) return false;
            slots.Add(now);
            return true;
        }

        public void NotifyPeerJoined(string role)
        {
            var peerRole = role == "desktop" ? "phone" : "desktop";
            _ = SendToRoleAsync(peerRole, BuildRelayEvent("relay.peer_joined", role), CancellationToken.None);
        }

        public void NotifyPeerLeft(string role)
        {
            var peerRole = role == "desktop" ? "phone" : "desktop";
            _ = SendToRoleAsync(peerRole, BuildRelayEvent("relay.peer_left", role), CancellationToken.None);
        }

        public async Task SendToDesktopAsync(byte[] frame, CancellationToken cancellationToken)
        {
            await SendToRoleAsync("desktop", frame, cancellationToken);
        }

        public async Task SendToPhoneAsync(byte[] frame, CancellationToken cancellationToken)
        {
            await SendToRoleAsync("phone", frame, cancellationToken);
        }

        public void SendRelayErrorToBoth(string code, string message)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                v = 1,
                type = "relay.error",
                code,
                message,
                ts = DateTime.UtcNow
            });
            _ = SendToRoleAsync("desktop", payload, CancellationToken.None);
            _ = SendToRoleAsync("phone", payload, CancellationToken.None);
        }

        // Per-socket send serialization. ASP.NET Core WebSocket sends are not documented
        // thread-safe; the heartbeat task, frame forwarding, and relay.error sends can
        // otherwise interleave on the same socket (H6).
        private async Task SendToRoleAsync(string role, byte[] frame, CancellationToken cancellationToken)
        {
            var gate = role == "desktop" ? _desktopSendLock : _phoneSendLock;
            WebSocket? socket;
            lock (_gate)
            {
                socket = role == "desktop" ? _desktop : _phone;
            }
            if (socket == null || socket.State != WebSocketState.Open) return;
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
                }
            }
            catch { }
            finally
            {
                gate.Release();
            }
        }

        private static byte[] BuildRelayEvent(string type, string role)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                v = 1,
                type,
                role,
                ts = DateTime.UtcNow
            });
        }
    }

    private readonly struct PeerSocket
    {
        private readonly WebSocket? _socket;
        public PeerSocket(WebSocket? socket) { _socket = socket; }
        public bool IsOpen() => _socket != null && _socket.State == WebSocketState.Open;
        public void Abort() { try { _socket?.Abort(); } catch { } }
    }
}
