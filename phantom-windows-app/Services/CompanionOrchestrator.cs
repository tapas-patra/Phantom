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
    /// the access token, pairing id, status/snapshot providers, the command target, and an
    /// overlay hide/show callback.
    /// </summary>
    public sealed class CompanionOrchestrator : IAsyncDisposable
    {
        private readonly IHostedCompanionClient _companionClient;
        private readonly Func<string?> _accessTokenProvider;
        private readonly Func<string> _deviceLabelProvider;
        private readonly Func<CompanionDesktopStatus> _statusProvider;
        private readonly Func<CompanionSessionSnapshotDto> _snapshotProvider;
        private readonly ICompanionCommandTarget _target;
        private readonly Action<bool> _setOverlayHidden;

        private CompanionRelayClient? _relay;
        private CompanionCommandHost? _host;
        private string? _activePairingId;
        private bool _enabled;
        private bool _disposed;
        private bool _overlayHiddenByCompanion;

        public bool IsEnabled => _enabled;
        public string? ActivePairingId => _activePairingId;
        public CompanionRelayState RelayState => _host?.RelayState ?? CompanionRelayState.Disconnected;
        public event Action? PairingBecameInvalid;

        public CompanionOrchestrator(
            IHostedCompanionClient companionClient,
            Func<string?> accessTokenProvider,
            Func<string> deviceLabelProvider,
            Func<CompanionDesktopStatus> statusProvider,
            Func<CompanionSessionSnapshotDto> snapshotProvider,
            ICompanionCommandTarget target,
            Action<bool> setOverlayHidden)
        {
            _companionClient = companionClient;
            _accessTokenProvider = accessTokenProvider;
            _deviceLabelProvider = deviceLabelProvider;
            _statusProvider = statusProvider;
            _snapshotProvider = snapshotProvider;
            _target = target;
            _setOverlayHidden = setOverlayHidden;
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

            // If already running for the same pairing, nothing to do.
            if (_relay != null && _host != null && _enabled
                && string.Equals(_activePairingId, pairingId, StringComparison.Ordinal))
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

            // Ticket provider fetches a fresh ticket on every connect (spec §9).
            Func<CancellationToken, Task<CompanionRelayTicketDto>> ticketProvider = _ =>
            {
                var token = _accessTokenProvider();
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("No access token for relay ticket.");
                }
                return Task.FromResult(_companionClient.CreateRelayTicket(new CompanionRelayTicketRequestDto
                {
                    PairingId = pairingId!,
                    Role = "desktop"
                }, token));
            };

            // Validate the first ticket fetch before flipping enabled / hiding overlay.
            try
            {
                _ = ticketProvider(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Companion orchestrator: initial relay ticket request failed: {ex.Message}");
                return;
            }

            _enabled = true;
            _relay = new CompanionRelayClient(ticketProvider, pairingId!, "desktop");
            _host = new CompanionCommandHost(_relay, _statusProvider, _snapshotProvider);

            WireCommandHost(_host);
            _host.RelayStateChanged += OnRelayStateChanged;
            _host.RelayError += OnRelayError;
            _host.Start();

            // Hide the overlay via the same path as the Ctrl+Alt+` hide shortcut (spec §7.4).
            if (!_overlayHiddenByCompanion)
            {
                _overlayHiddenByCompanion = true;
                try { _setOverlayHidden(true); } catch (Exception ex) { Log.WriteLine($"Companion overlay hide failed: {ex.Message}"); }
            }

            Log.WriteLine($"Companion orchestrator started for pairing {pairingId}");
        }

        private void OnRelayStateChanged(CompanionRelayState state)
        {
            if (state == CompanionRelayState.Connected)
            {
                var host = _host;
                if (host == null) return;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await host.SendDesktopHelloAsync();
                        await host.PublishSnapshotAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"Companion initial announce failed: {ex.Message}");
                    }
                });
            }
        }

        private void OnRelayError(string code, string message)
        {
            // pairing_revoked / replaced / server_shutdown are terminal for this session —
            // stop reconnecting so we don't hammer a dead/invalid ticket (M18).
            if (string.Equals(code, "pairing_revoked", StringComparison.Ordinal)
                || string.Equals(code, "replaced", StringComparison.Ordinal)
                || string.Equals(code, "server_shutdown", StringComparison.Ordinal))
            {
                Log.WriteLine($"Companion relay terminal error '{code}' — stopping companion mode.");
                PairingBecameInvalid?.Invoke();
                _ = StopAsync();
            }
        }

        public async Task StopAsync()
        {
            var wasEnabled = _enabled;
            _enabled = false;
            if (_host != null)
            {
                try { _host.RelayStateChanged -= OnRelayStateChanged; } catch { }
                try { _host.RelayError -= OnRelayError; } catch { }
                try { await _host.StopAsync(); } catch { }
                _host = null;
            }
            if (_relay != null)
            {
                try { await _relay.DisposeAsync(); } catch { }
                _relay = null;
            }
            _activePairingId = null;

            // Restore the overlay only if companion hid it.
            if (_overlayHiddenByCompanion)
            {
                _overlayHiddenByCompanion = false;
                if (wasEnabled)
                {
                    try { _setOverlayHidden(false); } catch (Exception ex) { Log.WriteLine($"Companion overlay restore failed: {ex.Message}"); }
                }
            }
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
        public void OnChatStarted(string? requestId, string turnId)
        {
            var host = _host;
            if (host == null || !_enabled) return;
            if (string.IsNullOrEmpty(requestId)) return;
            _ = host.SendChatStartedAsync(requestId, turnId);
            _ = host.PublishSnapshotAsync();
        }

        /// <summary>Notify the phone that a chat turn was cancelled and publish a snapshot.</summary>
        public Task OnTurnCancelled(string? requestId)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            if (!string.IsNullOrEmpty(requestId))
            {
                _ = host.SendChatCancelledAsync(requestId!);
            }
            return host.PublishSnapshotAsync();
        }

        /// <summary>Push current desktop.hello + session.snapshot immediately (lock start, etc.).</summary>
        public Task AnnounceReadyAsync()
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            return Task.Run(async () =>
            {
                try
                {
                    await host.SendDesktopHelloAsync();
                    await host.PublishSnapshotAsync();
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Companion announce failed: {ex.Message}");
                }
            });
        }

        public Task PublishSnapshotAsync()
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            return host.PublishSnapshotAsync();
        }

        public Task SendVoiceTranscriptAsync(string text, bool isFinal = true, bool sent = false)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            return host.SendVoiceTranscriptAsync(text, isFinal, sent);
        }

        public Task SendCaptureStartedAsync(string requestId, string? displayId)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            return host.SendCaptureStartedAsync(requestId, displayId);
        }

        public Task SendCaptureCompletedAsync(string requestId, int width, int height, string? thumbnailJpegBase64)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            return host.SendCaptureCompletedAsync(requestId, width, height, thumbnailJpegBase64);
        }

        public Task SendCaptureFailedAsync(string requestId, string code, string message)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
            return host.SendCaptureFailedAsync(requestId, code, message);
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

        /// <summary>Notify the phone that a capture lifecycle event completed and publish a snapshot.</summary>
        public Task OnCaptureCompleted(string? requestId)
        {
            var host = _host;
            if (host == null || !_enabled) return Task.CompletedTask;
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
            host.OnVoiceStart = () => _target.VoiceStart();
            host.OnVoiceStop = () => _target.VoiceStop();
            host.OnVoiceTranscript = (text, sent) => _target.VoiceTranscript(text, sent);
            host.OnRuntimeSelect = (provider, model) => _target.RuntimeSelect(provider, model);
            host.OnCaptureRemove = index => _target.CaptureRemove(index);
            host.OnCaptureClear = () => _target.CaptureClear();
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
        void VoiceStart();
        void VoiceStop();
        void VoiceTranscript(string text, bool sent);
        void RuntimeSelect(string? provider, string? model);
        void CaptureRemove(int index);
        void CaptureClear();
    }
}
