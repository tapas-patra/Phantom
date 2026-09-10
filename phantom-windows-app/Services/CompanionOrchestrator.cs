using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    /// <summary>
    /// Owns the Companion Mode lifecycle on the desktop: fetches a relay ticket, opens the
    /// relay socket, wires inbound frames to the provided command target, and forwards
    /// assistant deltas / turn completions back to the phone. The owner (MainWindow) supplies
    /// the access token, pairing id, status/snapshot providers, and the command target.
    /// </summary>
    public sealed class CompanionOrchestrator : IAsyncDisposable
    {
        private readonly IHostedCompanionClient _companionClient;
        private readonly Func<string?> _accessTokenProvider;
        private readonly Func<string> _deviceLabelProvider;
        private readonly Func<CompanionDesktopStatus> _statusProvider;
        private readonly Func<CompanionSessionSnapshotDto> _snapshotProvider;
        private readonly ICompanionCommandTarget _target;

        private CompanionRelayClient? _relay;
        private CompanionCommandHost? _host;
        private string? _activePairingId;
        private bool _enabled;
        private bool _disposed;

        public bool IsEnabled => _enabled;
        public string? ActivePairingId => _activePairingId;
        public CompanionRelayState RelayState => _host?.RelayState ?? CompanionRelayState.Disconnected;

        public CompanionOrchestrator(
            IHostedCompanionClient companionClient,
            Func<string?> accessTokenProvider,
            Func<string> deviceLabelProvider,
            Func<CompanionDesktopStatus> statusProvider,
            Func<CompanionSessionSnapshotDto> snapshotProvider,
            ICompanionCommandTarget target)
        {
            _companionClient = companionClient;
            _accessTokenProvider = accessTokenProvider;
            _deviceLabelProvider = deviceLabelProvider;
            _statusProvider = statusProvider;
            _snapshotProvider = snapshotProvider;
            _target = target;
        }

        /// <summary>
        /// Reconciles the orchestrator state with the current settings. Starts the relay when
        /// enabled and a pairing id is present; stops it otherwise. Returns the new state.
        /// </summary>
        public async Task ReconcileAsync(bool enabled, string? pairingId)
        {
            if (_disposed) return;

            if (!enabled || string.IsNullOrWhiteSpace(pairingId))
            {
                await StopAsync();
                return;
            }

            _enabled = true;

            // If already running for the same pairing, nothing to do.
            if (_relay != null && _host != null && string.Equals(_activePairingId, pairingId, StringComparison.Ordinal))
            {
                return;
            }

            await StopAsync();
            _activePairingId = pairingId;

            var accessToken = _accessTokenProvider();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Log.WriteLine("Companion orchestrator: no access token; staying idle.");
                return;
            }

            CompanionRelayTicketDto ticket;
            try
            {
                ticket = _companionClient.CreateRelayTicket(new CompanionRelayTicketRequestDto
                {
                    PairingId = pairingId!,
                    Role = "desktop"
                }, accessToken);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Companion orchestrator: relay ticket request failed: {ex.Message}");
                return;
            }

            _relay = new CompanionRelayClient(ticket.RelayUrl, ticket.Ticket, pairingId!, "desktop");
            _host = new CompanionCommandHost(_relay, _statusProvider, _snapshotProvider);

            WireCommandHost(_host);
            _host.Start();
            Log.WriteLine($"Companion orchestrator started for pairing {pairingId}");
        }

        public async Task StopAsync()
        {
            _enabled = false;
            if (_host != null)
            {
                try { await _host.StopAsync(); } catch { }
                _host = null;
            }
            if (_relay != null)
            {
                try { await _relay.DisposeAsync(); } catch { }
                _relay = null;
            }
            _activePairingId = null;
        }

        /// <summary>Forward an assistant stream delta to the phone as chat.delta.</summary>
        public void OnAssistantDelta(string text)
        {
            var host = _host;
            if (host == null || !_enabled) return;
            var requestId = _target.CurrentRequestId;
            if (string.IsNullOrEmpty(requestId)) return;
            _ = host.SendChatDeltaAsync(requestId, text);
        }

        /// <summary>Notify the phone that a chat turn started.</summary>
        public void OnChatStarted(string requestId, string turnId)
        {
            var host = _host;
            if (host == null || !_enabled) return;
            _ = host.SendChatStartedAsync(requestId, turnId);
        }

        /// <summary>Notify the phone that a chat turn completed and publish a snapshot.</summary>
        public Task OnTurnCompleted(string? requestId, bool succeeded, string? errorCode = null, string? errorMessage = null)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            if (!succeeded && !string.IsNullOrEmpty(errorCode) && !string.IsNullOrEmpty(requestId))
            {
                _ = host.SendChatFailedAsync(requestId!, errorCode!, errorMessage ?? string.Empty);
            }
            else if (succeeded && !string.IsNullOrEmpty(requestId))
            {
                _ = host.SendChatCompletedAsync(requestId!);
            }
            return host.PublishSnapshotAsync();
        }

        private void WireCommandHost(CompanionCommandHost host)
        {
            host.OnCaptureAsk = (requestId, displayId, prompt) =>
                _target.CaptureAsk(requestId, displayId, prompt);
            host.OnCaptureFull = (requestId, displayId, attachOnly) =>
                _target.CaptureFull(requestId, displayId, attachOnly);
            host.OnChatSend = (requestId, text) =>
                _target.ChatSend(requestId, text);
            host.OnChatCancel = requestId => _target.ChatCancel(requestId);
            host.OnChatNewTopic = () => _target.ChatNewTopic();
            host.OnDisplaySelect = displayId => _target.DisplaySelect(displayId);
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            await StopAsync();
        }
    }

    /// <summary>
    /// Surface that MainWindow implements so the CompanionOrchestrator can drive existing
    /// chat/capture behavior without the relay layer knowing about WPF.
    /// </summary>
    public interface ICompanionCommandTarget
    {
        string? CurrentRequestId { get; }
        void CaptureAsk(string requestId, string? displayId, string? prompt);
        void CaptureFull(string requestId, string? displayId, bool attachOnly);
        void ChatSend(string requestId, string text);
        void ChatCancel(string? requestId);
        void ChatNewTopic();
        void DisplaySelect(string? displayId);
    }
}
