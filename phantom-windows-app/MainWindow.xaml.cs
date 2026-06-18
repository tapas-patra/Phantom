using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SecureOverlay.Application.Billing;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Interviews;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Sync;
using SecureOverlay.Application.Telemetry;
using SecureOverlay.Services;
using SecureOverlay.Helpers;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Context;
using SecureOverlay.Infrastructure.Billing;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using SecureOverlay.Infrastructure.Interviews;
using SecureOverlay.Infrastructure.Persistence;
using SecureOverlay.Infrastructure.Sync;
using SecureOverlay.Infrastructure.Telemetry;
using System.Windows.Media.Imaging;
using System.IO; 

namespace SecureOverlay
{
    public partial class MainWindow : Window
    {
        private IntPtr _windowHandle;
        private IntPtr _hookID = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _proc;
        private bool _isHidden = false;
        
        private bool _isRestarting = false;
        private bool _cleanupPerformed = false;

        private AppSettings _settings;
        private IAIService? _currentAI;
        private VoiceInputService? _voiceService;
        private DebugLogger _debugLogger;
        
        private ConversationManager? _conversationManager;

        // ═══════════════════════════════════════════════════════════════
        // Cursor Manager (Two-Cursor System)
        // ═══════════════════════════════════════════════════════════════
        private CursorManager? _cursorManager;
        private bool _isDraggingWindow = false;

        // Streaming state
        private Paragraph? _currentStreamingParagraph = null;
        private StringBuilder _streamBuffer = new StringBuilder();
        private System.Windows.Threading.DispatcherTimer? _streamUpdateTimer = null;
        private CancellationTokenSource? _currentRequestCancellation = null;
        private bool _isProcessingRequest = false;

        private bool _autoSendAfterVoice = false;
        private System.Windows.Threading.DispatcherTimer? _voiceCompletionTimer;

        // ═══════════════════════════════════════════════════════════════
        // NEW: Settings Page
        // ═══════════════════════════════════════════════════════════════
        private SettingsPage? _settingsPage;

        private APIRotationManager? _rotationManager;
        private System.Windows.Threading.DispatcherTimer? _switchNotificationTimer;

        // ═══════════════════════════════════════════════════════════════
        // P/INVOKE FOR HIDING FROM TASK VIEW (WIN+TAB)
        // ═══════════════════════════════════════════════════════════════
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private TaskViewMonitor _taskViewMonitor; 
        
        private BitmapImage? _attachedScreenshot = null;

        private Window? _currentDropdownMenu = null;
        private readonly AppLaunchContext _launchContext;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IHostedAccountClient _hostedAccountClient;
        private readonly ICreditMeteringService _creditMeteringService;
        private readonly IContextPackService _contextPackService;
        private readonly IInterviewLockService _interviewLockService;
        private readonly IUsageReconciliationService _usageReconciliationService;
        private readonly IKnowledgeRetrievalService _knowledgeRetrievalService;
        private readonly ITelemetryService _telemetryService;
        private readonly IAccountCacheRepository _accountCacheRepository;
        private readonly HostedRuntimeOptions _hostedRuntimeOptions;
        private System.Windows.Threading.DispatcherTimer? _interviewLockHeartbeatTimer;
        private System.Windows.Threading.DispatcherTimer? _sessionStatusTimer;
        private System.Windows.Threading.DispatcherTimer? _sessionInactivityTimer;
        private AccountCacheSnapshot? _accountSnapshot;
        private bool _sessionExtensionOptInRequired;
        private bool _isBoundaryFinalizationInProgress;
        private string? _forcedManagedExtensionProviderId;
        private DateTime? _lastInterviewActivityUtc;

        public MainWindow() : this(new AppLaunchContext())
        {
        }

        public MainWindow(AppLaunchContext launchContext)
        {
            _launchContext = launchContext;
            InitializeComponent();
            WindowProtection.MakeInvisibleToScreenCapture(this);

            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("MAIN WINDOW CONSTRUCTOR");
            Log.WriteLine("═══════════════════════════════════════════════════════");

            _debugLogger = DebugLogger.Instance;
            Log.WriteLine($"Debug logger instance obtained ({_debugLogger.LogMessages.Count} messages already captured)");
            
            DebugLogsList.ItemsSource = _debugLogger.LogMessages;
            Log.WriteLine("Debug logs bound to UI");
            
            _debugLogger.LogMessages.CollectionChanged += (s, e) =>
            {
                if (AutoScrollCheckBox.IsChecked == true)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DebugScrollViewer.ScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            Log.WriteLine("Loading settings...");
            _settings = SettingsManager.Load();
            Log.WriteLine($"Settings loaded: AI={_settings.SelectedAI}, Voice={_settings.VoiceInputEnabled}");

            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            _authSessionRepository = new SqliteAuthSessionRepository(store);
            _accountCacheRepository = new SqliteAccountCacheRepository(store);
            IInterviewSessionRepository interviewSessionRepository = new SqliteInterviewSessionRepository(store);
            IContextPackRepository contextPackRepository = new SqliteContextPackRepository(store);
            IUsageReconciliationRepository usageReconciliationRepository = new SqliteUsageReconciliationRepository(store);
            ITelemetryRepository telemetryRepository = new SqliteTelemetryRepository(store);
            _hostedRuntimeOptions = HostedClientFactory.LoadOptions();
            _hostedAccountClient = HostedClientFactory.CreateAccountClient(_hostedRuntimeOptions);
            _creditMeteringService = new LocalCreditMeteringService(
                _authSessionRepository,
                _accountCacheRepository,
                interviewSessionRepository);
            _contextPackService = new LocalContextPackService(contextPackRepository);
            _knowledgeRetrievalService = new HostedKnowledgeRetrievalService(
                new LocalKnowledgeRetrievalService(),
                _authSessionRepository,
                _accountCacheRepository,
                _hostedAccountClient);
            _interviewLockService = new LocalInterviewLockService(
                interviewSessionRepository,
                _accountCacheRepository);
            _usageReconciliationService = new LocalUsageReconciliationService(
                usageReconciliationRepository,
                HostedClientFactory.CreateUsageClient(_hostedRuntimeOptions));
            _telemetryService = new HostedTelemetryService(
                telemetryRepository,
                HostedClientFactory.CreateTelemetryClient(_hostedRuntimeOptions),
                _hostedRuntimeOptions);
            _accountSnapshot = _accountCacheRepository.Load();
            RefreshManagedCatalogCache();

            var activeInterviewSession = _creditMeteringService.GetActiveSession();
            if (activeInterviewSession != null)
            {
                ActivateInterviewLock(activeInterviewSession);
                _lastInterviewActivityUtc = DateTime.UtcNow;
            }

            var reconciliationFlush = _usageReconciliationService.FlushPending();
            if (reconciliationFlush.PendingBefore > 0)
            {
                Log.WriteLine(
                    $"Usage reconciliation flush: pending={reconciliationFlush.PendingBefore}, " +
                    $"synced={reconciliationFlush.SyncedCount}, failed={reconciliationFlush.FailedCount}");
                LogUsageQueueSnapshot();
                _telemetryService.Track("sync", "usage_reconciliation_flush", new Dictionary<string, string>
                {
                    ["pending"] = reconciliationFlush.PendingBefore.ToString(),
                    ["synced"] = reconciliationFlush.SyncedCount.ToString(),
                    ["failed"] = reconciliationFlush.FailedCount.ToString()
                });
            }
            
            // ✅ UPDATED: Check for cached conversation in SEPARATE file
            bool hasRestoredConversation = false;
            ConversationCache? cachedConversation = null;
            
            cachedConversation = SettingsManager.LoadConversationCache();
            if (cachedConversation != null && cachedConversation.Messages != null && cachedConversation.Messages.Count > 1)
            {
                Log.WriteLine($"✓ Found cached conversation: {cachedConversation.Messages.Count} messages");
                hasRestoredConversation = true;
            }

            InitializeAI();
            InitializeVoice();

            // Initialize Task View monitor
            _taskViewMonitor = new TaskViewMonitor(this);
            _taskViewMonitor.StartMonitoring();
            
            Log.WriteLine("✓ Task View monitor initialized");

            // ═══════════════════════════════════════════════════════════════
            // RESTORE CONVERSATION IMMEDIATELY (not in Loaded event)
            // ═══════════════════════════════════════════════════════════════
            if (hasRestoredConversation && _conversationManager != null && cachedConversation != null && cachedConversation.Messages != null)
            {
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("RESTORING CACHED CONVERSATION");
                Log.WriteLine($"Messages to restore: {cachedConversation.Messages.Count}");
                Log.WriteLine($"Saved at: {cachedConversation.SavedAt}");
                Log.WriteLine("═══════════════════════════════════════════════════════");

                try
                {
                    _conversationManager.ImportConversation(cachedConversation.Messages);
                    
                    int messageCount = _conversationManager?.GetMessageCount() ?? 0;
                    Log.WriteLine($"✓ Imported {messageCount} messages into ConversationManager");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"✗ Error restoring conversation: {ex.Message}");
                    hasRestoredConversation = false;
                }
            }

            _cursorManager = new CursorManager(
                this, 
                CustomCursorCanvas, 
                _settings.UseFakeCursor,
                _settings.FakeCursorSize
            );
            Log.WriteLine("Two-cursor system initialized");

            _proc = HookCallback;
            this.Loaded += MainWindow_Loaded;
            this.Closing += (s, e) => Cleanup();

            this.Activated += (s, e) => FocusInput();

            this.MouseEnter += MainWindow_MouseEnter;
            this.MouseLeave += MainWindow_MouseLeave;

            Log.WriteLine("Setting up input box placeholder behavior...");
            
            InputTextBox.GotFocus += (s, e) => 
            { 
                if (InputTextBox.Text == "Ask me anything...") 
                {
                    InputTextBox.Text = ""; 
                    InputTextBox.Foreground = Brushes.White;
                }
                
                InputBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 170, 255));
            };
            
            InputTextBox.LostFocus += (s, e) => 
            { 
                if (string.IsNullOrWhiteSpace(InputTextBox.Text)) 
                {
                    InputTextBox.Text = "Ask me anything...";
                    InputTextBox.Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
                }
                
                InputBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(112, 255, 255, 255));
            };

            // ═══════════════════════════════════════════════════════════════
            // Rebuild chat UI from restored conversation
            // ═══════════════════════════════════════════════════════════════
            this.Loaded += (s, e) =>
            {
                // Update API key indicator AFTER window is loaded
                Log.WriteLine("Window loaded - updating API key indicator...");
                UpdateAPIKeyIndicator();
                
                if (hasRestoredConversation && 
                    _conversationManager != null && 
                    cachedConversation != null &&
                    cachedConversation.Messages != null)
                {
                    Log.WriteLine("Rebuilding chat UI from restored conversation...");
                    
                    try
                    {
                        MarkdownHelper.ClearDocument(ChatDocument);
                        
                        var displayMessages = cachedConversation.Messages
                            .Where(m => m.Role != "system")
                            .ToList();
                        
                        Log.WriteLine($"Rebuilding UI with {displayMessages.Count} messages...");
                        
                        int rebuilt = 0;
                        foreach (var msg in displayMessages)
                        {
                            var isUser = msg.Role == "user";
                            var aiName = _currentAI?.GetProviderName() ?? "AI";
                            var prefix = isUser ? "**You:** " : $"**{aiName}:** ";
                            var fullText = prefix + msg.Content;
                            
                            MarkdownHelper.AppendMarkdown(ChatDocument, fullText, isUser);
                            rebuilt++;
                        }
                        
                        Log.WriteLine($"✓ Rebuilt {rebuilt} messages in chat UI");

                        UpdateTokenCounter();

                        var savedTime = cachedConversation.SavedAt.ToString("HH:mm:ss");
                        StatusText.Text = $"✓ Restored conversation ({rebuilt} messages from {savedTime})";
                        StatusIndicator.Fill = Brushes.LightGreen;
                        
                        var restoreTimer = new System.Windows.Threading.DispatcherTimer 
                        { 
                            Interval = TimeSpan.FromSeconds(5) 
                        };
                        restoreTimer.Tick += (ts, te) =>
                        {
                            StatusText.Text = "✓ Protected | Two-cursor system active";
                            restoreTimer.Stop();
                        };
                        restoreTimer.Start();

                        SettingsManager.ClearConversationCache();
                        Log.WriteLine("✓ Cache cleared after successful restore");
                        
                        Log.WriteLine("═══════════════════════════════════════════════════════");
                        Log.WriteLine("✓ CONVERSATION FULLY RESTORED");
                        Log.WriteLine($"  Context messages: {_conversationManager.GetMessageCount()}");
                        Log.WriteLine($"  UI messages: {rebuilt}");
                        Log.WriteLine($"  Token count: {_conversationManager.GetContextTokens()}");
                        Log.WriteLine("═══════════════════════════════════════════════════════");
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"✗ Error rebuilding chat UI: {ex.Message}");
                        Log.WriteLine($"Stack trace: {ex.StackTrace}");
                        
                        MarkdownHelper.ClearDocument(ChatDocument);
                        MarkdownHelper.AddWelcomeMessage(ChatDocument);
                    }
                }
                else
                {
                    if (ChatDocument.Blocks.Count == 0)
                    {
                        Log.WriteLine("No cached conversation - showing welcome message");
                        MarkdownHelper.AddWelcomeMessage(ChatDocument);
                    }
                }
            };

            Log.WriteLine("✓ Main window constructor complete");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        private bool IsInterviewStartBlocked()
        {
            return _launchContext != null && !_launchContext.CanStartInterview;
        }

        private bool CanContinueRestrictedInterview()
        {
            return _launchContext != null
                && _launchContext.CanResumeLockedInterview
                && _creditMeteringService.GetActiveSession() != null;
        }

        private void ApplyLaunchRestrictions()
        {
            if (!IsInterviewStartBlocked())
            {
                LaunchRestrictionBanner.Visibility = Visibility.Collapsed;
                return;
            }

            LaunchRestrictionTitle.Text = string.IsNullOrWhiteSpace(_launchContext.Title)
                ? "Restricted Session"
                : _launchContext.Title;

            var detail = string.IsNullOrWhiteSpace(_launchContext.Detail)
                ? _launchContext.Message
                : $"{_launchContext.Message} {_launchContext.Detail}".Trim();

            LaunchRestrictionDetails.Text = detail;
            LaunchRestrictionBanner.Visibility = Visibility.Visible;

            if (CanContinueRestrictedInterview())
            {
                StatusText.Text = $"⚠️ {_launchContext.Title}";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat(
                    $"⚠️ **{_launchContext.Title}**\n\n{_launchContext.Message}\n\n{_launchContext.Detail}\n\nExisting locked interview continuation is still allowed on this device.",
                    true);
                return;
            }

            InputTextBox.Text = "Interview start is blocked for this account state.";
            InputTextBox.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            InputTextBox.IsReadOnly = true;
            InputTextBox.IsEnabled = false;
            VoiceButton.IsEnabled = false;
            ScreenshotButton.IsEnabled = false;
            SendButton.IsEnabled = false;
            InputBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 176, 0));

            StatusText.Text = $"⚠️ {_launchContext.Title}";
            StatusIndicator.Fill = Brushes.Orange;

            AddToChat(
                $"⚠️ **{_launchContext.Title}**\n\n{_launchContext.Message}\n\n{_launchContext.Detail}",
                true);
        }

        private void RefreshAccountSnapshot()
        {
            _accountSnapshot = _accountCacheRepository.Load();
        }

        private void ApplyAccountTierChrome()
        {
            ProviderSelectorBorder.Visibility = Visibility.Visible;
            ModelSelectorBorder.Visibility = Visibility.Visible;
            DebugButton.Visibility = HasByoEntitlement() ? Visibility.Visible : Visibility.Collapsed;
            if (!HasByoEntitlement())
            {
                DebugPanel.Visibility = Visibility.Collapsed;
            }

            if (!HasByoEntitlement())
            {
                APIKeyIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateCreditIndicator()
        {
            RefreshAccountSnapshot();
            if (_accountSnapshot == null)
            {
                CreditIndicatorText.Text = "Credits: unavailable";
                return;
            }

            if (IsFreeTrialAccount())
            {
                CreditIndicatorText.Text = "Free Trial | 2 x 15 min demo blocks";
                return;
            }

            var hasPremiumLaneOrDebt = HasPremiumManagedEntitlement() || (_accountSnapshot.PremiumNegativeCredits > 0m);
            if (hasPremiumLaneOrDebt && HasByoEntitlement())
            {
                CreditIndicatorText.Text =
                    $"Premium -> BYO | Premium {_accountSnapshot.PremiumAvailableCredits:0.##} | BYO {_accountSnapshot.ProAvailableCredits:0.##} | Debt {_accountSnapshot.PremiumNegativeCredits:0.##}";
                return;
            }

            if (hasPremiumLaneOrDebt)
            {
                CreditIndicatorText.Text =
                    $"Premium | Credits {_accountSnapshot.PremiumAvailableCredits:0.##} | Debt {_accountSnapshot.PremiumNegativeCredits:0.##}";
                return;
            }

            CreditIndicatorText.Text = $"Pro BYO | Credits {_accountSnapshot.ProAvailableCredits:0.##} | Debt {_accountSnapshot.PremiumNegativeCredits:0.##}";
        }

        private void StartSessionStatusTimer()
        {
            _sessionStatusTimer?.Stop();
            _sessionStatusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _sessionStatusTimer.Tick += (s, e) => UpdateSessionStatus();
            _sessionStatusTimer.Start();
        }

        private void StartSessionInactivityTimer()
        {
            _sessionInactivityTimer?.Stop();
            _sessionInactivityTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _sessionInactivityTimer.Tick += (s, e) => CheckInterviewInactivity();
            _sessionInactivityTimer.Start();
        }

        private void UpdateSessionStatus()
        {
            var activeSession = _creditMeteringService.GetActiveSession();
            if (activeSession == null)
            {
                _lastInterviewActivityUtc = null;
                SessionTimerBorder.Visibility = Visibility.Collapsed;
                SessionStatusText.Text = string.Empty;
                return;
            }

            if (activeSession.State == SecureOverlay.Domain.Enums.InterviewSessionState.Paused)
            {
                var pausedElapsed = _creditMeteringService.GetMeteredElapsed(activeSession);
                var pausedBilledMinutes = Math.Max(1, (int)Math.Ceiling(pausedElapsed.TotalSeconds / 60d));
                var pausedProjectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(pausedElapsed);

                SessionTimerBorder.Visibility = Visibility.Visible;
                SessionTimerText.Text = $"Session {pausedElapsed:hh\\:mm\\:ss}";
                SessionStatusText.Text = $"Paused | {pausedBilledMinutes} min | {pausedProjectedCharge:0.##} cr";
                return;
            }

            if (ShouldFinalizeAtCurrentBoundary(activeSession))
            {
                FinalizeActiveInterviewSessionAtBoundary();
                return;
            }

            var elapsed = _creditMeteringService.GetMeteredElapsed(activeSession);
            var billedMinutes = Math.Max(1, (int)Math.Ceiling(elapsed.TotalSeconds / 60d));
            var projectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(elapsed);

            SessionTimerBorder.Visibility = Visibility.Visible;
            SessionTimerText.Text = $"Session {elapsed:hh\\:mm\\:ss}";
            SessionStatusText.Text = $"Live | {billedMinutes} min | {projectedCharge:0.##} cr";
        }

        private void CheckInterviewInactivity()
        {
            var activeSession = _creditMeteringService.GetActiveSession();
            if (activeSession == null || activeSession.State != SecureOverlay.Domain.Enums.InterviewSessionState.Active)
            {
                return;
            }

            if (!_settings.AutoPauseOnInactivityEnabled || _isProcessingRequest)
            {
                return;
            }

            _lastInterviewActivityUtc ??= DateTime.UtcNow;
            var inactivityThreshold = TimeSpan.FromMinutes(Math.Max(5, _settings.AutoPauseOnInactivityMinutes));
            if (DateTime.UtcNow - _lastInterviewActivityUtc.Value < inactivityThreshold)
            {
                return;
            }

            PauseInterviewSessionForInactivity(inactivityThreshold);
        }

        private void RecordInterviewActivity(string reason)
        {
            if (_creditMeteringService.GetActiveSession() == null)
            {
                return;
            }

            _lastInterviewActivityUtc = DateTime.UtcNow;
        }

        private bool IsSessionExtensionEnabledForCurrentTier()
        {
            if (IsFreeTrialAccount())
            {
                return _settings.AllowFreeTrialSessionExtension;
            }

            if (HasPaidCreditExhaustionGate())
            {
                return _settings.AllowByoSessionExtension;
            }

            return true;
        }

        private bool ShouldFinalizeAtCurrentBoundary(InterviewSessionRecord session)
        {
            if (session.State == SecureOverlay.Domain.Enums.InterviewSessionState.Paused)
            {
                return false;
            }

            if (IsFreeTrialAccount())
            {
                return !IsSessionExtensionEnabledForCurrentTier()
                    && _creditMeteringService.GetMeteredElapsed(session) >= TimeSpan.FromMinutes(15);
            }

            if (!HasPaidCreditExhaustionGate())
            {
                return false;
            }

            if (IsSessionExtensionEnabledForCurrentTier())
            {
                return GetProjectedTotalPremiumDebt(session) >= 1.0m;
            }

            var projectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(_creditMeteringService.GetMeteredElapsed(session));
            return projectedCharge > GetTotalPaidCreditsAvailable();
        }

        private void PauseInterviewSessionForError(string reason)
        {
            if (!_creditMeteringService.PauseActiveSession())
            {
                return;
            }

            Log.WriteLine($"Interview paused after error: {reason}");
            UpdateSessionStatus();
            _telemetryService.Track("billing", "interview_session_paused", new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["tier"] = _accountSnapshot?.AccessTier ?? "unknown"
            });
        }

        private void PauseInterviewSessionForInactivity(TimeSpan inactivityThreshold)
        {
            if (!_creditMeteringService.PauseActiveSession())
            {
                return;
            }

            _lastInterviewActivityUtc = null;
            Log.WriteLine($"Interview paused after inactivity: threshold={inactivityThreshold.TotalMinutes:0} minutes");
            UpdateSessionStatus();
            StatusText.Text = "⏸️ Interview auto-paused";
            StatusIndicator.Fill = Brushes.Orange;
            AddToChat(
                $"⏸️ **Interview auto-paused**\n\nNo active question/answer activity was detected for {inactivityThreshold.TotalMinutes:0} minutes. Send another message to resume the session.",
                false);
            _telemetryService.Track("billing", "interview_session_auto_paused", new Dictionary<string, string>
            {
                ["reason"] = "inactivity",
                ["threshold_minutes"] = inactivityThreshold.TotalMinutes.ToString("0"),
                ["tier"] = _accountSnapshot?.AccessTier ?? "unknown"
            });
        }

        private void ResumeInterviewSessionAfterSuccess()
        {
            if (!_creditMeteringService.ResumePausedSession())
            {
                return;
            }

            _lastInterviewActivityUtc = DateTime.UtcNow;
            Log.WriteLine("Interview resumed after successful response");
            UpdateSessionStatus();
            _telemetryService.Track("billing", "interview_session_resumed", new Dictionary<string, string>
            {
                ["tier"] = _accountSnapshot?.AccessTier ?? "unknown"
            });
        }

        private void FinalizeActiveInterviewSessionAtBoundary()
        {
            if (_isBoundaryFinalizationInProgress)
            {
                return;
            }

            _isBoundaryFinalizationInProgress = true;
            try
            {
                if (_isProcessingRequest && _currentRequestCancellation != null)
                {
                    Log.WriteLine("Boundary reached during active response - cancelling in-flight request");
                    _currentRequestCancellation.Cancel();
                }

                var completion = _creditMeteringService.FinalizeActiveSession();
                if (completion == null)
                {
                    return;
                }

                _usageReconciliationService.Enqueue(new UsageReconciliationPayload
                {
                    UserId = completion.UserId,
                    SessionId = completion.SessionId,
                    StartedAtUtc = completion.StartedAtUtc,
                    EndedAtUtc = completion.EndedAtUtc,
                    ChargedCredits = completion.ChargedCredits,
                    ChargedBlocks = completion.ChargedBlocks,
                    PremiumDebtAdded = completion.PremiumDebtAdded
                });
                var reconciliationFlush = _usageReconciliationService.FlushPending();
                _interviewLockService.MarkLockReleased();
                _interviewLockHeartbeatTimer?.Stop();
                _interviewLockHeartbeatTimer = null;
                _lastInterviewActivityUtc = null;
                _sessionExtensionOptInRequired = IsFreeTrialAccount() || HasPaidCreditExhaustionGate();

                RefreshAccountSnapshot();
                UpdateCreditIndicator();
                UpdateSessionStatus();

                var title = IsFreeTrialAccount() ? "Free Trial Block Complete" : "Paid Credits Exhausted";
                var message = IsFreeTrialAccount()
                    ? "The first 15-minute demo block has ended. Enable session extension in Settings if you want to continue into the next free-trial block."
                    : (IsSessionExtensionEnabledForCurrentTier()
                        ? "The current interview reached the maximum 1.0 credit managed extension limit. Start a new session after clearing the Premium debt or restoring your BYO provider."
                        : "The current interview has consumed the available paid credits. Enable paid session extension in Settings if you want the interview to continue beyond the available Premium and BYO credits.");

                StatusText.Text = $"⚠️ {title}";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat($"⚠️ **{title}**\n\n{message}", false);

                Log.WriteLine(
                    $"Boundary finalization complete: session={completion.SessionId}, blocks={completion.ChargedBlocks}, " +
                    $"charged={completion.ChargedCredits:0.##}, premiumDebt={completion.PremiumDebtAdded:0.##}");
                Log.WriteLine(
                    $"Boundary usage reconciliation: pending={reconciliationFlush.PendingBefore}, " +
                    $"synced={reconciliationFlush.SyncedCount}, failed={reconciliationFlush.FailedCount}");
                LogUsageQueueSnapshot();
                _telemetryService.Track("billing", "interview_session_boundary_finalized", new Dictionary<string, string>
                {
                    ["session_id"] = completion.SessionId,
                    ["charged_credits"] = completion.ChargedCredits.ToString("0.##"),
                    ["charged_blocks"] = completion.ChargedBlocks.ToString(),
                    ["tier"] = _accountSnapshot?.AccessTier ?? "unknown"
                });
            }
            finally
            {
                _isBoundaryFinalizationInProgress = false;
            }
        }

        private static string GetTierLabel(string? accessTier)
        {
            return accessTier?.ToLowerInvariant() switch
            {
                "premium" => "Premium",
                "pro_byo" => "Pro BYO",
                _ => "Free"
            };
        }

        private bool IsPremiumAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "premium", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasPremiumManagedEntitlement()
        {
            return !IsFreeTrialAccount()
                && (((_accountSnapshot?.PremiumAvailableCredits ?? 0m) > 0m)
                    || string.Equals(_accountSnapshot?.AccessTier, "premium", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsByoAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "pro_byo", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasByoEntitlement()
        {
            return !IsFreeTrialAccount()
                && (((_accountSnapshot?.ProAvailableCredits ?? 0m) > 0m)
                    || string.Equals(_accountSnapshot?.AccessTier, "pro_byo", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsFreeTrialAccount()
        {
            return string.Equals(_accountSnapshot?.AccessTier, "free", StringComparison.OrdinalIgnoreCase);
        }

        private decimal GetTotalPaidCreditsAvailable()
        {
            var premiumCredits = _accountSnapshot?.PremiumAvailableCredits ?? 0m;
            var byoCredits = HasByoEntitlement()
                ? (_accountSnapshot?.ProAvailableCredits ?? 0m)
                : 0m;
            return premiumCredits + byoCredits;
        }

        private bool HasPaidCreditExhaustionGate()
        {
            return !IsFreeTrialAccount() && (HasPremiumManagedEntitlement() || HasByoEntitlement());
        }

        private void RefreshManagedCatalogCache()
        {
            try
            {
                var session = _authSessionRepository.Load();
                if (session == null || !session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
                {
                    return;
                }

                var catalog = _hostedAccountClient.GetManagedCatalog(session.AccessToken);
                if (catalog != null)
                {
                    _settings.PremiumConfiguredProviders = (catalog.Providers ?? new List<ManagedAiProviderOptionDto>())
                        .Select(item => item.ProviderId)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    ProviderModelCatalogCache.MergeCatalog(_settings, catalog);
                    SettingsManager.Save(_settings);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Managed catalog refresh skipped: {ex.Message}");
            }
        }

        private ManagedAiProviderOptionDto? GetManagedProviderCatalog(string provider)
        {
            return ProviderModelCatalogCache.GetProvider(_settings, provider);
        }

        private string[] GetConfiguredModelsForProvider(string provider)
        {
            return ProviderModelCatalogCache.GetModelIds(_settings, provider);
        }

        private ModelConfig GetModelConfigForCurrentSelection(string provider, string modelId)
        {
            var registryConfig = AIModelRegistry.GetModelConfig(modelId);
            if (!string.Equals(registryConfig.Name, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return registryConfig;
            }

            var managedModel = ProviderModelCatalogCache.GetModel(_settings, provider, modelId);
            return new ModelConfig
            {
                Name = managedModel?.DisplayName ?? modelId,
                MaxContextTokens = 128000,
                MaxResponseTokens = 4000,
                SlidingWindowSize = 15
            };
        }

        private string GetModelDisplayName(string provider, string modelId)
        {
            var registryName = AIModelRegistry.GetDisplayName(modelId);
            if (!string.Equals(registryName, modelId, StringComparison.Ordinal)
                && !string.Equals(registryName, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return registryName;
            }

            return ProviderModelCatalogCache.GetModel(_settings, provider, modelId)?.DisplayName
                ?? modelId;
        }

        private SecureOverlay.Domain.Enums.InterviewUsageSource DetermineUsageSourceForCurrentRuntime(string provider)
        {
            if (IsFreeTrialAccount())
            {
                return SecureOverlay.Domain.Enums.InterviewUsageSource.FreeTrialManaged;
            }

            if (_currentAI is HostedManagedAiService)
            {
                if (_settings.AllowByoSessionExtension
                    && (!HasPremiumManagedEntitlement() || !PremiumCreditsCanStillCoverCurrentSession()))
                {
                    return SecureOverlay.Domain.Enums.InterviewUsageSource.PremiumDebtExtension;
                }

                return SecureOverlay.Domain.Enums.InterviewUsageSource.PremiumManaged;
            }

            return SecureOverlay.Domain.Enums.InterviewUsageSource.ProByo;
        }

        private decimal GetProjectedPremiumExtensionCharge(InterviewSessionRecord session)
        {
            var meteredElapsed = _creditMeteringService.GetMeteredElapsed(session);
            var requestedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(meteredElapsed);
            var totalMeteredSeconds = Math.Max(0, (int)Math.Floor(meteredElapsed.TotalSeconds));
            var debtSeconds = 0;

            foreach (var segment in session.UsageSegments ?? Enumerable.Empty<SecureOverlay.Domain.Entities.InterviewSessionUsageSegment>())
            {
                if (segment.Source != SecureOverlay.Domain.Enums.InterviewUsageSource.PremiumDebtExtension)
                {
                    continue;
                }

                var segmentEnd = segment.EndedMeteredSecond ?? totalMeteredSeconds;
                debtSeconds += Math.Max(0, segmentEnd - segment.StartedMeteredSecond);
            }

            if (debtSeconds <= 0 || totalMeteredSeconds <= 0)
            {
                return 0m;
            }

            return Math.Round(requestedCharge * debtSeconds / totalMeteredSeconds, 2, MidpointRounding.AwayFromZero);
        }

        private decimal GetProjectedTotalPremiumDebt(InterviewSessionRecord session)
        {
            var existingDebt = _accountSnapshot?.PremiumNegativeCredits ?? 0m;
            var projectedExtensionDebt = GetProjectedPremiumExtensionCharge(session);
            return Math.Round(existingDebt + projectedExtensionDebt, 2, MidpointRounding.AwayFromZero);
        }

        private static bool IsManagedProvider(string provider)
        {
            return provider == AIModelRegistry.Providers.ChatGPT
                || provider == AIModelRegistry.Providers.Claude
                || provider == AIModelRegistry.Providers.Gemini
                || provider == AIModelRegistry.Providers.Mistral
                || provider == AIModelRegistry.Providers.Groq
                || provider == AIModelRegistry.Providers.Nvidia;
        }

        private bool HasConfiguredByoKeysForProvider(string provider)
        {
            return _rotationManager != null && _rotationManager.GetTotalKeyCount(provider) > 0;
        }

        private bool PremiumCreditsCanStillCoverCurrentSession()
        {
            var premiumCredits = _accountSnapshot?.PremiumAvailableCredits ?? 0m;
            if (premiumCredits <= 0m)
            {
                return false;
            }

            var activeSession = _creditMeteringService.GetActiveSession();
            if (activeSession == null)
            {
                return true;
            }

            var projectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(_creditMeteringService.GetMeteredElapsed(activeSession));
            return projectedCharge <= premiumCredits;
        }

        private bool ShouldUseByoRuntimeForCurrentSelection(string provider)
        {
            if (IsFreeTrialAccount() || !HasByoEntitlement())
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_forcedManagedExtensionProviderId)
                && string.Equals(_forcedManagedExtensionProviderId, provider, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!IsManagedProvider(provider))
            {
                return true;
            }

            if (HasPremiumManagedEntitlement() && PremiumCreditsCanStillCoverCurrentSession())
            {
                return false;
            }

            if (HasConfiguredByoKeysForProvider(provider))
            {
                return true;
            }

            if (_settings.AllowByoSessionExtension)
            {
                return false;
            }

            return true;
        }

        private bool CanUseManagedExtensionFallbackForProvider(string provider)
        {
            return _settings.AllowByoSessionExtension
                && HasByoEntitlement()
                && IsManagedProvider(provider);
        }

        private void ForceManagedExtensionForCurrentProvider(string provider)
        {
            _forcedManagedExtensionProviderId = provider;
            InitializeAI();
        }

        private string[] GetAvailableProvidersForCurrentTier()
        {
            if (HasByoEntitlement())
            {
                return AIModelRegistry.GetAllProviders();
            }

            var providers = _settings.PremiumConfiguredProviders?
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (providers == null || providers.Length == 0)
            {
                providers = (_settings.ManagedAiCatalogCache?.Providers ?? new List<ManagedAiProviderOptionDto>())
                    .Select(item => item.ProviderId)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return providers != null && providers.Length > 0
                ? providers
                : new[] { _settings.SelectedAI };
        }

        private string[] GetAvailableModelsForSelectedProvider()
        {
            return GetConfiguredModelsForProvider(_settings.SelectedAI);
        }

        // ═══════════════════════════════════════════════════════════════
        // CURSOR MANAGEMENT - EVENT HANDLERS
        // ═══════════════════════════════════════════════════════════════

        private void MainWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingWindow)
                return;
            
            _cursorManager?.ActivateCustomCursor();
        }

        private void MainWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingWindow)
                return;
            
            _cursorManager?.DeactivateCustomCursor();
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var position = e.GetPosition(this);
            _cursorManager?.UpdateCustomCursorPosition(position);
        }

        // Public methods for settings preview
        public void ShowFakeCursorPreview()
        {
            _cursorManager?.ShowFakeCursorPreview();
        }

        public void HideFakeCursorPreview()
        {
            _cursorManager?.HideFakeCursorPreview();
        }

        public void UpdateFakeCursorPreviewSize(double scale)
        {
            _cursorManager?.UpdateFakeCursorSize(scale);
        }

        // ═══════════════════════════════════════════════════════════════
        // WINDOW LOADED
        // ═══════════════════════════════════════════════════════════════

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            
            if (_windowHandle == IntPtr.Zero)
            {
                Log.WriteLine("✗ CRITICAL: Failed to get window handle!");
                InvisibleMessageBox.Show("Failed to initialize window!", "Error");
                System.Windows.Application.Current.Shutdown();
                return;
            }

            Log.WriteLine($"Window Handle: 0x{_windowHandle:X}");

            try
            {
                _hookID = SetHook(_proc);
                Log.WriteLine($"Keyboard hook: {(_hookID != IntPtr.Zero ? "✓ Installed" : "✗ Failed")}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Hook error: {ex.Message}");
            }

            Log.WriteLine("Applying screen capture protection...");
            WindowProtection.ApplyProtection(_windowHandle);
            
            uint affinity;
            if (NativeMethods.GetWindowDisplayAffinity(_windowHandle, out affinity) && 
                affinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE)
            {
                StatusText.Text = "✓ Protected | Two-cursor system active";
                StatusIndicator.Fill = Brushes.LightGreen;
                Log.WriteLine("✓ Screen capture protection VERIFIED");
            }
            else
            {
                StatusText.Text = "⚠️ Protection may not be active";
                StatusIndicator.Fill = Brushes.Orange;
                Log.WriteLine($"⚠️ Protection verification failed. Affinity: 0x{affinity:X}");
            }

            // ✅ NEW: Initialize screenshot button visibility
            UpdateAPIKeyIndicator();
            UpdateScreenshotButtonVisibility();
            UpdateProviderAndModelDisplay();
            RefreshAccountSnapshot();
            ApplyAccountTierChrome();
            UpdateCreditIndicator();
            UpdateSessionStatus();
            StartSessionStatusTimer();
            StartSessionInactivityTimer();

            FocusInput();
            ApplyLaunchRestrictions();
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("✓ APPLICATION READY");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        // ═══════════════════════════════════════════════════════════════
        // AI INITIALIZATION (Preserve history on model switch)
        // ═══════════════════════════════════════════════════════════════

        private void InitializeAI()
        {
            Log.WriteLine("Initializing AI service...");

            // NEW: Configure debug mode
            var debugModeEnabled = HasByoEntitlement() && _settings.DebugModeEnabled;
            ErrorSimulator.IsDebugModeEnabled = debugModeEnabled;
            ErrorSimulator.SimulationMode = debugModeEnabled ? _settings.DebugErrorSimulation : "None";
            
            if (debugModeEnabled)
            {
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("🧪 DEBUG MODE ENABLED");
                Log.WriteLine($"   Simulation: {_settings.DebugErrorSimulation}");
                Log.WriteLine("   API errors will be simulated for testing!");
                Log.WriteLine("═══════════════════════════════════════════════════════");
                
                // Show debug warning banner
                if (DebugModeWarning != null)
                {
                    DebugModeWarning.Visibility = Visibility.Visible;
                    DebugModeText.Text = $"🧪 DEBUG MODE: Simulating {_settings.DebugErrorSimulation} Errors";
                    DebugModeDetails.Text = ErrorSimulator.GetStatus();
                }
            }
            else
            {
                // Hide debug warning banner
                if (DebugModeWarning != null)
                {
                    DebugModeWarning.Visibility = Visibility.Collapsed;
                }
            }

            // Initialize rotation manager
            _rotationManager = new APIRotationManager(_settings);
            
            Log.WriteLine($"✓ Rotation manager initialized");
            Log.WriteLine($"  Auto-switch keys: {_settings.AutoSwitchKeysOnError}");
            Log.WriteLine($"  Auto-switch models: {_settings.AutoSwitchModelsOnError}");
            
            // Log key counts for current provider
            var keyCount = _rotationManager.GetTotalKeyCount(_settings.SelectedAI);
            var availableKeys = _rotationManager.GetAvailableKeyCount(_settings.SelectedAI);
            Log.WriteLine($"  {_settings.SelectedAI} keys: {keyCount} total, {availableKeys} available");

            var allowedProviders = GetAvailableProvidersForCurrentTier();
            if (!allowedProviders.Contains(_settings.SelectedAI))
            {
                _settings.SelectedAI = allowedProviders.FirstOrDefault()
                    ?? _settings.ManagedAiCatalogCache?.Providers?.FirstOrDefault()?.ProviderId
                    ?? AIModelRegistry.Providers.ChatGPT;
            }
            
            // ✅ FIX: Get the CORRECT model from settings (not hardcoded default)
            var currentModel = _rotationManager.GetCurrentModel(_settings.SelectedAI);
            var allowedModels = GetAvailableModelsForSelectedProvider();
            if (!allowedModels.Contains(currentModel))
            {
                currentModel = allowedModels.FirstOrDefault() ?? currentModel;
                if (!string.IsNullOrWhiteSpace(currentModel))
                {
                    AIModelRegistry.SetModelForProvider(_settings, _settings.SelectedAI, currentModel);
                    SettingsManager.Save(_settings);
                }
            }
            
            Log.WriteLine($"✓ Loading model from settings: {currentModel}");

            var useByoRuntime = ShouldUseByoRuntimeForCurrentSelection(_settings.SelectedAI);
            
            // Create AI service with rotation
            IAIService newAI = useByoRuntime
                ? AIServiceFactory.CreateServiceWithRotation(_settings.SelectedAI, _rotationManager)
                : new HostedManagedAiService(
                    _authSessionRepository,
                    _hostedRuntimeOptions,
                    _settings.SelectedAI,
                    currentModel,
                    _settings.AllowByoSessionExtension);
            
            AIProviderText.Text = newAI.GetProviderName();

            var modelConfig = GetModelConfigForCurrentSelection(_settings.SelectedAI, currentModel);
            
            Log.WriteLine($"Model config: {modelConfig.Name} ({modelConfig.MaxContextTokens} tokens)");

            if (_conversationManager != null)
            {
                _conversationManager.APISwitchNotification -= OnAPISwitchNotification;
                _conversationManager.UpdateAIService(newAI);
                _conversationManager.UpdateModelConfig(modelConfig);
                _conversationManager.UpdateSystemPrompt(_settings.SystemPrompt);
                _conversationManager.SetRotationManager(_rotationManager);
                
                // Subscribe to API switch notifications
                _conversationManager.APISwitchNotification += OnAPISwitchNotification;
                
                Log.WriteLine("✓ AI service updated - conversation history PRESERVED");
            }
            else
            {
                _conversationManager = new ConversationManager(
                    newAI,
                    _settings.SystemPrompt,
                    modelConfig,
                    _rotationManager,
                    query => _knowledgeRetrievalService.RetrieveForPrompt(_contextPackService.GetSelectedPack(), query));
                
                // Subscribe to API switch notifications
                _conversationManager.APISwitchNotification += OnAPISwitchNotification;
                
                var selectedPack = _contextPackService.GetSelectedPack();

                if (!string.IsNullOrWhiteSpace(selectedPack.ResumeText))
                {
                    _conversationManager.SetResume(selectedPack.ResumeText, selectedPack.ResumeSummary);
                    Log.WriteLine($"Resume loaded ({_conversationManager.HasResume()})");
                }

                if (!string.IsNullOrWhiteSpace(selectedPack.JobDescriptionText))
                {
                    _conversationManager.SetJobDescription(selectedPack.JobDescriptionText, selectedPack.JobDescriptionSummary);
                    Log.WriteLine($"Job description loaded ({_conversationManager.HasJobDescription()})");
                }

                Log.WriteLine("✓ New conversation manager created with rotation support");
            }

            _currentAI = newAI;
            UpdateTokenCounter();

            if (!_currentAI.IsConfigured())
            {
                Log.WriteLine($"⚠️ {_currentAI.GetProviderName()} not configured (no API key)");
            }
            else
            {
                Log.WriteLine($"✓ AI service: {_currentAI.GetProviderName()} (configured)");
            }
            
            // NEW: Update indicator immediately after AI is initialized
            // But only if window is already loaded (during settings change)
    if (this.IsLoaded)
    {
        UpdateAPIKeyIndicator();
        UpdateScreenshotButtonVisibility();
        UpdateProviderAndModelDisplay();  // ✅ This updates the title bar
    }
}


        private void UpdateAPIKeyIndicator()
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("UPDATING API KEY INDICATOR");

            if (!HasByoEntitlement() || !ShouldUseByoRuntimeForCurrentSelection(_settings.SelectedAI))
            {
                Log.WriteLine("  Non-BYO runtime - hiding indicator");
                APIKeyIndicator.Visibility = Visibility.Collapsed;
                Log.WriteLine("═══════════════════════════════════════════════════════");
                return;
            }
            
            if (_rotationManager == null)
            {
                Log.WriteLine("  Rotation manager is null - hiding indicator");
                APIKeyIndicator.Visibility = Visibility.Collapsed;
                Log.WriteLine("═══════════════════════════════════════════════════════");
                return;
            }

            var provider = _settings.SelectedAI;
            var keyCount = _rotationManager.GetTotalKeyCount(provider);
            var currentIndex = _rotationManager.GetCurrentKeyIndex(provider);
            var availableKeys = _rotationManager.GetAvailableKeyCount(provider);
            
            Log.WriteLine($"  Provider: {provider}");
            Log.WriteLine($"  Total keys: {keyCount}");
            Log.WriteLine($"  Current index: {currentIndex}");
            Log.WriteLine($"  Available keys: {availableKeys}");
            
            if (keyCount > 1)
            {
                // Show indicator with key info
                APIKeyText.Text = $"Key #{currentIndex + 1}/{keyCount}";
                APIKeyIndicator.Visibility = Visibility.Visible;
                
                // Color code based on available keys
                if (availableKeys < keyCount / 2)
                {
                    // More than half failed - yellow warning
                    APIKeyIndicator.Background = new SolidColorBrush(Color.FromArgb(80, 255, 165, 0));
                }
                else
                {
                    // Most keys available - green
                    APIKeyIndicator.Background = new SolidColorBrush(Color.FromArgb(80, 0, 255, 0));
                }
                
                Log.WriteLine($"  ✓ Indicator VISIBLE: {APIKeyText.Text}");
            }
            else if (keyCount == 1)
            {
                // Single key - hide indicator (optional: can show "Key #1")
                APIKeyText.Text = "Key #1";
                APIKeyIndicator.Visibility = Visibility.Collapsed; // Change to Visible if you want to show it
                
                Log.WriteLine($"  Single key - indicator hidden");
            }
            else
            {
                // No keys
                APIKeyIndicator.Visibility = Visibility.Collapsed;
                Log.WriteLine($"  No keys - indicator hidden");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }


        // Add this new event handler
        private void OnAPISwitchNotification(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                Log.WriteLine($"🔄 API Switch: {message}");
                
                // Update key indicator
                UpdateAPIKeyIndicator();
                
                UpdateProviderAndModelDisplay();

                // ✅ NEW: Refresh settings page if it's open
                if (SettingsPageContainer.Visibility == Visibility.Visible && _settingsPage != null)
                {
                    Log.WriteLine("Settings page is open - refreshing to show new model");
                    _settingsPage.RefreshSettings();
                }
                
                // Flash the key indicator (animate)
                FlashAPIKeyIndicator();
                
                // Update screenshot button visibility in case model changed
                UpdateScreenshotButtonVisibility();
                
                // Show notification in status bar
                StatusText.Text = $"🔄 {message}";
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange
                
                // Reset status after 5 seconds
                _switchNotificationTimer?.Stop();
                _switchNotificationTimer = new System.Windows.Threading.DispatcherTimer 
                { 
                    Interval = TimeSpan.FromSeconds(5) 
                };
                _switchNotificationTimer.Tick += (s, e) =>
                {
                    StatusText.Text = "✓ Protected | Two-cursor system active";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    _switchNotificationTimer.Stop();
                };
                _switchNotificationTimer.Start();
            });
        }


        // Add animation for visual feedback
        private void FlashAPIKeyIndicator()
        {
            if (APIKeyIndicator.Visibility != Visibility.Visible)
                return;

            // Create flash animation
            var flashAnimation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = Color.FromArgb(80, 255, 165, 0), // Orange
                To = Color.FromArgb(80, 0, 255, 0),     // Green
                Duration = TimeSpan.FromMilliseconds(300),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
            };

            var brush = new SolidColorBrush(Color.FromArgb(80, 0, 255, 0));
            APIKeyIndicator.Background = brush;
            
            brush.BeginAnimation(SolidColorBrush.ColorProperty, flashAnimation);
        }

        private void UpdateTokenCounter()
        {
            if (_conversationManager == null)
            {
                TokenCounterText.Text = "0k/0k";
                return;
            }

            var contextTokens = _conversationManager.GetContextTokens();
            var maxTokens = _conversationManager.EstimateTokens("") > 0 ? 8000 : 8000;
            
            var contextK = contextTokens / 1000.0;
            var maxK = maxTokens / 1000.0;
            
            TokenCounterText.Text = $"{contextK:F1}k/{maxK:F0}k";
            
            var usage = (double)contextTokens / maxTokens;
            if (usage > 0.9)
            {
                TokenCounterText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            }
            else if (usage > 0.7)
            {
                TokenCounterText.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 0));
            }
            else
            {
                TokenCounterText.Foreground = Brushes.White;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // MESSAGE SENDING
        // ═══════════════════════════════════════════════════════════════

        private async Task SendMessage()
        {
            if (IsInterviewStartBlocked() && !CanContinueRestrictedInterview())
            {
                Log.WriteLine($"Interview start blocked by launch context: {_launchContext.Title}");
                StatusText.Text = $"⚠️ {_launchContext.Title}";
                StatusIndicator.Fill = Brushes.Orange;
                return;
            }

            var activeSessionBeforeSend = _creditMeteringService.GetActiveSession();
            if (activeSessionBeforeSend != null && ShouldFinalizeAtCurrentBoundary(activeSessionBeforeSend))
            {
                FinalizeActiveInterviewSessionAtBoundary();
                FocusInput();
                return;
            }

            if (_sessionExtensionOptInRequired && !IsSessionExtensionEnabledForCurrentTier())
            {
                var blockedTitle = IsFreeTrialAccount() ? "Free Trial Extension Disabled" : "Paid Session Extension Disabled";
                var blockedMessage = IsFreeTrialAccount()
                    ? "Enable session extension in Settings if you want to consume the next 15-minute free-trial block in this interview."
                    : "Enable paid session extension in Settings if you want this interview to continue after the available Premium and BYO credits are exhausted.";
                Log.WriteLine($"Interview continuation blocked: {blockedTitle}");
                StatusText.Text = $"⚠️ {blockedTitle}";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat($"⚠️ **{blockedTitle}**\n\n{blockedMessage}", false);
                FocusInput();
                return;
            }

            if (_sessionExtensionOptInRequired && IsSessionExtensionEnabledForCurrentTier())
            {
                _sessionExtensionOptInRequired = false;
            }

            RefreshAccountSnapshot();
            if (ShouldUseByoRuntimeForCurrentSelection(_settings.SelectedAI) != (_currentAI is not HostedManagedAiService))
            {
                InitializeAI();
            }

            var message = InputTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(message) || message == "Ask me anything...") 
            {
                if (_attachedScreenshot == null)
                {
                    Log.WriteLine("Send message called with empty input and no screenshot - ignoring");
                    return;
                }
                else
                {
                    // We have a screenshot but no text - add default message
                    message = "Please analyze this screenshot.";
                    Log.WriteLine("No text but screenshot attached - using default message");
                }
            }

            if (_currentAI == null || !_currentAI.IsConfigured())
            {
                if (HasByoEntitlement()
                    && ShouldUseByoRuntimeForCurrentSelection(_settings.SelectedAI)
                    && !HasConfiguredByoKeysForProvider(_settings.SelectedAI))
                {
                    Log.WriteLine("BYO runtime selected but no provider key is configured - pausing interview continuation");
                    PauseInterviewSessionForError("byo_provider_unavailable");
                    AddToChat(
                        $"⚠️ **{_settings.SelectedAI} key required**\n\n" +
                        $"This interview is currently using your BYO {_settings.SelectedAI} provider. Add a key in Settings to continue." +
                        (_settings.AllowByoSessionExtension
                            ? "\n\nManaged paid fallback is enabled, but Phantom could not switch to it for this request."
                            : "\n\nIf you want Phantom to continue with managed paid fallback when your BYO provider becomes unavailable, enable paid session extension in Settings."),
                        false);
                    StatusText.Text = "⚠️ BYO provider unavailable";
                    StatusIndicator.Fill = Brushes.Orange;
                    FocusInput();
                    return;
                }

                Log.WriteLine("AI not configured, cannot send message");
                PauseInterviewSessionForError("ai_not_configured");
                AddToChat("⚠️ **AI not configured!**\n\nClick ⚙️ Settings to configure your API key.", false);
                FocusInput();
                return;
            }

            if (_currentAI is HostedManagedAiService managedAi && !managedAi.HasUsableSession())
            {
                Log.WriteLine("Hosted desktop auth session is no longer valid - prompting re-login before metering");
                _authSessionRepository.Clear();
                StatusText.Text = "⚠️ Session expired";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat("⚠️ **Desktop session expired**\n\nPlease sign in again to continue.", false);
                FocusInput();
                return;
            }

            if (_attachedScreenshot != null && !CurrentModelSupportsVision())
            {
                Log.WriteLine("✗ Screenshot attached but current model doesn't support vision");
                InvisibleMessageBox.Show(
                    "The current AI model doesn't support image analysis.\n\n" +
                    "Please switch to a vision-capable model in Settings:\n" +
                    "- GPT-4 Vision/Turbo/4o\n" +
                    "- Claude 3 (any variant)\n" +
                    "- Gemini 1.5/2.0",
                    "Vision Not Supported"
                );
                return;
            }

            if (_conversationManager == null)
            {
                Log.WriteLine("✗ ConversationManager not initialized!");
                AddToChat("⚠️ **System error!** Please restart the application.", false);
                return;
            }

            var hadActiveInterview = _creditMeteringService.GetActiveSession() != null;
            var meteringActivation = _creditMeteringService.EnsureInterviewSession();
            if (!meteringActivation.Allowed)
            {
                Log.WriteLine($"Interview metering denied: {meteringActivation.Title} | {meteringActivation.Message}");
                StatusText.Text = $"⚠️ {meteringActivation.Title}";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat($"⚠️ **{meteringActivation.Title}**\n\n{meteringActivation.Message}", false);
                FocusInput();
                return;
            }

            if (meteringActivation.StartedNewSession || (!hadActiveInterview && meteringActivation.ResumedExistingSession))
            {
                Log.WriteLine($"{meteringActivation.Title}: {meteringActivation.Message}");
                StatusText.Text = $"✓ {meteringActivation.Title}";
                StatusIndicator.Fill = Brushes.LightGreen;
                _sessionExtensionOptInRequired = false;
                _telemetryService.Track("billing", "interview_session_activated", new Dictionary<string, string>
                {
                    ["started_new"] = meteringActivation.StartedNewSession.ToString(),
                    ["resumed"] = meteringActivation.ResumedExistingSession.ToString(),
                    ["session_id"] = meteringActivation.Session?.SessionId ?? string.Empty
                });
            }

            if (meteringActivation.Session != null)
            {
                ActivateInterviewLock(meteringActivation.Session);
                RecordInterviewActivity("session_active");
                RefreshAccountSnapshot();
                UpdateCreditIndicator();
                UpdateSessionStatus();
            }

            _creditMeteringService.TrackUsageSource(DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI), _settings.SelectedAI);

            string? imageBase64 = null;
            if (_attachedScreenshot != null)
            {
                imageBase64 = BitmapImageToBase64(_attachedScreenshot);
                if (imageBase64 != null)
                {
                    Log.WriteLine($"✓ Screenshot encoded for transmission ({imageBase64.Length} chars)");
                }
                else
                {
                    Log.WriteLine("✗ Failed to encode screenshot - sending without image");
                }
            }

            RecordInterviewActivity("request_started");

            if (_isProcessingRequest && _currentRequestCancellation != null)
            {
                Log.WriteLine("⚠️ Another request is in progress - cancelling it");
                
                _currentRequestCancellation.Cancel();
                _streamUpdateTimer?.Stop();
                _streamUpdateTimer = null;
                
                lock (_streamBuffer)
                {
                    _streamBuffer.Clear();
                }
                
                if (_currentStreamingParagraph != null)
                {
                    ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                    _currentStreamingParagraph = null;
                }
                
                await Task.Delay(200);
                
                Log.WriteLine("✓ Previous request cleaned up");
            }

            _currentRequestCancellation = new CancellationTokenSource();
            _isProcessingRequest = true;

            Log.WriteLine($"Sending message: '{message}'");
            AddToChat($"**You:** {message}", false);
            InputTextBox.Text = "";

            StatusText.Text = "🔄 Thinking...";
            StatusIndicator.Fill = Brushes.Yellow;
            
            var aiName = _currentAI.GetProviderName();
            
            lock (_streamBuffer)
            {
                _streamBuffer.Clear();
            }
            
            _currentStreamingParagraph = new Paragraph
            {
                Foreground = Brushes.White,
                Margin = new Thickness(0, 5, 0, 5)
            };
            
            var headerRun = new Run($"{aiName}:\n") { FontWeight = FontWeights.Bold };
            _currentStreamingParagraph.Inlines.Add(headerRun);
            ChatDocument.Blocks.Add(_currentStreamingParagraph);
            
            _streamUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            
            _streamUpdateTimer.Tick += StreamUpdateTimer_Tick;
            _streamUpdateTimer.Start();
            
            var startTime = DateTime.Now;

            try
            {
                Action<string> onChunk = (chunk) =>
                {
                    if (_currentRequestCancellation != null && !_currentRequestCancellation.Token.IsCancellationRequested)
                    {
                        Dispatcher.BeginInvoke(new Action(() => RecordInterviewActivity("response_stream")));
                        lock (_streamBuffer)
                        {
                            _streamBuffer.Append(chunk);
                        }
                    }
                };

                var (response, error) = await Task.Run(async () =>
                {
                    return await _conversationManager.SendMessageStreamAsync(
                        message, 
                        onChunk, 
                        _currentRequestCancellation.Token,
                        imageBase64,
                        ResetCurrentStreamingAttempt
                    );
                }, _currentRequestCancellation.Token);
                
                _streamUpdateTimer?.Stop();

                var canRetryWithManagedFallback =
                    !string.IsNullOrEmpty(error) &&
                    !string.Equals(error, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    !(_currentAI is HostedManagedAiService) &&
                    CanUseManagedExtensionFallbackForProvider(_settings.SelectedAI);

                if (canRetryWithManagedFallback)
                {
                    Log.WriteLine($"BYO runtime failed for {_settings.SelectedAI}. Retrying same request with managed extension fallback.");
                    ForceManagedExtensionForCurrentProvider(_settings.SelectedAI);
                    _creditMeteringService.TrackUsageSource(DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI), _settings.SelectedAI);

                    lock (_streamBuffer)
                    {
                        _streamBuffer.Clear();
                    }

                    if (_currentStreamingParagraph != null)
                    {
                        ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                        _currentStreamingParagraph = null;
                    }

                    StatusText.Text = "🔄 Switching to managed extension...";
                    StatusIndicator.Fill = Brushes.Yellow;

                    aiName = _currentAI?.GetProviderName() ?? _settings.SelectedAI;
                    _currentStreamingParagraph = new Paragraph
                    {
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 5, 0, 5)
                    };
                    var retryHeaderRun = new Run($"{aiName}:\n") { FontWeight = FontWeights.Bold };
                    _currentStreamingParagraph.Inlines.Add(retryHeaderRun);
                    ChatDocument.Blocks.Add(_currentStreamingParagraph);

                    _streamUpdateTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(50)
                    };
                    _streamUpdateTimer.Tick += StreamUpdateTimer_Tick;
                    _streamUpdateTimer.Start();

                    (response, error) = await Task.Run(async () =>
                    {
                        return await _conversationManager.SendMessageStreamAsync(
                            message,
                            onChunk,
                            _currentRequestCancellation.Token,
                            imageBase64,
                            ResetCurrentStreamingAttempt
                        );
                    }, _currentRequestCancellation.Token);

                    _streamUpdateTimer?.Stop();
                }
                
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (error == "Cancelled")
                {
                    Log.WriteLine("✗ Request was cancelled");
                    
                    if (_currentStreamingParagraph != null)
                    {
                        ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                        _currentStreamingParagraph = null;
                    }
                    
                    AddToChat("_[Request cancelled]_", true);
                    
                    StatusText.Text = "⚠️ Cancelled";
                    StatusIndicator.Fill = Brushes.Orange;
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    Log.WriteLine($"✗ AI Error: {error}");

                    var isDesktopAuthFailure =
                        error.IndexOf("Desktop session is no longer valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        error.IndexOf("Hosted desktop session not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        error.IndexOf("Refresh token is no longer valid", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isDesktopAuthFailure)
                    {
                        _authSessionRepository.Clear();
                        _creditMeteringService.AbandonActiveSession();
                        _interviewLockService.MarkLockReleased();
                        RefreshAccountSnapshot();
                        UpdateCreditIndicator();
                        UpdateSessionStatus();
                    }
                    
                    if (_currentStreamingParagraph != null)
                    {
                        ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                        _currentStreamingParagraph = null;
                    }
                    
                    AddToChat($"❌ **Error:** {error}", true);

                    if (isDesktopAuthFailure)
                    {
                        AddToChat("⚠️ **Desktop session expired**\n\nPlease sign in again to continue.", false);
                    }
                    else
                    {
                        PauseInterviewSessionForError("runtime_error_response");
                        AddToChat("⏸️ **Interview paused**\n\nPhantom paused the active interview after this error. The session timer and billing stay frozen until a later response succeeds.", false);
                    }
                    
                    StatusText.Text = "✗ Error occurred";
                    StatusIndicator.Fill = Brushes.Red;
                }
                else
                {
                    ResumeInterviewSessionAfterSuccess();
                    Log.WriteLine($"✓ Received response ({response.Length} chars) in {elapsed:F1}s");
                    
                    if (_currentStreamingParagraph != null)
                    {
                        string finalText;
                        lock (_streamBuffer)
                        {
                            finalText = _streamBuffer.ToString();
                        }
                        
                        while (_currentStreamingParagraph.Inlines.Count > 1)
                        {
                            _currentStreamingParagraph.Inlines.Remove(_currentStreamingParagraph.Inlines.LastInline);
                        }
                        _currentStreamingParagraph.Inlines.Add(new Run(finalText));
                    }
                    
                    await Task.Delay(100);
                    
                    if (_currentStreamingParagraph != null)
                    {
                        ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                        _currentStreamingParagraph = null;
                    }
                    
                    var fullMarkdown = $"**{aiName}:**\n\n{response}";
                    
                    int blockCountBefore = ChatDocument.Blocks.Count;
                    MarkdownHelper.AppendMarkdown(ChatDocument, fullMarkdown, false);
                    
                    if (ChatDocument.Blocks.Count > blockCountBefore)
                    {
                        var newBlock = ChatDocument.Blocks.Skip(blockCountBefore).FirstOrDefault();
                        if (newBlock != null)
                        {
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { newBlock.BringIntoView(); } catch { }
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    
                    var selectedPack = _contextPackService.GetSelectedPack();
                    if (_conversationManager.HasResume() && string.IsNullOrWhiteSpace(selectedPack.ResumeSummary))
                    {
                        selectedPack.ResumeSummary = _conversationManager.GetResumeSummary();
                        _contextPackService.SaveSelectedPack(selectedPack);
                        Log.WriteLine("✓ Resume summary cached to context pack");
                    }

                    if (_conversationManager.HasJobDescription() && string.IsNullOrWhiteSpace(selectedPack.JobDescriptionSummary))
                    {
                        selectedPack.JobDescriptionSummary = _conversationManager.GetJobDescriptionSummary();
                        _contextPackService.SaveSelectedPack(selectedPack);
                        Log.WriteLine("✓ Job description summary cached to context pack");
                    }
                    
                    StatusText.Text = $"✓ Response in {elapsed:F1}s | Two-cursor active";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, args) =>
                    {
                        StatusText.Text = "✓ Protected | Two-cursor system active";
                        timer.Stop();
                    };
                    timer.Start();
                    RecordInterviewActivity("response_completed");
                    
                }
                // ✅ NEW: Clear screenshot after successful send
                if (_attachedScreenshot != null)
                {
                    Log.WriteLine("✓ Clearing screenshot attachment after successful send");
                    ClearAttachedScreenshot();
                }

                UpdateTokenCounter();
                // NEW: Update debug info if in debug mode
                if (_settings.DebugModeEnabled && DebugModeDetails != null)
                {
                    DebugModeDetails.Text = ErrorSimulator.GetStatus();
                }
            }
            catch (OperationCanceledException)
            {
                Log.WriteLine("✗ Request cancelled (exception)");
                
                _streamUpdateTimer?.Stop();
                
                if (_currentStreamingParagraph != null)
                {
                    ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                    _currentStreamingParagraph = null;
                }
                
                StatusText.Text = "⚠️ Cancelled";
                StatusIndicator.Fill = Brushes.Orange;

                if (_attachedScreenshot != null)
                {
                    ClearAttachedScreenshot();
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Exception: {ex.Message}");
                
                _streamUpdateTimer?.Stop();
                
                if (_currentStreamingParagraph != null)
                {
                    ChatDocument.Blocks.Remove(_currentStreamingParagraph);
                    _currentStreamingParagraph = null;
                }
                
                AddToChat($"❌ **Exception:** {ex.Message}", true);
                PauseInterviewSessionForError("runtime_exception");
                AddToChat("⏸️ **Interview paused**\n\nPhantom paused the active interview after this exception. The session timer and billing stay frozen until a later response succeeds.", false);
                
                StatusText.Text = "✗ Exception occurred";
                StatusIndicator.Fill = Brushes.Red;

                if (_attachedScreenshot != null)
                {
                    ClearAttachedScreenshot();
                }

            }
            finally
            {
                _streamUpdateTimer?.Stop();
                _streamUpdateTimer = null;
                _currentStreamingParagraph = null;
                
                lock (_streamBuffer)
                {
                    _streamBuffer.Clear();
                }
                
                _isProcessingRequest = false;
                _currentRequestCancellation?.Dispose();
                _currentRequestCancellation = null;
                
                FocusInput();
            }
        }

        private void StreamUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentStreamingParagraph != null)
            {
                string currentText;
                lock (_streamBuffer)
                {
                    currentText = _streamBuffer.ToString();
                }
                
                if (currentText.Length > 0)
                {
                    while (_currentStreamingParagraph.Inlines.Count > 1)
                    {
                        _currentStreamingParagraph.Inlines.Remove(_currentStreamingParagraph.Inlines.LastInline);
                    }
                    
                    _currentStreamingParagraph.Inlines.Add(new Run(currentText));
                    
                    try
                    {
                        _currentStreamingParagraph.BringIntoView();
                    }
                    catch { }
                }
            }
        }

        private void AddToChat(string text, bool isResponse)
        {
            try
            {
                if (ChatDocument.Blocks.Count > 0)
                {
                    var firstBlock = ChatDocument.Blocks.FirstBlock;
                    if (firstBlock is Paragraph p && p.Inlines.FirstInline is Run r)
                    {
                        if (r.Text.Contains("Welcome to your invisible"))
                        {
                            MarkdownHelper.ClearDocument(ChatDocument);
                            Log.WriteLine("Cleared welcome message");
                        }
                    }
                }

                int blockCountBefore = ChatDocument.Blocks.Count;

                MarkdownHelper.AppendMarkdown(ChatDocument, text, !isResponse);

                if (ChatDocument.Blocks.Count > blockCountBefore)
                {
                    var newBlocks = ChatDocument.Blocks.Skip(blockCountBefore).FirstOrDefault();
                    
                    if (newBlocks != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            newBlocks.BringIntoView();
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error adding to chat: {ex.Message}");
                
                var paragraph = new Paragraph(new Run(text))
                {
                    Foreground = isResponse ? Brushes.White : Brushes.LightBlue,
                    Margin = new Thickness(0, 5, 0, 5)
                };
                ChatDocument.Blocks.Add(paragraph);
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    paragraph.BringIntoView();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void ActivateInterviewLock(InterviewSessionRecord session)
        {
            var lockResult = _interviewLockService.StartOrResumeLock(session);
            if (!lockResult.Succeeded)
            {
                Log.WriteLine($"Interview lock activation failed: {lockResult.Title} | {lockResult.Message}");
                return;
            }

            _interviewLockHeartbeatTimer?.Stop();
            _interviewLockHeartbeatTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(session.HeartbeatIntervalSeconds > 0 ? session.HeartbeatIntervalSeconds : 60)
            };
            _interviewLockHeartbeatTimer.Tick += InterviewLockHeartbeatTimer_Tick;
            _interviewLockHeartbeatTimer.Start();

            Log.WriteLine($"Interview lock active until {lockResult.LockExpiresAtUtc:O}");
            _telemetryService.Track("lock", "interview_lock_active", new Dictionary<string, string>
            {
                ["session_id"] = session.SessionId,
                ["expires_at"] = lockResult.LockExpiresAtUtc?.ToString("O") ?? string.Empty
            });
        }

        private void InterviewLockHeartbeatTimer_Tick(object? sender, EventArgs e)
        {
            var heartbeat = _interviewLockService.HeartbeatActiveLock();
            if (!heartbeat.Succeeded)
            {
                Log.WriteLine($"Interview lock heartbeat failed: {heartbeat.Title} | {heartbeat.Message}");
                _interviewLockHeartbeatTimer?.Stop();
                StatusText.Text = $"⚠️ {heartbeat.Title}";
                StatusIndicator.Fill = Brushes.Orange;
                _telemetryService.Track("lock", "interview_lock_heartbeat_failed", new Dictionary<string, string>
                {
                    ["title"] = heartbeat.Title,
                    ["message"] = heartbeat.Message
                });
                return;
            }

            Log.WriteLine($"Interview lock heartbeat refreshed until {heartbeat.LockExpiresAtUtc:O}");
            _telemetryService.Track("lock", "interview_lock_heartbeat", new Dictionary<string, string>
            {
                ["expires_at"] = heartbeat.LockExpiresAtUtc?.ToString("O") ?? string.Empty
            });
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Send button clicked");
            await SendMessage();
        }

        // ═══════════════════════════════════════════════════════════════
        // INPUT BOX KEY HANDLING
        // ═══════════════════════════════════════════════════════════════

        private async void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ✅ Enter alone = Send message
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true; // Prevent newline from being inserted
                Log.WriteLine("Enter key pressed - sending message");
                await SendMessage();
            }
            // ✅ Shift+Enter = New line (allow default TextBox behavior)
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Don't set e.Handled - let TextBox insert the newline
                Log.WriteLine("Shift+Enter pressed - new line added");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PASTE HANDLER - Preserve multi-line formatting
        // ═══════════════════════════════════════════════════════════════

        private void OnPaste(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                // Get text from clipboard
                if (Clipboard.ContainsText())
                {
                    string clipboardText = Clipboard.GetText(TextDataFormat.Text);
                    
                    // Normalize line endings to Windows format (\r\n)
                    clipboardText = clipboardText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    
                    // Clear placeholder if present
                    if (InputTextBox.Text == "Ask me anything...")
                    {
                        InputTextBox.Text = "";
                        InputTextBox.Foreground = Brushes.White;
                    }
                    
                    // Get current selection
                    int selectionStart = InputTextBox.SelectionStart;
                    int selectionLength = InputTextBox.SelectionLength;
                    
                    // Remove selected text if any
                    if (selectionLength > 0)
                    {
                        InputTextBox.Text = InputTextBox.Text.Remove(selectionStart, selectionLength);
                    }
                    
                    // Insert the clipboard text at caret position
                    InputTextBox.Text = InputTextBox.Text.Insert(selectionStart, clipboardText);
                    
                    // Move caret to end of pasted text
                    InputTextBox.SelectionStart = selectionStart + clipboardText.Length;
                    InputTextBox.SelectionLength = 0;
                    
                    // Scroll to caret
                    InputTextBox.ScrollToEnd();
                    
                    // Log the paste
                    int lineCount = clipboardText.Split(new[] { "\r\n" }, StringSplitOptions.None).Length;
                    Log.WriteLine($"Pasted text with {lineCount} line(s), {clipboardText.Length} characters");
                    
                    // Prevent default paste behavior
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error during paste: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CLEAR CHAT & NEW TOPIC
        // ═══════════════════════════════════════════════════════════════
        private void ClearChatButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("CLEAR CHAT BUTTON CLICKED");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            var result = InvisibleMessageBox.ShowYesNo(
                "Clear conversation history?\n\n" +
                "This will reset the AI's context and remove your resume and job description.",
                "Clear Chat"
            );
            
            if (result && _conversationManager != null)
            {
                // Clear conversation (this now properly re-adds system prompt)
                _conversationManager.ClearConversation();
                
                _contextPackService.ClearSelectedPack(clearResume: true, clearJobDescription: true);
                
                // Clear conversation cache
                SettingsManager.ClearConversationCache();
                
                // Clear resume and JD from conversation manager
                _conversationManager.UpdateResume("", "");
                _conversationManager.ClearJobDescription();
                
                // Clear UI
                MarkdownHelper.ClearDocument(ChatDocument);
                MarkdownHelper.AddWelcomeMessage(ChatDocument);
                
                // Update token counter
                UpdateTokenCounter();
                
                Log.WriteLine("✓ Everything cleared:");
                Log.WriteLine("  - Conversation history cleared");
                Log.WriteLine("  - Resume cleared");
                Log.WriteLine("  - Job description cleared");
                Log.WriteLine("  - Conversation cache cleared");
                Log.WriteLine("  - Model reset to preferred");
                Log.WriteLine($"  - Messages in conversation: {_conversationManager.GetMessageCount()}");
                Log.WriteLine("═══════════════════════════════════════════════════════");
                
                // Update status
                StatusText.Text = "✓ Everything cleared - Fresh start";
                StatusIndicator.Fill = Brushes.LightGreen;
                
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, args) =>
                {
                    StatusText.Text = "✓ Protected | Two-cursor system active";
                    timer.Stop();
                };
                timer.Start();
            }
            else
            {
                Log.WriteLine("✗ User cancelled clear chat");
            }
            
            FocusInput();
        }




        private void NewTopicButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("NEW TOPIC BUTTON CLICKED");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            var result = InvisibleMessageBox.ShowYesNo(
                "Start a new topic?\n\n" +
                "This will clear the conversation and job description, but keep your resume profile.",
                "New Topic"
            );
            
            if (result && _conversationManager != null)
            {
                // Start new topic (this now properly preserves resume and re-adds system prompt)
                _conversationManager.StartNewTopic();
                
                _contextPackService.ClearSelectedPack(clearResume: false, clearJobDescription: true);
                
                // Clear JD from conversation manager
                _conversationManager.ClearJobDescription();
                
                // Clear UI
                MarkdownHelper.ClearDocument(ChatDocument);
                MarkdownHelper.AddWelcomeMessage(ChatDocument);
                
                // Update token counter
                UpdateTokenCounter();
                
                Log.WriteLine("✓ New topic started:");
                Log.WriteLine("  - Conversation history cleared");
                Log.WriteLine("  - Job description cleared");
                Log.WriteLine("  - Resume profile preserved");
                Log.WriteLine("  - Model reset to preferred");
                Log.WriteLine($"  - Messages in conversation: {_conversationManager.GetMessageCount()}");
                Log.WriteLine("═══════════════════════════════════════════════════════");
                
                // Update status
                if (_conversationManager.HasResume())
                {
                    StatusText.Text = "✓ New topic - Resume kept, JD cleared";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, args) =>
                    {
                        StatusText.Text = "✓ Protected | Two-cursor system active";
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            else
            {
                Log.WriteLine("✗ User cancelled new topic");
            }
            
            FocusInput();
        }



        // ═══════════════════════════════════════════════════════════════
        // FOCUS MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        private void FocusInput()
        {
            // Only focus input when on chat page
            if (ChatPageContainer.Visibility == Visibility.Visible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    InputTextBox.Focus();
                    Keyboard.Focus(InputTextBox);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            
            if (element is Button || element is TextBox || element is ComboBox || element is CheckBox)
            {
                return;
            }
            
            Log.WriteLine("Window background clicked - focusing input");
            this.Activate();
            FocusInput();
        }

        private void ChatArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            if (element is Button || element is TextBox)
            {
                return;
            }
            
            this.Activate();
            FocusInput();
        }

        private void InputBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Activate();
        }

        private void InputTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Activate();
        }

        private void StatusBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            if (element is Button)
            {
                return;
            }
            
            this.Activate();
            FocusInput();
        }

        // ═══════════════════════════════════════════════════════════════
        // VOICE INPUT
        // ═══════════════════════════════════════════════════════════════

        private async void InitializeVoice()
        {
            Log.WriteLine("═══════════════════════════════════════════════");
            Log.WriteLine("InitializeVoice() called");
            Log.WriteLine($"  VoiceInputEnabled: {_settings.VoiceInputEnabled}");
            
            if (!_settings.VoiceInputEnabled) 
            {
                Log.WriteLine("Voice input disabled in settings");
                VoiceButton.IsEnabled = false;
                VoiceButton.Opacity = 0.5;
                VoiceStatusText.Text = "Disabled";
                return;
            }

            try
            {
                Log.WriteLine("Creating browser-based VoiceInputService...");
                _voiceService = new VoiceInputService();
                
                _voiceService.SpeechRecognized += OnSpeechRecognized;
                _voiceService.StatusChanged += OnVoiceStatusChanged;
                
                Log.WriteLine("Starting async initialization...");
                VoiceStatusText.Text = "Initializing...";
                VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0));
                
                var success = await _voiceService.InitializeAsync();
                
                if (success)
                {
                    Log.WriteLine("✓ Voice service initialized successfully");
                    VoiceButton.IsEnabled = true;
                    VoiceButton.Opacity = 1.0;
                    VoiceStatusText.Text = "Ready";
                    VoiceStatusText.Foreground = Brushes.LightGreen;
                }
                else
                {
                    Log.WriteLine("✗ Voice service initialization failed");
                    VoiceButton.IsEnabled = false;
                    VoiceButton.Opacity = 0.5;
                    VoiceStatusText.Text = "Failed";
                    VoiceStatusText.Foreground = Brushes.Red;
                    
                    InvisibleMessageBox.Show(
                        "Voice recognition failed to initialize.\n\n" +
                        "Check debug logs (🐛) for details.",
                        "Voice Init Failed"
                    );
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Voice init exception: {ex.Message}");
                
                VoiceButton.IsEnabled = false;
                VoiceButton.Opacity = 0.5;
                VoiceStatusText.Text = "Error";
                VoiceStatusText.Foreground = Brushes.Red;
                
                InvisibleMessageBox.Show($"Voice initialization error:\n\n{ex.Message}", "Error");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════");
        }

        private void OnSpeechRecognized(object? sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                Log.WriteLine("─────────────────────────────────────────────────────");
                Log.WriteLine($"Speech recognized: '{text}'");
                
                if (_isHidden) 
                {
                    Log.WriteLine("  Window was hidden, showing it now");
                    ToggleVisibility();
                }
                
                this.Activate();
                
                if (InputTextBox.Text == "Ask me anything..." || string.IsNullOrWhiteSpace(InputTextBox.Text))
                {
                    InputTextBox.Text = text;
                    Log.WriteLine("  Text placed in empty input box");
                }
                else
                {
                    InputTextBox.Text += " " + text;
                    Log.WriteLine("  Text appended to existing input");
                }
                
                FocusInput();
                
                if (_autoSendAfterVoice && _voiceCompletionTimer != null)
                {
                    Log.WriteLine("  Auto-send active - RESTARTING completion timer for new words");
                    
                    _voiceCompletionTimer.Stop();
                    _voiceCompletionTimer.Start();
                    
                    Log.WriteLine("  Timer restarted - waiting 1.5 more seconds");
                }
                else
                {
                    StatusText.Text = "✓ Speech captured - Press Enter to send";
                }
                
                StatusIndicator.Fill = Brushes.LightGreen;
                
                Log.WriteLine("✓ Text populated.");
                Log.WriteLine("─────────────────────────────────────────────────────");
            });
        }

        private void OnVoiceStatusChanged(object? sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                Log.WriteLine($"Voice status: {status}");
                VoiceStatusText.Text = status;
                
                if (status.Contains("Listening"))
                {
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0));
                }
                else if (status.Contains("Error"))
                {
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 100, 100));
                }
                else if (status == "Ready")
                {
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144));
                }
                else
                {
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                }
            });
        }

        private void VoiceButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("═══════════════════════════════════════════════");
            Log.WriteLine("VoiceButton_Click");
            
            if (_voiceService == null)
            {
                Log.WriteLine("  Voice service is null");
                InvisibleMessageBox.Show(
                    "Voice input not available.\n\nEnable it in Settings.", 
                    "Voice Input"
                );
                FocusInput();
                return;
            }

            if (!_voiceService.IsInitialized())
            {
                Log.WriteLine("  Voice not initialized yet");
                InvisibleMessageBox.Show(
                    "Voice recognition is initializing...\n\nPlease wait and try again.", 
                    "Please Wait"
                );
                FocusInput();
                return;
            }

            if (_voiceService.IsListening())
            {
                Log.WriteLine("  Currently listening - stopping");
                
                _autoSendAfterVoice = true;
                
                _voiceService.StopListening();
                VoiceButton.Content = "🎤";
                VoiceButton.Background = new SolidColorBrush(Color.FromArgb(80, 0, 170, 0));
                VoiceStatusText.Text = "Processing speech...";
                VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0));
                
                Log.WriteLine("  ✓ Stopped listening - waiting for ALL speech to complete");
                Log.WriteLine("  Auto-send enabled - starting completion timer");
                
                _voiceCompletionTimer?.Stop();
                
                _voiceCompletionTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                
                _voiceCompletionTimer.Tick += async (s, args) =>
                {
                    _voiceCompletionTimer?.Stop();
                    
                    Log.WriteLine("  Completion timer fired - checking if speech is complete...");
                    
                    if (!string.IsNullOrWhiteSpace(InputTextBox.Text) && 
                        InputTextBox.Text != "Ask me anything..." &&
                        _voiceService != null &&
                        !_voiceService.IsListening())
                    {
                        Log.WriteLine($"  ✓ Speech FULLY completed. Auto-sending: '{InputTextBox.Text}'");
                        
                        StatusText.Text = "✓ Speech captured - Sending automatically...";
                        StatusIndicator.Fill = Brushes.LightGreen;
                        VoiceStatusText.Text = "Sending...";
                        VoiceStatusText.Foreground = Brushes.LightGreen;
                        
                        await Task.Delay(500);
                        
                        await SendMessage();
                        
                        VoiceStatusText.Text = "Ready";
                        VoiceStatusText.Foreground = Brushes.LightGreen;
                    }
                    else
                    {
                        Log.WriteLine("  ⚠️ Auto-send cancelled - no text captured");
                        StatusText.Text = "⚠️ No speech detected - try again";
                        VoiceStatusText.Text = "Ready";
                    }
                    
                    _autoSendAfterVoice = false;
                };
                
                _voiceCompletionTimer.Start();
                Log.WriteLine("  Started 1.5-second completion timer");
            }
            else
            {
                Log.WriteLine("  Not listening - starting");
                
                if (InputTextBox.Text == "Ask me anything...")
                {
                    InputTextBox.Text = "";
                }
                
                _autoSendAfterVoice = false;
                
                _voiceService.StartListening();
                VoiceButton.Content = "⏹️";
                VoiceButton.Background = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0));
                VoiceStatusText.Text = "🎙️ Getting microphone ready";
                VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0));
                
                Log.WriteLine("  ✓ Started listening");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════");
        }

        // ═══════════════════════════════════════════════════════════════
        // DEBUG PANEL
        // ═══════════════════════════════════════════════════════════════

        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("─────────────────────────────────────────────────────");
            Log.WriteLine("Debug button clicked");
            
            if (DebugPanel.Visibility == Visibility.Visible)
            {
                DebugPanel.Visibility = Visibility.Collapsed;
                Log.WriteLine("✓ Debug panel hidden");
            }
            else
            {
                DebugPanel.Visibility = Visibility.Visible;
                Log.WriteLine("✓ Debug panel shown");
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DebugScrollViewer.ScrollToEnd();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            
            Log.WriteLine("─────────────────────────────────────────────────────");
        }

        private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Copy logs button clicked");
            
            try
            {
                var allLogs = _debugLogger.GetAllLogs();
                
                if (!string.IsNullOrEmpty(allLogs))
                {
                    Clipboard.SetText(allLogs);
                    StatusText.Text = "✓ Logs copied to clipboard!";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    
                    Log.WriteLine($"✓ Copied {allLogs.Length} characters to clipboard");
                    
                    var timer = new System.Windows.Threading.DispatcherTimer 
                    { 
                        Interval = TimeSpan.FromSeconds(3) 
                    };
                    timer.Tick += (s, args) =>
                    {
                        StatusText.Text = "✓ Protected | Two-cursor system active";
                        timer.Stop();
                    };
                    timer.Start();
                }
                else
                {
                    StatusText.Text = "No logs to copy";
                    Log.WriteLine("No logs available");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Failed to copy logs: {ex.Message}");
                StatusText.Text = "✗ Failed to copy logs";
            }
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Clear logs button clicked");

            var result = InvisibleMessageBox.ShowYesNo(
                "Clear all debug logs?",
                "Confirm Clear"
            );

            if (result)
            {
                _debugLogger.Clear();
                Log.WriteLine("✓ Debug logs cleared by user");
                StatusText.Text = "✓ Logs cleared";
            }
            else
            {
                Log.WriteLine("Clear logs cancelled by user");
            }
        }

        private void ViewContextButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("View context button clicked");
            
            if (_conversationManager == null)
            {
                InvisibleMessageBox.Show("No conversation active yet.", "Context Viewer");
                return;
            }

            try
            {
                var context = _conversationManager.GetOptimizedContextForDebug();
                
                var contextDisplay = new System.Text.StringBuilder();
                contextDisplay.AppendLine("═══════════════════════════════════════════════════════");
                contextDisplay.AppendLine("ACTUAL CONTEXT SENT TO AI");
                contextDisplay.AppendLine("═══════════════════════════════════════════════════════");
                contextDisplay.AppendLine();
                
                int messageNum = 1;
                foreach (var msg in context)
                {
                    contextDisplay.AppendLine($"[{messageNum}] Role: {msg.Role.ToUpper()}");
                    contextDisplay.AppendLine($"Tokens: ~{msg.EstimatedTokens}");
                    contextDisplay.AppendLine($"Content:");
                    contextDisplay.AppendLine(msg.Content);
                    contextDisplay.AppendLine();
                    contextDisplay.AppendLine("─────────────────────────────────────────────────────");
                    contextDisplay.AppendLine();
                    messageNum++;
                }
                
                var totalTokens = context.Sum(m => m.EstimatedTokens);
                contextDisplay.AppendLine($"Total Context Tokens: ~{totalTokens}");
                contextDisplay.AppendLine("═══════════════════════════════════════════════════════");
                
                Clipboard.SetText(contextDisplay.ToString());
                
                InvisibleMessageBox.Show(
                    $"Context copied to clipboard!\n\n" +
                    $"Messages: {context.Count}\n" +
                    $"Tokens: ~{totalTokens}\n\n" +
                    $"Check your clipboard to see the full context.",
                    "Context Viewer"
                );
                
                Log.WriteLine($"✓ Context exported: {context.Count} messages, ~{totalTokens} tokens");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Context view error: {ex.Message}");
                InvisibleMessageBox.Show($"Error viewing context: {ex.Message}", "Error");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SETTINGS (MULTI-PAGE NAVIGATION)
        // ═══════════════════════════════════════════════════════════════

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Settings button clicked - switching to settings page");
            RefreshAccountSnapshot();

            _settingsPage = new SettingsPage(_accountSnapshot);
            _settingsPage.SettingsClosed += OnSettingsClosed;
            SettingsPageHost.Content = _settingsPage;

            // Switch pages
            ChatPageContainer.Visibility = Visibility.Collapsed;
            SettingsPageContainer.Visibility = Visibility.Visible;
        }


        private void OnSettingsClosed(object? sender, bool saved)
        {
            Log.WriteLine($"Settings closed - saved: {saved}");

            if (saved)
            {
                var oldProvider = _currentAI?.GetProviderName() ?? "None";
                var oldModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "unknown";
                _forcedManagedExtensionProviderId = null;
                
                // Reload settings
                _settings = SettingsManager.Load();
                RefreshManagedCatalogCache();
                RefreshAccountSnapshot();
                ApplyAccountTierChrome();
                UpdateCreditIndicator();
                
                Log.WriteLine($"Model before settings reload: {oldModel}");
                
                // Reinitialize AI with new settings
                InitializeAI();

                if (_conversationManager != null)
                {
                    var selectedPack = _contextPackService.GetSelectedPack();
                    _conversationManager.UpdateResume(selectedPack.ResumeText, selectedPack.ResumeSummary);
                    Log.WriteLine("✓ Resume updated in conversation manager");
                    _conversationManager.UpdateJobDescription(selectedPack.JobDescriptionText, selectedPack.JobDescriptionSummary);
                    Log.WriteLine("✓ Job description updated in conversation manager");
                }

                _cursorManager?.Dispose();
                _cursorManager = new CursorManager(
                    this, 
                    CustomCursorCanvas, 
                    _settings.UseFakeCursor,
                    _settings.FakeCursorSize
                );
                Log.WriteLine("✓ Cursor manager reinitialized with new settings");

                var newProvider = _currentAI?.GetProviderName() ?? "None";
                var newModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "unknown";
                
                // Check if model actually changed
                bool modelChanged = oldModel != newModel;
                bool providerChanged = oldProvider != newProvider;
                
                if (_settings.VoiceInputEnabled && _voiceService == null)
                {
                    Log.WriteLine("Voice was disabled, now enabled - initializing");
                    InitializeVoice();
                }
                else if (!_settings.VoiceInputEnabled && _voiceService != null)
                {
                    Log.WriteLine("Voice was enabled, now disabled - disposing");
                    _voiceService.Dispose();
                    _voiceService = null;
                    VoiceButton.IsEnabled = false;
                    VoiceButton.Opacity = 0.5;
                    VoiceStatusText.Text = "Disabled";
                }

                // ✅ Show notification for changes
                if (providerChanged || modelChanged)
                {
                    InvisibleMessageBox.Show(
                        $"✓ Settings Applied\n\n" +
                        $"Provider: {newProvider}\n" +
                        $"Model: {GetModelDisplayName(_settings.SelectedAI, newModel)}\n\n" +
                        "Your conversation history has been preserved!",
                        "Settings Saved"
                    );
                }
                else
                {
                    InvisibleMessageBox.Show("Settings saved and reloaded!", "Success");
                }
                
                // ✅ ALWAYS update all indicators (even if no change detected)
                UpdateAPIKeyIndicator();
                UpdateScreenshotButtonVisibility();
                UpdateProviderAndModelDisplay();  // ✅ Critical for sync!
                StartSessionInactivityTimer();
                
                Log.WriteLine("✓ Settings reloaded successfully");
                Log.WriteLine($"  Final model: {newModel}");
            }
            else
            {
                Log.WriteLine("Settings cancelled by user");
                
                // Even if cancelled, refresh the settings page to show current values
                _settingsPage?.RefreshSettings();
            }

            // Switch back to chat page
            SettingsPageContainer.Visibility = Visibility.Collapsed;
            ChatPageContainer.Visibility = Visibility.Visible;

            this.Activate();
            FocusInput();
        }




        // ═══════════════════════════════════════════════════════════════
        // WINDOW CONTROLS
        // ═══════════════════════════════════════════════════════════════

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Minimize button clicked");
            ToggleVisibility();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Close button clicked");

            if (InvisibleMessageBox.ShowYesNo(
                "Are you sure you want to exit?\n\nAny unsaved chat history will be lost.",
                "Confirm Exit"))
            {
                Log.WriteLine("User confirmed exit");

                PerformFullCleanup();

                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                Log.WriteLine("User cancelled exit");
                FocusInput();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Log.WriteLine("Window closing event triggered");
            PerformFullCleanup();
            base.OnClosing(e);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            if (element is Button)
            {
                return;
            }
            
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try 
                { 
                    Log.WriteLine("Dragging window...");
                    
                    _isDraggingWindow = true;
                    _cursorManager?.SuspendCursorChanges();
                    
                    this.DragMove();
                    
                    _isDraggingWindow = false;
                    _cursorManager?.ResumeCursorChanges();
                    
                    FocusInput();
                }
                catch (Exception ex) 
                { 
                    Log.WriteLine($"DragMove error: {ex.Message}");
                    _isDraggingWindow = false;
                    _cursorManager?.ResumeCursorChanges();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // KEYBOARD HOOKS - GLOBAL HOTKEYS
        // ═══════════════════════════════════════════════════════════════

        private IntPtr SetHook(NativeMethods.LowLevelKeyboardProc? proc)
        {
            if (proc == null) return IntPtr.Zero;
            
            using (Process curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                if (curModule == null) return IntPtr.Zero;
                
                return NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    proc,
                    NativeMethods.GetModuleHandle(curModule.ModuleName),
                    0
                );
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Ctrl + Alt + ` - Toggle visibility
                if (vkCode == NativeMethods.VK_OEM_3)
                {
                    if (NativeMethods.IsKeyPressed(NativeMethods.VK_CONTROL) && 
                        NativeMethods.IsKeyPressed(NativeMethods.VK_MENU))
                    {
                        Log.WriteLine("Hotkey: Ctrl+Alt+` pressed");
                        Dispatcher.Invoke(() => ToggleVisibility());
                        return (IntPtr)1;
                    }
                }

                // F13 - Toggle visibility
                if (vkCode == NativeMethods.VK_F13)
                {
                    Log.WriteLine("Hotkey: F13 pressed");
                    Dispatcher.Invoke(() => ToggleVisibility());
                    return (IntPtr)1;
                }

                // Ctrl + Alt + - - Quit
                if (vkCode == NativeMethods.VK_OEM_MINUS)
                {
                    if (NativeMethods.IsKeyPressed(NativeMethods.VK_CONTROL) && 
                        NativeMethods.IsKeyPressed(NativeMethods.VK_MENU))
                    {
                        Log.WriteLine("Hotkey: Ctrl+Alt+- pressed");
                        Dispatcher.Invoke(() => 
                        {
                            if (InvisibleMessageBox.ShowYesNo("Exit application?", "Confirm"))
                                Close();
                        });
                        return (IntPtr)1;
                    }
                }

                // F14 - Quit
                if (vkCode == NativeMethods.VK_F14)
                {
                    Log.WriteLine("Hotkey: F14 pressed");
                    Dispatcher.Invoke(() => 
                    {
                        if (InvisibleMessageBox.ShowYesNo("Exit application?", "Confirm"))
                            Close();
                    });
                    return (IntPtr)1;
                }

                // F15 - Settings
                if (vkCode == NativeMethods.VK_F15)
                {
                    Log.WriteLine("Hotkey: F15 pressed");
                    Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
                    return (IntPtr)1;
                }

                // Ctrl + Alt + D - Toggle Debug Panel
                if (vkCode == (int)Key.D)
                {
                    if (NativeMethods.IsKeyPressed(NativeMethods.VK_CONTROL) && 
                        NativeMethods.IsKeyPressed(NativeMethods.VK_MENU))
                    {
                        Log.WriteLine("Hotkey: Ctrl+Alt+D pressed");
                        Dispatcher.Invoke(() => DebugButton_Click(this, new RoutedEventArgs()));
                        return (IntPtr)1;
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void ToggleVisibility()
        {
            if (_isHidden)
            {
                Log.WriteLine("Showing window...");
                this.Opacity = 1.0;
                _isHidden = false;
                
                this.Activate();
                this.Topmost = true;
                
                FocusInput();
                
                Log.WriteLine("✓ Window shown and focused");
            }
            else
            {
                Log.WriteLine("Hiding window...");
                this.Opacity = 0.0;
                _isHidden = true;
                
                _cursorManager?.DeactivateCustomCursor();
                
                Log.WriteLine("✓ Window hidden");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // AUTO-RESTART WITH CONVERSATION PRESERVATION
        // ═══════════════════════════════════════════════════════════════

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("RESTART BUTTON CLICKED");
            Log.WriteLine("═══════════════════════════════════════════════════════");

            try
            {
                // Save current conversation
                if (_conversationManager != null)
                {
                    var messages = _conversationManager.ExportConversation();
                    
                    if (messages.Count > 0)
                    {
                        Log.WriteLine($"Saving {messages.Count} messages before restart...");
                        SettingsManager.SaveConversationCache(messages);
                        
                        StatusText.Text = $"✓ Saved {messages.Count} messages - Restarting...";
                        StatusIndicator.Fill = Brushes.Yellow;
                        
                        // ✅ Log cache location for debugging
                        Log.WriteLine($"✓ Conversation saved to: {SettingsManager.GetConversationCachePath()}");
                    }
                    else
                    {
                        Log.WriteLine("No conversation to save");
                        SettingsManager.ClearConversationCache();
                    }
                }
                else
                {
                    Log.WriteLine("No conversation manager - clearing cache");
                    SettingsManager.ClearConversationCache();
                }

                Log.WriteLine("Note: Model preference will reset to default on restart");

                // Show brief confirmation
                InvisibleMessageBox.Show(
                    "Restarting application...\n\n" +
                    "Your conversation will be restored automatically.\n" +
                    "Model preference will reset to default.",
                    "Auto-Restart"
                );

                // Get current executable path
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.WriteLine("✗ Failed to get executable path");
                    InvisibleMessageBox.Show("Failed to restart: Cannot find executable path", "Error");
                    return;
                }

                Log.WriteLine($"Executable path: {exePath}");
                Log.WriteLine("Starting new instance...");

                // ✅ CRITICAL: Set flag BEFORE closing (so cleanup knows not to delete cache)
                _isRestarting = true;

                // Start new instance
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--restart-main-window",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                });

                Log.WriteLine("✓ New instance started");
                Log.WriteLine("✓ Conversation saved for restoration");
                Log.WriteLine($"✓ Cache file preserved at: {SettingsManager.GetConversationCachePath()}");
                Log.WriteLine("✓ Model preference will reset to preferred");
                Log.WriteLine("Closing current instance...");
                Log.WriteLine("═══════════════════════════════════════════════════════");

                // Close current instance (PerformFullCleanup will run, but won't delete cache)
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Restart failed: {ex.Message}");
                Log.WriteLine($"Stack trace: {ex.StackTrace}");
                Log.WriteLine("═══════════════════════════════════════════════════════");
                
                InvisibleMessageBox.Show(
                    $"Failed to restart application:\n\n{ex.Message}\n\n" +
                    "Check debug logs (🐛) for details.",
                    "Restart Failed"
                );
                
                StatusText.Text = "✗ Restart failed";
                StatusIndicator.Fill = Brushes.Red;
                
                // ✅ Reset flag on error
                _isRestarting = false;
            }
        }


        // ═══════════════════════════════════════════════════════════════
        // SCREENSHOT FEATURE (UPDATED)
        // ═══════════════════════════════════════════════════════════════

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Screenshot button clicked");
            
            // If screenshot already attached, show options menu
            if (_attachedScreenshot != null)
            {
                ShowScreenshotOptions();
            }
            else
            {
                CaptureScreenshot();
            }
        }

        private void ShowScreenshotOptions()
        {
            var menuWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Cursor = Cursors.Arrow
            };

            // Apply screen capture protection
            menuWindow.Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(menuWindow).Handle;
                WindowProtection.ApplyProtection(hwnd);
                Log.WriteLine("✓ Screenshot menu protected from screen capture");
            };

            // Create menu content
            var menuBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 0, 170, 255)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5)
            };

            var menuStack = new StackPanel();
            var menuScrollViewer = new ScrollViewer
            {
                Content = menuStack,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = true
            };

            // ✅ FIX #1: Declare handler variable before use
            EventHandler? deactivateHandler = null;

            // Preview button
            var previewButton = new Button
            {
                Content = "👁️ Preview Screenshot",
                Foreground = Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Padding = new Thickness(15, 8, 15, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Arrow
            };
            previewButton.Click += (s, e) =>
            {
                if (deactivateHandler != null)
                    menuWindow.Deactivated -= deactivateHandler;
                menuWindow.Close();
                PreviewScreenshot();
            };
            menuStack.Children.Add(previewButton);

            // Replace button
            var replaceButton = new Button
            {
                Content = "🔄 Replace Screenshot",
                Foreground = Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Padding = new Thickness(15, 8, 15, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Arrow
            };
            replaceButton.Click += (s, e) =>
            {
                if (deactivateHandler != null)
                    menuWindow.Deactivated -= deactivateHandler;
                menuWindow.Close();
                CaptureScreenshot();
            };
            menuStack.Children.Add(replaceButton);

            // Separator
            var separator = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Margin = new Thickness(5)
            };
            menuStack.Children.Add(separator);

            // Remove button
            var removeButton = new Button
            {
                Content = "🗑️ Remove Screenshot",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Padding = new Thickness(15, 8, 15, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Arrow
            };
            removeButton.Click += (s, e) =>
            {
                if (deactivateHandler != null)
                    menuWindow.Deactivated -= deactivateHandler;
                menuWindow.Close();
                ClearAttachedScreenshot();
            };
            menuStack.Children.Add(removeButton);

            menuBorder.Child = menuScrollViewer;
            menuWindow.Content = menuBorder;

            // Position correctly relative to main window
            var mainWindowPosition = this.PointToScreen(new System.Windows.Point(0, 0));
            var buttonRelativePosition = ScreenshotButton.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0));
            
            menuWindow.Left = mainWindowPosition.X + buttonRelativePosition.X;
            menuWindow.Top = mainWindowPosition.Y + buttonRelativePosition.Y - 140;

            // Safe close on deactivate
            deactivateHandler = (s, e) =>
            {
                try
                {
                    if (deactivateHandler != null)
                        menuWindow.Deactivated -= deactivateHandler;
                    menuWindow.Close();
                }
                catch (InvalidOperationException)
                {
                    // Window already closing, ignore
                }
            };
            menuWindow.Deactivated += deactivateHandler;

            // NO cursor changes - keep arrow
            foreach (var child in menuStack.Children)
            {
                if (child is Button btn)
                {
                    btn.MouseEnter += (s, e) =>
                    {
                        btn.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                    };
                    btn.MouseLeave += (s, e) =>
                    {
                        btn.Background = System.Windows.Media.Brushes.Transparent;
                    };
                }
            }

            menuWindow.Show();
            menuWindow.Activate();
        }



        private void PreviewScreenshot()
        {
            if (_attachedScreenshot == null)
            {
                Log.WriteLine("No screenshot to preview");
                return;
            }

            Log.WriteLine("Opening screenshot preview");

            // ✅ Create preview window with same protection as main window
            var previewWindow = new Window
            {
                Title = "Screenshot Preview",
                Width = 800,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                WindowStyle = WindowStyle.None,  // ✅ No title bar
                AllowsTransparency = true,       // ✅ Allow transparency
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Topmost = true,
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 0, 170, 255)),
                BorderThickness = new Thickness(2)
            };

            // ✅ Apply screen capture protection BEFORE window is shown
            previewWindow.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(previewWindow).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    WindowProtection.ApplyProtection(hwnd);
                    Log.WriteLine("✓ Preview window protected from screen capture");
                    
                    // Verify protection
                    uint affinity;
                    if (NativeMethods.GetWindowDisplayAffinity(hwnd, out affinity))
                    {
                        Log.WriteLine($"  Preview window affinity: 0x{affinity:X}");
                    }
                }
            };

            var mainGrid = new Grid();

            // Image display
            var image = new Image
            {
                Source = _attachedScreenshot,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(10)
            };
            mainGrid.Children.Add(image);

            // Top bar with title and close button
            var topBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
                Height = 40,
                VerticalAlignment = VerticalAlignment.Top
            };
            
            var topBarGrid = new Grid();
            topBar.Child = topBarGrid;

            // Title
            var titleText = new TextBlock
            {
                Text = "📸 Screenshot Preview",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            topBarGrid.Children.Add(titleText);

            // Close button
            var closeButton = new Button
            {
                Content = "✕",
                Width = 40,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromArgb(0, 255, 68, 68)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Arrow  // ✅ Keep arrow cursor
            };
            
            closeButton.Click += (s, e) => previewWindow.Close();
            
            // Hover effect
            closeButton.MouseEnter += (s, e) =>
            {
                closeButton.Background = new SolidColorBrush(Color.FromArgb(200, 255, 68, 68));
            };
            closeButton.MouseLeave += (s, e) =>
            {
                closeButton.Background = new SolidColorBrush(Color.FromArgb(0, 255, 68, 68));
            };
            
            topBarGrid.Children.Add(closeButton);
            mainGrid.Children.Add(topBar);

            // ✅ Make top bar draggable
            topBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try
                    {
                        previewWindow.DragMove();
                    }
                    catch { }
                }
            };

            // ✅ Add resize grip (optional)
            var resizeGrip = new System.Windows.Controls.Primitives.ResizeGrip
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 5, 5),
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
            };
            mainGrid.Children.Add(resizeGrip);

            previewWindow.Content = mainGrid;

            // ✅ Add keyboard shortcuts
            previewWindow.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter)
                {
                    previewWindow.Close();
                }
            };

            previewWindow.ShowDialog();
        }


        private void CaptureScreenshot()
        {
            try
            {
                // Hide main window during capture
                this.Hide();
                System.Threading.Thread.Sleep(200); // Let window hide
                
                var screenshot = ScreenshotCapture.CaptureScreenshot();
                
                // Show main window again
                this.Show();
                
                if (screenshot != null)
                {
                    _attachedScreenshot = screenshot;
                    Log.WriteLine("✓ Screenshot attached");
                    
                    // Show visual feedback
                    ScreenshotButtonText.Text = "📸✓";
                    ScreenshotButton.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(80, 0, 170, 255)
                    );
                    
                    StatusText.Text = "✓ Screenshot attached - Click 📸 for options";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, args) =>
                    {
                        StatusText.Text = "✓ Protected | Two-cursor system active";
                        timer.Stop();
                    };
                    timer.Start();
                }
                else
                {
                    Log.WriteLine("Screenshot capture cancelled");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error capturing screenshot: {ex.Message}");
                this.Show();
            }
        }

        private void ClearAttachedScreenshot()
        {
            if (_attachedScreenshot != null)
            {
                Log.WriteLine("Clearing attached screenshot");
            }
            
            _attachedScreenshot = null;
            ScreenshotButtonText.Text = "📸";
            ScreenshotButton.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(80, 0, 170, 255)
            );
            
            StatusText.Text = "Screenshot removed";
            StatusIndicator.Fill = Brushes.Orange;
            
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                StatusText.Text = "✓ Protected | Two-cursor system active";
                StatusIndicator.Fill = Brushes.LightGreen;
                timer.Stop();
            };
            timer.Start();
        }


        // ═══════════════════════════════════════════════════════════════
        // CHECK IF CURRENT MODEL SUPPORTS VISION
        // ═══════════════════════════════════════════════════════════════

        private bool CurrentModelSupportsVision()
        {
            if (_settings == null || _rotationManager == null) 
            {
                Log.WriteLine("Settings or RotationManager not initialized");
                return false;
            }

            var provider = _settings.SelectedAI;
            var currentModel = _rotationManager.GetCurrentModel(provider);
            
            Log.WriteLine($"Checking vision support for: {provider} - {currentModel}");

            var model = ProviderModelCatalogCache.GetModel(_settings, provider, currentModel);
            bool supportsVision = model?.SupportsVision ?? AIModelRegistry.SupportsVision(currentModel);
            
            Log.WriteLine($"  Result: {(supportsVision ? "✓ Supports vision" : "✗ No vision support")}");
            
            return supportsVision;
        }


        // ═══════════════════════════════════════════════════════════════
        // IMAGE CONVERSION HELPER
        // ═══════════════════════════════════════════════════════════════

        private string? BitmapImageToBase64(BitmapImage? bitmapImage)
        {
            if (bitmapImage == null) return null;

            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                
                using (var memoryStream = new MemoryStream())
                {
                    encoder.Save(memoryStream);
                    byte[] imageBytes = memoryStream.ToArray();
                    string base64 = Convert.ToBase64String(imageBytes);
                    
                    Log.WriteLine($"✓ Converted screenshot to base64 ({base64.Length} chars, {imageBytes.Length} bytes)");
                    return base64;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Error converting screenshot to base64: {ex.Message}");
                return null;
            }
        }

        private void UpdateScreenshotButtonVisibility()
        {
            if (ScreenshotButton == null)
            {
                Log.WriteLine("ScreenshotButton not initialized yet");
                return;
            }

            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("UPDATING SCREENSHOT BUTTON VISIBILITY");
            
            if (CurrentModelSupportsVision())
            {
                ScreenshotButton.Visibility = Visibility.Visible;
                Log.WriteLine("✓ Screenshot button VISIBLE (model supports vision)");
            }
            else
            {
                ScreenshotButton.Visibility = Visibility.Collapsed;
                ClearAttachedScreenshot();
                Log.WriteLine("✗ Screenshot button HIDDEN (model doesn't support vision)");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        // ═══════════════════════════════════════════════════════════════
        // PROVIDER & MODEL SELECTION FROM TITLE BAR
        // ═══════════════════════════════════════════════════════════════

        private void ProviderSelector_Click(object sender, MouseButtonEventArgs e)
        {
            Log.WriteLine("Provider selector clicked");
            ShowProviderMenu();
        }

        private void ModelSelector_Click(object sender, MouseButtonEventArgs e)
        {
            Log.WriteLine("Model selector clicked");
            ShowModelMenu();
        }

        private void ShowProviderMenu()
        {
            // ✅ Close any existing dropdown menu
            CloseCurrentDropdownMenu();
            
            var menuWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Cursor = Cursors.Arrow,
                Owner = this  // ✅ SET OWNER - This fixes Z-order!
            };

            // ✅ Track this menu
            _currentDropdownMenu = menuWindow;

            // ✅ Flag to prevent premature closing
            bool isInitializing = true;

            menuWindow.Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(menuWindow).Handle;
                WindowProtection.ApplyProtection(hwnd);
                Log.WriteLine("✓ Provider menu protected from screen capture");
            };

            var menuBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 215, 0)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5),
                MinWidth = 150
            };

            var menuStack = new StackPanel();
            var menuScrollViewer = new ScrollViewer
            {
                Content = menuStack,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = true
            };

            var providers = GetAvailableProvidersForCurrentTier();
            
            foreach (var provider in providers)
            {
                var button = new Button
                {
                    Content = provider == _settings.SelectedAI ? $"✓ {provider}" : $"   {provider}",
                    Foreground = provider == _settings.SelectedAI ? 
                        new SolidColorBrush(Color.FromRgb(255, 215, 0)) : Brushes.White,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 13,
                    FontWeight = provider == _settings.SelectedAI ? FontWeights.Bold : FontWeights.Normal,
                    Padding = new Thickness(15, 8, 15, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Cursor = Cursors.Arrow,
                    Tag = provider
                };
                
                button.Click += (s, e) =>
                {
                    try
                    {
                        menuWindow.Close();
                        _currentDropdownMenu = null;  // ✅ Clear reference
                    }
                    catch { }
                    
                    var selectedProvider = (s as Button)?.Tag as string;
                    if (selectedProvider != null && selectedProvider != _settings.SelectedAI)
                    {
                        ChangeProvider(selectedProvider);
                    }
                };
                
                button.MouseEnter += (s, e) =>
                {
                    button.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                };
                button.MouseLeave += (s, e) =>
                {
                    button.Background = System.Windows.Media.Brushes.Transparent;
                };
                
                menuStack.Children.Add(button);
            }

            menuBorder.Child = menuScrollViewer;
            menuWindow.Content = menuBorder;

            // ✅ POSITION RELATIVE TO SCREEN (not window)
            var mainWindowPosition = this.PointToScreen(new System.Windows.Point(0, 0));
            var buttonRelativePosition = ProviderSelectorBorder.TransformToAncestor(this)
                .Transform(new System.Windows.Point(0, 0));
            
            menuWindow.Left = mainWindowPosition.X + buttonRelativePosition.X;
            menuWindow.Top = mainWindowPosition.Y + buttonRelativePosition.Y + 35;

            // ✅ SHOW WINDOW FIRST
            menuWindow.Show();
            menuWindow.Activate();

            // ✅ DELAY ATTACHING DEACTIVATE HANDLER
            var timer = new System.Windows.Threading.DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(100) 
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                isInitializing = false;
                
                // Now attach the deactivate handler
                menuWindow.Deactivated += (sender, args) =>
                {
                    if (!isInitializing)
                    {
                        try
                        {
                            menuWindow.Close();
                            _currentDropdownMenu = null;  // ✅ Clear reference
                        }
                        catch { }
                    }
                };
            };
            timer.Start();
        }



        private void ShowModelMenu()
        {
            // ✅ Close any existing dropdown menu
            CloseCurrentDropdownMenu();
            
            var menuWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Cursor = Cursors.Arrow,
                Owner = this  // ✅ SET OWNER - This fixes Z-order!
            };

            // ✅ Track this menu
            _currentDropdownMenu = menuWindow;

            // ✅ Flag to prevent premature closing
            bool isInitializing = true;

            menuWindow.Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(menuWindow).Handle;
                WindowProtection.ApplyProtection(hwnd);
                Log.WriteLine("✓ Model menu protected from screen capture");
            };

            var menuBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 0, 170, 255)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5),
                MinWidth = 200
            };

            var menuStack = new StackPanel();
            var menuScrollViewer = new ScrollViewer
            {
                Content = menuStack,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = true
            };

            // ✅ USE REGISTRY - Get models for current provider
            string[] models = GetAvailableModelsForSelectedProvider();
            string currentModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "";
            
            foreach (var model in models)
            {
                // ✅ USE REGISTRY - Get display name
            var displayName = GetModelDisplayName(_settings.SelectedAI, model);
                
                var button = new Button
                {
                    Content = model == currentModel ? $"✓ {displayName}" : $"   {displayName}",
                    Foreground = model == currentModel ? 
                        new SolidColorBrush(Color.FromRgb(0, 170, 255)) : Brushes.White,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 12,
                    FontWeight = model == currentModel ? FontWeights.Bold : FontWeights.Normal,
                    Padding = new Thickness(15, 8, 15, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Cursor = Cursors.Arrow,
                    Tag = model
                };
                
                button.Click += (s, e) =>
                {
                    try
                    {
                        menuWindow.Close();
                        _currentDropdownMenu = null;  // ✅ Clear reference
                    }
                    catch { }
                    
                    var selectedModel = (s as Button)?.Tag as string;
                    if (selectedModel != null && selectedModel != currentModel)
                    {
                        ChangeModel(selectedModel);
                    }
                };
                
                button.MouseEnter += (s, e) =>
                {
                    button.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                };
                button.MouseLeave += (s, e) =>
                {
                    button.Background = System.Windows.Media.Brushes.Transparent;
                };
                
                menuStack.Children.Add(button);
            }

            menuBorder.Child = menuScrollViewer;
            menuWindow.Content = menuBorder;

            // ✅ POSITION RELATIVE TO SCREEN (not window)
            var mainWindowPosition = this.PointToScreen(new System.Windows.Point(0, 0));
            var buttonRelativePosition = ModelSelectorBorder.TransformToAncestor(this)
                .Transform(new System.Windows.Point(0, 0));
            
            menuWindow.Left = mainWindowPosition.X + buttonRelativePosition.X;
            menuWindow.Top = mainWindowPosition.Y + buttonRelativePosition.Y + 35;

            // ✅ SHOW WINDOW FIRST
            menuWindow.Show();
            menuWindow.Activate();

            // ✅ DELAY ATTACHING DEACTIVATE HANDLER
            var timer = new System.Windows.Threading.DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(100) 
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                isInitializing = false;
                
                // Now attach the deactivate handler
                menuWindow.Deactivated += (sender, args) =>
                {
                    if (!isInitializing)
                    {
                        try
                        {
                            menuWindow.Close();
                            _currentDropdownMenu = null;  // ✅ Clear reference
                        }
                        catch { }
                    }
                };
            };
            timer.Start();
        }

        /// <summary>
        /// Close any currently open dropdown menu
        /// </summary>
        private void CloseCurrentDropdownMenu()
        {
            if (_currentDropdownMenu != null)
            {
                try
                {
                    _currentDropdownMenu.Close();
                }
                catch { }
                finally
                {
                    _currentDropdownMenu = null;
                }
            }
        }


        private void ChangeProvider(string newProvider)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine($"CHANGING PROVIDER FROM TITLE BAR: {_settings.SelectedAI} → {newProvider}");
            
            _settings.SelectedAI = newProvider;
            
            // ✅ SAVE SETTINGS IMMEDIATELY
            SettingsManager.Save(_settings);
            Log.WriteLine($"✓ Settings saved with new provider: {newProvider}");
            
            // Reinitialize AI with new provider
            InitializeAI();
            
            // ✅ Update UI
            UpdateProviderAndModelDisplay();
            UpdateAPIKeyIndicator();
            UpdateScreenshotButtonVisibility();
            
            // Update settings page if open
            if (SettingsPageContainer.Visibility == Visibility.Visible && _settingsPage != null)
            {
                _settingsPage.RefreshSettings();
            }
            
            StatusText.Text = $"✓ Switched to {newProvider}";
            StatusIndicator.Fill = Brushes.LightGreen;
            
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, args) =>
            {
                StatusText.Text = "✓ Protected | Two-cursor system active";
                timer.Stop();
            };
            timer.Start();
            
            Log.WriteLine($"✓ Provider changed to {newProvider}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }


        private void ChangeModel(string newModel)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine($"CHANGING MODEL FROM TITLE BAR: {newModel}");
            
            // ✅ Set model in settings
            AIModelRegistry.SetModelForProvider(_settings, _settings.SelectedAI, newModel);
            
            // ✅ SAVE SETTINGS IMMEDIATELY
            SettingsManager.Save(_settings);
            Log.WriteLine($"✓ Settings saved with new model: {newModel}");
            
            // ✅ Force rotation manager to use new model
            if (_rotationManager != null)
            {
                _rotationManager.SetCurrentModel(_settings.SelectedAI, newModel);
                Log.WriteLine($"✓ Rotation manager updated to model: {newModel}");
            }

            InitializeAI();
            
            // ✅ Update UI - This will refresh the display
            UpdateProviderAndModelDisplay();
            UpdateScreenshotButtonVisibility();
            
            // Update settings page if open
            if (SettingsPageContainer.Visibility == Visibility.Visible && _settingsPage != null)
            {
                _settingsPage.RefreshSettings();
            }
            
            StatusText.Text = $"✓ Switched to {GetModelDisplayName(_settings.SelectedAI, newModel)}";
            StatusIndicator.Fill = Brushes.LightGreen;
            
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, args) =>
            {
                StatusText.Text = "✓ Protected | Two-cursor system active";
                timer.Stop();
            };
            timer.Start();
            
            Log.WriteLine($"✓ Model changed to {newModel}");
            DebugCurrentModel();
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        private void ResetCurrentStreamingAttempt()
        {
            Dispatcher.Invoke(() =>
            {
                lock (_streamBuffer)
                {
                    _streamBuffer.Clear();
                }

                if (_currentStreamingParagraph == null)
                    return;

                while (_currentStreamingParagraph.Inlines.Count > 1)
                {
                    _currentStreamingParagraph.Inlines.Remove(_currentStreamingParagraph.Inlines.LastInline);
                }
            });
        }



        private void UpdateProviderAndModelDisplay()
        {
            if (AIProviderText == null || ModelText == null) return;
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("UPDATING PROVIDER AND MODEL DISPLAY");

            // Update provider display
            var providerName = _currentAI?.GetProviderName() ?? _settings.SelectedAI;
            AIProviderText.Text = providerName;
            Log.WriteLine($"  Provider display: {providerName}");
            
            // Update model display
            var currentModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "";
            Log.WriteLine($"  Current model ID: {currentModel}");
            
            // ✅ USE REGISTRY - Get display name
            var displayModel = GetModelDisplayName(_settings.SelectedAI, currentModel);
            Log.WriteLine($"  Display name: {displayModel}");
            
            ModelText.Text = displayModel;
            
            Log.WriteLine($"✓ Title bar updated: {providerName} | {displayModel}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        // ✅ TEMPORARY DEBUG METHOD - Add this to MainWindow class
        private void DebugCurrentModel()
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("DEBUG: CURRENT MODEL STATE");
            Log.WriteLine($"  Provider: {_settings.SelectedAI}");
            
            // ✅ FIX: Get the correct model for current provider
            string currentModel = "";
            switch (_settings.SelectedAI)
            {
                case "ChatGPT":
                    currentModel = _settings.ChatGPTModel;
                    Log.WriteLine($"  Settings.ChatGPTModel: {currentModel}");
                    Log.WriteLine($"  Settings.ChatGPTModels list: {string.Join(", ", _settings.ChatGPTModels)}");
                    break;
                case "Claude":
                    currentModel = _settings.ClaudeModel;
                    Log.WriteLine($"  Settings.ClaudeModel: {currentModel}");
                    Log.WriteLine($"  Settings.ClaudeModels list: {string.Join(", ", _settings.ClaudeModels)}");
                    break;
                case "Mistral":
                    currentModel = _settings.MistralModel;
                    Log.WriteLine($"  Settings.MistralModel: {currentModel}");
                    Log.WriteLine($"  Settings.MistralModels list: {string.Join(", ", _settings.MistralModels)}");
                    break;
                case "Gemini":
                    currentModel = _settings.GeminiModel;
                    Log.WriteLine($"  Settings.GeminiModel: {currentModel}");
                    Log.WriteLine($"  Settings.GeminiModels list: {string.Join(", ", _settings.GeminiModels)}");
                    break;
                case "Groq":
                    currentModel = _settings.GroqModel;
                    Log.WriteLine($"  Settings.GroqModel: {currentModel}");
                    Log.WriteLine($"  Settings.GroqModels list: {string.Join(", ", _settings.GroqModels)}");
                    break;
            }
            
            if (_rotationManager != null)
            {
                var rotationModel = _rotationManager.GetCurrentModel(_settings.SelectedAI);
                Log.WriteLine($"  RotationManager.GetCurrentModel(): {rotationModel}");
            }
            
            Log.WriteLine($"  ModelText.Text: {ModelText.Text}");
            
            // ✅ FIX: Use current provider's model for display name lookup
            if (!string.IsNullOrEmpty(currentModel))
            {
                Log.WriteLine($"  Registry display name: {AIModelRegistry.GetDisplayName(currentModel)}");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }



        // ═══════════════════════════════════════════════════════════════
        // CLEANUP
        // ═══════════════════════════════════════════════════════════════

        private void PerformFullCleanup()
        {
            if (_cleanupPerformed)
            {
                Log.WriteLine("Cleanup already performed - skipping duplicate call");
                return;
            }

            _cleanupPerformed = true;

            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("PERFORMING FULL CLEANUP");
            Log.WriteLine($"Is Restarting: {_isRestarting}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            try
            {
                // ✅ CRITICAL: Only clear cache if NOT restarting
                if (!_isRestarting)
                {
                    Log.WriteLine("Normal close detected - clearing conversation cache...");
                    SettingsManager.ClearConversationCache();
                    Log.WriteLine("  ✓ Conversation cache cleared");

                    var completion = _creditMeteringService.FinalizeActiveSession();
                    if (completion != null)
                    {
                        _usageReconciliationService.Enqueue(new UsageReconciliationPayload
                        {
                            UserId = completion.UserId,
                            SessionId = completion.SessionId,
                            StartedAtUtc = completion.StartedAtUtc,
                            EndedAtUtc = completion.EndedAtUtc,
                            ChargedCredits = completion.ChargedCredits,
                            ChargedBlocks = completion.ChargedBlocks,
                            PremiumDebtAdded = completion.PremiumDebtAdded
                        });
                        var reconciliationFlush = _usageReconciliationService.FlushPending();
                        Log.WriteLine(
                            $"  ✓ Interview finalized: session={completion.SessionId}, blocks={completion.ChargedBlocks}, " +
                            $"charged={completion.ChargedCredits:0.##}, premiumDebt={completion.PremiumDebtAdded:0.##}");
                        Log.WriteLine(
                            $"  ✓ Usage reconciliation: pending={reconciliationFlush.PendingBefore}, " +
                            $"synced={reconciliationFlush.SyncedCount}, failed={reconciliationFlush.FailedCount}");
                        LogUsageQueueSnapshot();
                        _telemetryService.Track("billing", "interview_session_finalized", new Dictionary<string, string>
                        {
                            ["session_id"] = completion.SessionId,
                            ["charged_credits"] = completion.ChargedCredits.ToString("0.##"),
                            ["charged_blocks"] = completion.ChargedBlocks.ToString(),
                            ["premium_debt"] = completion.PremiumDebtAdded.ToString("0.##")
                        });
                        _telemetryService.Track("sync", "usage_reconciliation_after_finalize", new Dictionary<string, string>
                        {
                            ["pending"] = reconciliationFlush.PendingBefore.ToString(),
                            ["synced"] = reconciliationFlush.SyncedCount.ToString(),
                            ["failed"] = reconciliationFlush.FailedCount.ToString()
                        });
                    }

                    _interviewLockService.MarkLockReleased();
                }
                else
                {
                    Log.WriteLine("Restart detected - PRESERVING conversation cache");
                    Log.WriteLine($"  Cache location: {SettingsManager.GetConversationCachePath()}");
                }

                _interviewLockHeartbeatTimer?.Stop();
                _interviewLockHeartbeatTimer = null;
                _sessionStatusTimer?.Stop();
                _sessionStatusTimer = null;
                
                Log.WriteLine("Clearing job description on app close...");
                _contextPackService.ClearSelectedPack(clearResume: false, clearJobDescription: true);
                Log.WriteLine("  ✓ Job description cleared");
                
                if (_cursorManager != null)
                {
                    Log.WriteLine("Disposing cursor manager...");
                    _cursorManager.Dispose();
                    _cursorManager = null;
                    Log.WriteLine("  ✓ Cursor manager disposed");
                }

                _taskViewMonitor?.StopMonitoring();

                if (_voiceService != null)
                {
                    Log.WriteLine("Disposing voice service...");
                    _voiceService.Dispose();
                    _voiceService = null;
                    Log.WriteLine("  ✓ Voice service disposed");
                }
                
                if (_hookID != IntPtr.Zero)
                {
                    Log.WriteLine("Removing keyboard hook...");
                    NativeMethods.UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                    Log.WriteLine("  ✓ Keyboard hook removed");
                }
                
                if (_streamUpdateTimer != null)
                {
                    Log.WriteLine("Stopping stream update timer...");
                    _streamUpdateTimer.Stop();
                    _streamUpdateTimer = null;
                    Log.WriteLine("  ✓ Stream timer stopped");
                }
                
                if (_voiceCompletionTimer != null)
                {
                    Log.WriteLine("Stopping voice completion timer...");
                    _voiceCompletionTimer.Stop();
                    _voiceCompletionTimer = null;
                    Log.WriteLine("  ✓ Voice completion timer stopped");
                }
                
                if (_currentRequestCancellation != null)
                {
                    Log.WriteLine("Cancelling ongoing requests...");
                    _currentRequestCancellation.Cancel();
                    _currentRequestCancellation.Dispose();
                    _currentRequestCancellation = null;
                    Log.WriteLine("  ✓ Requests cancelled");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error during cleanup: {ex.Message}");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("✓ CLEANUP COMPLETE");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }



        private void Cleanup()
        {
            PerformFullCleanup();
        }

        private void LogUsageQueueSnapshot()
        {
            var snapshot = _usageReconciliationService.GetQueueSnapshot();
            Log.WriteLine(
                $"  Queue snapshot: pending={snapshot.PendingCount}, failed={snapshot.FailedCount}, deadLetters={snapshot.DeadLetterCount}");

            foreach (var deadLetter in snapshot.DeadLetters.Take(3))
            {
                Log.WriteLine(
                    $"  Dead-letter record: id={deadLetter.RecordId}, session={deadLetter.Payload.SessionId}, " +
                    $"attempts={deadLetter.AttemptCount}, error={deadLetter.LastError}");
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                
                if (hwnd != IntPtr.Zero)
                {
                    // Hide from Alt+Tab and Win+Tab
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW;
                    exStyle |= WS_EX_NOACTIVATE;
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                    
                    // NEW: Hide from Virtual Desktop list
                    // VirtualDesktopHelper.HideFromVirtualDesktop(hwnd);
                    // VirtualDesktopHelper.MakeDesktopChild(hwnd);
                    
                    Log.WriteLine("✓ Main window hidden from Task View and Virtual Desktop");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Failed to hide main window: {ex.Message}");
            }
        }
        
    }
}
