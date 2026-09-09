using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using FormsScreen = System.Windows.Forms.Screen;
using FormsControl = System.Windows.Forms.Control;
using SecureOverlay.Application.Billing;
using SecureOverlay.Application.Context;
using SecureOverlay.Application.Interviews;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Sync;
using SecureOverlay.Application.Telemetry;
using SecureOverlay.Services;
using SecureOverlay.Helpers;
using SecureOverlay.Domain;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Context;
using SecureOverlay.Infrastructure.Billing;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using SecureOverlay.Infrastructure.Interviews;
using SecureOverlay.Infrastructure.Persistence;
using SecureOverlay.Infrastructure.Sync;
using SecureOverlay.Infrastructure.Telemetry;
using SecureOverlay.Platform.Windows.Device;
using SecureOverlay.Platform.Windows.Secrets;
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
        private StringBuilder _streamBuffer = new StringBuilder();
        private System.Windows.Threading.DispatcherTimer? _streamUpdateTimer = null;
        private CancellationTokenSource? _currentRequestCancellation = null;
        private bool _isProcessingRequest = false;
        private readonly List<MarkdownHelper.ChatRenderMessage> _chatMessages = new();
        private string? _streamingChatMarkdown;
        private readonly SemaphoreSlim _chatRenderLock = new(1, 1);
        private bool _chatSurfaceInitialized;
        private bool _chatCursorBridgeInitialized;
        private bool _chatCursorHidden;
        private bool _nextRequestIsVoice;
        private string? _streamMessageId;
        private int _streamRenderedLength;
        private bool _streamDeltaInFlight;
        private bool _mermaidCorrectionInFlight;
        private LiveRequestTrace? _activeRequestTrace;

        private bool _autoSendAfterVoice = false;
        private System.Windows.Threading.DispatcherTimer? _voiceCompletionTimer;
        private bool _isChatSectionCollapsed = false;
        private const double ExpandedWindowMinHeight = 220;
        private const double CollapsedWindowMinHeight = 88;

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
        
        private sealed class AttachedScreenshotItem
        {
            public BitmapImage Image { get; init; } = null!;
            public string? Base64 { get; set; }
        }

        private const int MaxAttachedScreenshots = 3;
        private readonly List<AttachedScreenshotItem> _attachedScreenshots = new();

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
        private int _interviewLockHeartbeatCount;
        private string? _lastRetryableQuestion;
        private Task _managedCatalogRefreshTask = Task.CompletedTask;

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
            _debugLogger.SetUiCollectionEnabled(false);

            Log.WriteLine("Loading settings...");
            _settings = SettingsManager.Load();
            Log.WriteLine($"Settings loaded: AI={_settings.SelectedAI}, Voice={_settings.VoiceInputEnabled}");
            LiveModeComboBox.SelectedIndex = string.Equals(_settings.CopilotMode, "Briefing", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            HeaderOpacitySlider.Value = _settings.WindowOpacity;
            ApplyWindowOpacity(_settings.WindowOpacity, persistSetting: false);
            UpdateLegacyFallbackButtonState();
            UpdateClickThroughButtonState();

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
                interviewSessionRepository,
                () => _settings.PreferByoCreditsFirst,
                () => HasByoEntitlement() && HasAnyConfiguredByoProvider());
            _contextPackService = new LocalContextPackService(contextPackRepository);
            _knowledgeRetrievalService = new HostedKnowledgeRetrievalService(
                new LocalKnowledgeRetrievalService(),
                _authSessionRepository,
                _accountCacheRepository,
                _hostedAccountClient);
            var deviceProfile = new WindowsDeviceIdentityService(
                new SqliteDeviceProfileRepository(store),
                new WindowsSecretVault(store))
                .GetOrCreateProfile();
            _interviewLockService = new HostedInterviewLockService(
                interviewSessionRepository,
                _accountCacheRepository,
                _authSessionRepository,
                HostedClientFactory.CreateLockClient(_hostedRuntimeOptions),
                deviceProfile.InstallId);
            _usageReconciliationService = new LocalUsageReconciliationService(
                usageReconciliationRepository,
                _authSessionRepository,
                HostedClientFactory.CreateUsageClient(_hostedRuntimeOptions));
            _telemetryService = new HostedTelemetryService(
                telemetryRepository,
                _authSessionRepository,
                HostedClientFactory.CreateTelemetryClient(_hostedRuntimeOptions),
                _hostedRuntimeOptions);
            _accountSnapshot = _accountCacheRepository.Load();
            UpdateLegacyFallbackButtonState();
            _managedCatalogRefreshTask = RefreshDesktopCatalogsAsync(force: false);

            var activeInterviewSession = _creditMeteringService.GetActiveSession();
            if (activeInterviewSession != null)
            {
                ActivateInterviewLock(activeInterviewSession);
                _lastInterviewActivityUtc = DateTime.UtcNow;
            }

            _usageReconciliationService.FlushPendingInBackground();
            
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
                    Log.WriteLine($"✗ Error restoring conversation: {ex.GetType().Name}");
                    hasRestoredConversation = false;
                }
            }

            _cursorManager = new CursorManager(
                this, 
                CustomCursorCanvas, 
                _settings.UseFakeCursor,
                _settings.FakeCursorSize
            );
            _cursorManager.SetClickThroughActive(_settings.ClickThroughEnabled);
            Log.WriteLine("Two-cursor system initialized");

            _proc = HookCallback;
            this.Loaded += MainWindow_Loaded;
            this.Closing += (s, e) => Cleanup();

            this.Activated += (s, e) =>
            {
                _cursorManager?.SetApplicationFocusActive(true);
                FocusInput();
                // Activate/Topmost can put the main overlay above the live cursor window.
                _cursorManager?.EnsureLiveCursorAbove();
            };
            this.Deactivated += (s, e) =>
            {
                // Owned dropdown menus take activation; do not tear the cursor down.
                if (_currentDropdownMenu?.IsVisible == true)
                {
                    _cursorManager?.SetOwnedOverlayActive(true);
                    return;
                }

                SetChatCursorHidden(false);
                _cursorManager?.SetApplicationFocusActive(false);
            };

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
            this.Loaded += async (s, e) =>
            {
                // Update API key indicator AFTER window is loaded
                Log.WriteLine("Window loaded - updating API key indicator...");
                UpdateAPIKeyIndicator();
                await EnsureChatSurfaceReadyAsync();
                
                if (hasRestoredConversation && 
                    _conversationManager != null && 
                    cachedConversation != null &&
                    cachedConversation.Messages != null)
                {
                    Log.WriteLine("Rebuilding chat UI from restored conversation...");
                    
                    try
                    {
                        _chatMessages.Clear();
                        
                        var displayMessages = cachedConversation.Messages
                            .Where(m => m.Role != "system")
                            .ToList();
                        
                        Log.WriteLine($"Rebuilding UI with {displayMessages.Count} messages...");
                        
                        int rebuilt = 0;
                        foreach (var msg in displayMessages)
                        {
                            var isUser = msg.Role == "user";
                            var aiName = GetCurrentDisplayProvider();
                            var prefix = isUser ? "**You:** " : $"**{aiName}:** ";
                            var timing = !isUser && msg.ResponseTimeMs.HasValue
                                ? $"\n\n_Response time: {FormatResponseTime(msg.ResponseTimeMs.Value)}_"
                                : string.Empty;
                            var fullText = prefix + msg.Content + timing;
                            _chatMessages.Add(new MarkdownHelper.ChatRenderMessage(isUser, fullText));
                            rebuilt++;
                        }
                        RegenerateButton.IsEnabled = displayMessages.Count >= 2
                            && string.Equals(displayMessages[^1].Role, "assistant", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(displayMessages[^2].Role, "user", StringComparison.OrdinalIgnoreCase);
                        await RefreshChatSurfaceAsync();
                        
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
                        Log.WriteLine($"✗ Error rebuilding chat UI: {ex.GetType().Name}");
                        Log.WriteLine($"Stack trace: {ex.StackTrace}");
                        _chatMessages.Clear();
                        await RefreshChatSurfaceAsync();
                    }
                }
                else
                {
                    if (_chatMessages.Count == 0)
                    {
                        Log.WriteLine("No cached conversation - showing welcome message");
                        await RefreshChatSurfaceAsync();
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
                StatusText.Text = $"⚠️ {UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Title)}";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat(
                    $"⚠️ **{UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Title)}**\n\n{UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Message)}\n\n{UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Detail)}\n\nExisting locked interview continuation is still allowed on this device.",
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

            StatusText.Text = $"⚠️ {UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Title)}";
            StatusIndicator.Fill = Brushes.Orange;

            AddToChat(
                $"⚠️ **{UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Title)}**\n\n{UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Message)}\n\n{UserFacingErrorSanitizer.SanitizeUserFacingError(_launchContext.Detail)}",
                true);
        }

        private void RefreshAccountSnapshot()
        {
            _accountSnapshot = _accountCacheRepository.Load();
            UpdateLegacyFallbackButtonState();
        }

        private void ApplyAccountTierChrome()
        {
            var selectorsVisible = ShouldShowByoSelectors() ? Visibility.Visible : Visibility.Collapsed;
            ProviderSelectorBorder.Visibility = selectorsVisible;
            ModelSelectorBorder.Visibility = selectorsVisible;

            if (!ShouldShowByoSelectors())
            {
                APIKeyIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private bool ShouldShowByoSelectors()
        {
            if (!HasByoEntitlement() || !HasAnyConfiguredByoProvider())
            {
                return false;
            }

            // PreferByoCreditsFirst alone is not enough while premium is still the active lane.
            return IsByoLaneActiveNow();
        }

        private void UpdateCreditIndicator()
        {
            RefreshAccountSnapshot();
            if (_accountSnapshot == null)
            {
                CreditIndicatorText.Text = "Cr n/a";
                UpdateActiveCreditModeIndicator();
                return;
            }

            if (IsFreeTrialAccount())
            {
                CreditIndicatorText.Text = "Trial 2x15m";
                UpdateActiveCreditModeIndicator();
                return;
            }

            var hasPremiumLaneOrDebt = HasPremiumManagedEntitlement() || (_accountSnapshot.PremiumNegativeCredits > 0m);
            if (hasPremiumLaneOrDebt && HasByoEntitlement())
            {
                CreditIndicatorText.Text =
                    $"P {_accountSnapshot.PremiumAvailableCredits:0.##} | B {_accountSnapshot.ProAvailableCredits:0.##} | D {_accountSnapshot.PremiumNegativeCredits:0.##}";
                UpdateActiveCreditModeIndicator();
                return;
            }

            if (hasPremiumLaneOrDebt)
            {
                CreditIndicatorText.Text =
                    $"P {_accountSnapshot.PremiumAvailableCredits:0.##} | D {_accountSnapshot.PremiumNegativeCredits:0.##}";
                UpdateActiveCreditModeIndicator();
                return;
            }

            CreditIndicatorText.Text = $"BYO {_accountSnapshot.ProAvailableCredits:0.##} | D {_accountSnapshot.PremiumNegativeCredits:0.##}";
            UpdateActiveCreditModeIndicator();
        }

        private void UpdateActiveCreditModeIndicator()
        {
            if (ActiveCreditModeText == null || ActiveCreditModeBorder == null)
            {
                return;
            }

            var (label, background) = ResolveActiveCreditMode();
            ActiveCreditModeText.Text = label;
            ActiveCreditModeBorder.Background = background;
        }

        private (string Label, Brush Background) ResolveActiveCreditMode()
        {
            if (_accountSnapshot == null)
            {
                return ("Idle", new SolidColorBrush(Color.FromArgb(0x50, 0x50, 0x50, 0x50)));
            }

            if (IsFreeTrialAccount())
            {
                return ("Trial", new SolidColorBrush(Color.FromArgb(0x50, 0x22, 0x6F, 0xA8)));
            }

            var activeSession = _creditMeteringService.GetActiveSession();
            if (activeSession != null)
            {
                return DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI) switch
                {
                    SecureOverlay.Domain.Enums.InterviewUsageSource.ProByo
                        => ("BYO", new SolidColorBrush(Color.FromArgb(0x50, 0x18, 0x72, 0x45))),
                    SecureOverlay.Domain.Enums.InterviewUsageSource.PremiumDebtExtension
                        => ("Debt", new SolidColorBrush(Color.FromArgb(0x50, 0x9A, 0x3D, 0x00))),
                    SecureOverlay.Domain.Enums.InterviewUsageSource.PremiumManaged
                        => ("Premium", new SolidColorBrush(Color.FromArgb(0x50, 0x6A, 0x4C, 0x1F))),
                    _ => ("Trial", new SolidColorBrush(Color.FromArgb(0x50, 0x22, 0x6F, 0xA8)))
                };
            }

            if (_accountSnapshot.PremiumNegativeCredits > 0m)
            {
                return ("Debt", new SolidColorBrush(Color.FromArgb(0x50, 0x9A, 0x3D, 0x00)));
            }

            if (IsByoLaneActiveNow())
            {
                return ("BYO", new SolidColorBrush(Color.FromArgb(0x50, 0x18, 0x72, 0x45)));
            }

            if (HasPremiumManagedEntitlement())
            {
                return ("Premium", new SolidColorBrush(Color.FromArgb(0x50, 0x6A, 0x4C, 0x1F)));
            }

            if (HasByoEntitlement())
            {
                // Show BYO whenever BYO credits/tier exist — even before keys are configured —
                // so Idle is reserved for accounts with no usable paid lane.
                return ("BYO", new SolidColorBrush(Color.FromArgb(0x50, 0x18, 0x72, 0x45)));
            }

            return ("Idle", new SolidColorBrush(Color.FromArgb(0x50, 0x50, 0x50, 0x50)));
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
                UpdateActiveCreditModeIndicator();
                return;
            }

            SyncRuntimeWithCurrentCreditLane();
            UpdateActiveCreditModeIndicator();

            if (activeSession.State == SecureOverlay.Domain.Enums.InterviewSessionState.Paused)
            {
                var pausedElapsed = _creditMeteringService.GetMeteredElapsed(activeSession);
                var pausedBilledMinutes = Math.Max(0, (int)Math.Floor(pausedElapsed.TotalSeconds / 60d));
                var pausedProjectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(pausedElapsed);

                SessionTimerBorder.Visibility = Visibility.Visible;
                SessionTimerText.Text = $"T {pausedElapsed:hh\\:mm\\:ss}";
                SessionStatusText.Text = $"Paused | {pausedBilledMinutes} min | {pausedProjectedCharge:0.##} cr";
                return;
            }

            if (ShouldFinalizeAtCurrentBoundary(activeSession))
            {
                FinalizeActiveInterviewSessionAtBoundary();
                return;
            }

            var elapsed = _creditMeteringService.GetMeteredElapsed(activeSession);
            var billedMinutes = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds / 60d));
            var projectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(elapsed);

            SessionTimerBorder.Visibility = Visibility.Visible;
            SessionTimerText.Text = $"T {elapsed:hh\\:mm\\:ss}";
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
            var inactivityThreshold = TimeSpan.FromMinutes(Math.Max(10, _settings.AutoPauseOnInactivityMinutes));
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
            return projectedCharge >= GetTotalPaidCreditsAvailable();
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
                    ConsumedProCredits = completion.ConsumedProCredits,
                    ConsumedPremiumCredits = completion.ConsumedPremiumCredits,
                    PremiumDebtAdded = completion.PremiumDebtAdded,
                    QuestionInputs = completion.QuestionInputs
                });
                _usageReconciliationService.FlushPendingInBackground();
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
                    $"Boundary usage reconciliation queued for background flush: session={completion.SessionId}");
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

        private async Task RefreshDesktopCatalogsAsync(bool force)
        {
            var cacheEmpty = CatalogRefreshQuota.IsChatCatalogEmpty(_settings)
                || CatalogRefreshQuota.IsSpeechCatalogEmpty(_settings);
            if (!force && !CatalogRefreshQuota.TryConsumeAutomaticRefresh(_settings, cacheEmpty))
            {
                Log.WriteLine("Automatic catalog refresh skipped (daily quota)");
                return;
            }

            await RefreshManagedCatalogCacheAsync(force: true);
            await RefreshByoCatalogCacheAsync(forceAll: true);
        }

        private async Task RefreshManagedCatalogCacheAsync(bool force = false)
        {
            try
            {
                var session = _authSessionRepository.Load();
                if (session == null || !session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
                {
                    return;
                }

                if (!force)
                {
                    var cacheEmpty = CatalogRefreshQuota.IsChatCatalogEmpty(_settings)
                        || CatalogRefreshQuota.IsSpeechCatalogEmpty(_settings);
                    if (!CatalogRefreshQuota.TryConsumeAutomaticRefresh(_settings, cacheEmpty))
                    {
                        Log.WriteLine("Automatic managed/speech catalog refresh skipped (daily quota)");
                        return;
                    }
                }

                var catalog = await _hostedAccountClient.GetManagedCatalogAsync(session.AccessToken);
                try
                {
                    _settings.SpeechCatalogCache = await _hostedAccountClient.GetSpeechCatalogAsync(session.AccessToken);
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Speech catalog refresh skipped: {ex.GetType().Name}");
                }
                if (catalog != null)
                {
                    _settings.PremiumConfiguredProviders = (catalog.Providers ?? new List<ManagedAiProviderOptionDto>())
                        .Select(item => item.ProviderId)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    ProviderModelCatalogCache.ReplaceCatalog(_settings, catalog);
                    SettingsManager.Save(_settings);

                    var managedProvider = catalog.Providers?.FirstOrDefault();
                    var managedModel = managedProvider?.Models?.FirstOrDefault();
                    Log.WriteLine(
                        $"Managed catalog refreshed: provider={managedProvider?.ProviderId ?? "none"}, model={managedModel?.ModelId ?? "none"}");

                    if (_currentAI is HostedManagedAiService && managedProvider != null && managedModel != null)
                    {
                        Log.WriteLine("Managed catalog changed - reinitializing AI with the hosted runtime selection");
                        InitializeAI();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Managed catalog refresh skipped: {ex.GetType().Name}");
            }
        }

        private async Task RefreshByoCatalogCacheAsync(string? forceProvider = null, bool forceAll = false)
        {
            var session = _authSessionRepository.Load();
            if (session == null || !session.IsAuthenticated || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return;
            }

            var explicitRefresh = forceAll || !string.IsNullOrWhiteSpace(forceProvider);
            if (!explicitRefresh)
            {
                var cacheEmpty = CatalogRefreshQuota.IsChatCatalogEmpty(_settings)
                    || CatalogRefreshQuota.IsSpeechCatalogEmpty(_settings);
                if (!CatalogRefreshQuota.TryConsumeAutomaticRefresh(_settings, cacheEmpty))
                {
                    Log.WriteLine("Automatic BYO/speech catalog refresh skipped (daily quota)");
                    return;
                }
            }

            var selectedProvider = _settings.SelectedAI;
            var selectedModels = CaptureSelectedByoModels();
            var speechProvider = _settings.SpeechProviderId;
            var speechModel = _settings.SpeechModelId;

            await ByoProviderModelCatalogService.RefreshStaleCatalogsAsync(
                _settings,
                _hostedAccountClient,
                session.AccessToken,
                forceProvider,
                forceAll);

            try
            {
                _settings.SpeechCatalogCache = await _hostedAccountClient.GetSpeechCatalogAsync(session.AccessToken);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Speech catalog refresh skipped: {ex.GetType().Name}");
            }

            RestoreSelectedByoModels(selectedProvider, selectedModels, speechProvider, speechModel);
            SettingsManager.Save(_settings);
            if (_currentAI != null && IsByoLaneActiveNow() && GetConfiguredModelsForProvider(_settings.SelectedAI).Length > 0)
            {
                InitializeAI();
            }

            ApplyAccountTierChrome();
            UpdateProviderAndModelDisplay();
        }

        private Dictionary<string, string> CaptureSelectedByoModels()
        {
            var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in AIModelRegistry.GetAllProviders())
            {
                var model = AIModelRegistry.GetCurrentModelForProvider(_settings, provider);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    selected[provider] = model;
                }
            }

            return selected;
        }

        private void RestoreSelectedByoModels(
            string selectedProvider,
            Dictionary<string, string> selectedModels,
            string speechProvider,
            string speechModel)
        {
            if (!string.IsNullOrWhiteSpace(selectedProvider))
            {
                _settings.SelectedAI = selectedProvider;
            }

            foreach (var pair in selectedModels)
            {
                var available = ProviderModelCatalogCache.GetModelIds(_settings, pair.Key, byo: true);
                if (available.Length == 0 || available.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
                {
                    AIModelRegistry.SetModelForProvider(_settings, pair.Key, pair.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(speechProvider))
            {
                _settings.SpeechProviderId = speechProvider;
            }

            if (!string.IsNullOrWhiteSpace(speechModel))
            {
                _settings.SpeechModelId = speechModel;
            }
        }

        private ManagedAiProviderOptionDto? GetManagedProviderCatalog(string provider)
        {
            return ProviderModelCatalogCache.GetProvider(_settings, provider);
        }

        private string[] GetConfiguredModelsForProvider(string provider)
        {
            return ProviderModelCatalogCache.GetModelIds(_settings, provider, byo: true);
        }

        private ModelConfig GetModelConfigForCurrentSelection(string provider, string modelId)
        {
            var registryConfig = AIModelRegistry.GetModelConfig(modelId);
            if (!string.Equals(registryConfig.Name, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return registryConfig;
            }

            var managedModel = ProviderModelCatalogCache.GetModel(_settings, provider, modelId)
                ?? ProviderModelCatalogCache.GetModel(_settings, provider, modelId, byo: true);
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

            return (ProviderModelCatalogCache.GetModel(_settings, provider, modelId)
                    ?? ProviderModelCatalogCache.GetModel(_settings, provider, modelId, byo: true))?.DisplayName
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

        /// <summary>
        /// True when BYO is the only viable paid lane (or the lane that would start next) but no provider keys exist.
        /// Used to surface a key-required error before interview metering.
        /// </summary>
        private bool RequiresByoProviderKeysBeforeSend(out string provider)
        {
            provider = string.IsNullOrWhiteSpace(_settings.SelectedAI) ? "provider" : _settings.SelectedAI;
            if (IsFreeTrialAccount() || !HasByoEntitlement() || HasAnyConfiguredByoProvider())
            {
                return false;
            }

            var byoCredits = _accountSnapshot?.ProAvailableCredits ?? 0m;
            if (byoCredits <= 0m)
            {
                return false;
            }

            // Prefer-BYO-first would meter on BYO when credits exist — require keys even if Premium remains.
            if (!PreferByoCreditsFirst()
                && HasPremiumManagedEntitlement()
                && PremiumCreditsCanStillCoverCurrentSession())
            {
                return false;
            }

            // Prefer configured selection, else first BYO catalog provider id.
            if (string.IsNullOrWhiteSpace(provider) || IsManagedProvider(provider))
            {
                provider = (_settings.ByoAiCatalogCache?.Providers ?? new List<ManagedAiProviderOptionDto>())
                    .Select(item => item.ProviderId)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                    ?? provider;
            }

            return true;
        }

        private bool HasAnyConfiguredByoProvider()
        {
            if (_rotationManager == null)
            {
                return false;
            }

            return AIModelRegistry.GetAllProviders().Any(provider => _rotationManager.GetTotalKeyCount(provider) > 0);
        }

        private string GetFallbackConfiguredByoProvider()
        {
            if (_rotationManager == null)
            {
                return _settings.SelectedAI;
            }

            if (HasConfiguredByoKeysForProvider(_settings.SelectedAI))
            {
                return _settings.SelectedAI;
            }

            return AIModelRegistry.GetAllProviders()
                .FirstOrDefault(provider => _rotationManager.GetTotalKeyCount(provider) > 0)
                ?? _settings.SelectedAI;
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
            return projectedCharge < premiumCredits;
        }

        private bool ByoCreditsCanStillCoverCurrentSession()
        {
            var byoCredits = _accountSnapshot?.ProAvailableCredits ?? 0m;
            if (byoCredits <= 0m)
            {
                return false;
            }

            var activeSession = _creditMeteringService.GetActiveSession();
            if (activeSession == null)
            {
                return true;
            }

            var projectedCharge = LocalCreditMeteringService.EstimateChargeForElapsed(_creditMeteringService.GetMeteredElapsed(activeSession));
            return projectedCharge < byoCredits;
        }

        private bool PreferByoCreditsFirst()
        {
            return _settings.PreferByoCreditsFirst && HasByoEntitlement() && HasPremiumManagedEntitlement();
        }

        private bool IsByoLaneActiveNow()
        {
            if (IsFreeTrialAccount() || !HasByoEntitlement() || !HasAnyConfiguredByoProvider())
            {
                return false;
            }

            if (PreferByoCreditsFirst())
            {
                return ByoCreditsCanStillCoverCurrentSession() || !HasPremiumManagedEntitlement();
            }

            if (HasPremiumManagedEntitlement() && PremiumCreditsCanStillCoverCurrentSession())
            {
                return false;
            }

            return true;
        }

        private string GetManagedRuntimeProviderId()
        {
            return _settings.ManagedAiCatalogCache?.Providers?
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.ProviderId))
                ?.ProviderId
                ?? _settings.SelectedAI;
        }

        private string GetManagedRuntimeModelId(string provider)
        {
            return ProviderModelCatalogCache.GetModelIds(_settings, provider).FirstOrDefault()
                ?? _rotationManager?.GetCurrentModel(provider)
                ?? string.Empty;
        }

        private string GetCurrentDisplayProvider()
        {
            return _currentAI is HostedManagedAiService
                ? "AI"
                : (_currentAI?.GetProviderName() ?? "AI");
        }

        private string GetCurrentRuntimeProviderId()
        {
            return _currentAI is HostedManagedAiService
                ? _conversationManager?.CurrentProvider ?? GetManagedRuntimeProviderId()
                : _settings.SelectedAI;
        }

        private string GetCurrentRuntimeModelId()
        {
            var provider = GetCurrentRuntimeProviderId();
            return _currentAI is HostedManagedAiService
                ? _conversationManager?.CurrentModel ?? GetManagedRuntimeModelId(provider)
                : _rotationManager?.GetCurrentModel(provider) ?? string.Empty;
        }

        private void SyncRuntimeWithCurrentCreditLane()
        {
            if (_currentAI == null)
            {
                return;
            }

            var shouldUseByoRuntime = IsByoLaneActiveNow();
            var isUsingByoRuntime = _currentAI is not HostedManagedAiService;
            if (shouldUseByoRuntime != isUsingByoRuntime)
            {
                Log.WriteLine($"Credit lane changed - reinitializing AI. BYO required: {shouldUseByoRuntime}");
                InitializeAI();
            }
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

            return IsByoLaneActiveNow();
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
                // BYO: catalog cache only — never fall back to AIModelRegistry model IDs.
                return (_settings.ByoAiCatalogCache?.Providers ?? new List<ManagedAiProviderOptionDto>())
                    .Select(item => item.ProviderId)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
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

            ResetEmbeddedCursorState();
            _cursorManager?.ActivateCustomCursor();
        }

        private void MainWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingWindow)
                return;

            // WebView2 is a child HWND and can raise a WPF MouseLeave even though
            // the pointer is still inside the outer Phantom window.
            if (_cursorManager?.IsPointerInsideParentWindow() == true)
                return;

            // Provider/model menus are separate Topmost windows. Leaving the main
            // chrome onto them must not tear down the live cursor.
            if (IsPointerOverCurrentDropdownMenu())
            {
                _cursorManager?.EnsureLiveCursorAbove();
                return;
            }

            ResetEmbeddedCursorState();
            _cursorManager?.DeactivateCustomCursor();
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ResetEmbeddedCursorState();
            var position = e.GetPosition(this);
            _cursorManager?.UpdateCustomCursorPosition(position);
        }

        private void ResetEmbeddedCursorState()
        {
            SetChatCursorHidden(false);
            _cursorManager?.SetEmbeddedSurfaceCursorActive(false);
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
            ApplyClickThroughState(_settings.ClickThroughEnabled);
            UpdateClickThroughButtonState();
            
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
            var debugModeEnabled = _accountSnapshot?.CanUseDesktopPowerFeatures == true && _settings.DebugModeEnabled;
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
            var rotationManager = new APIRotationManager(_settings);
            _rotationManager = rotationManager;
            
            Log.WriteLine($"✓ Rotation manager initialized");
            Log.WriteLine($"  Auto-switch keys: {_settings.AutoSwitchKeysOnError}");
            Log.WriteLine($"  Auto-switch models: {_settings.AutoSwitchModelsOnError}");
            
            // Log key counts for current provider
            var keyCount = rotationManager.GetTotalKeyCount(_settings.SelectedAI);
            var availableKeys = rotationManager.GetAvailableKeyCount(_settings.SelectedAI);
            Log.WriteLine($"  {_settings.SelectedAI} keys: {keyCount} total, {availableKeys} available");

            var allowedProviders = GetAvailableProvidersForCurrentTier();
            if (allowedProviders.Length > 0
                && !allowedProviders.Contains(_settings.SelectedAI, StringComparer.OrdinalIgnoreCase))
            {
                _settings.SelectedAI = allowedProviders[0];
                SettingsManager.Save(_settings);
            }

            var selectedProvider = _settings.SelectedAI;
            
            // ✅ FIX: Get the CORRECT model from settings (not hardcoded default)
            var currentModel = rotationManager.GetCurrentModel(selectedProvider);
            var allowedModels = GetConfiguredModelsForProvider(selectedProvider);
            if (allowedModels.Length > 0
                && !allowedModels.Contains(currentModel, StringComparer.OrdinalIgnoreCase))
            {
                currentModel = allowedModels[0];
                AIModelRegistry.SetModelForProvider(_settings, selectedProvider, currentModel);
                SettingsManager.Save(_settings);
            }
            
            var useByoRuntime = ShouldUseByoRuntimeForCurrentSelection(selectedProvider);
            // Keep the user's SelectedAI. Do not rewrite it to another keyed provider
            // (that made title-bar / settings provider changes snap back to e.g. Mistral).
            var runtimeProvider = useByoRuntime ? selectedProvider : GetManagedRuntimeProviderId();

            currentModel = useByoRuntime
                ? (rotationManager.GetCurrentModel(runtimeProvider) ?? currentModel)
                : GetManagedRuntimeModelId(runtimeProvider);

            Log.WriteLine($"✓ Loading model from settings: {currentModel}");
            var runtimeModel = useByoRuntime ? currentModel : GetManagedRuntimeModelId(runtimeProvider);
            
            // Create AI service with rotation
            IAIService newAI = useByoRuntime
                ? AIServiceFactory.CreateServiceWithRotation(runtimeProvider, rotationManager)
                : new HostedManagedAiService(
                    _authSessionRepository,
                    _hostedRuntimeOptions,
                    runtimeProvider,
                    runtimeModel,
                    _settings.AllowByoSessionExtension);

            var modelConfig = GetModelConfigForCurrentSelection(runtimeProvider, runtimeModel);
            Log.WriteLine($"Model config: {modelConfig.Name} ({modelConfig.MaxContextTokens} tokens)");

            if (_conversationManager != null)
            {
                _conversationManager.APISwitchNotification -= OnAPISwitchNotification;
                _conversationManager.StageChanged -= OnCopilotStageChanged;
                _conversationManager.DecisionParsed -= OnCopilotDecisionParsed;
                _conversationManager.UpdateAIService(newAI);
                _conversationManager.UpdateModelConfig(modelConfig);
                _conversationManager.ConfigureCopilot(
                    string.Equals(_settings.CopilotMode, "Briefing", StringComparison.OrdinalIgnoreCase) ? CopilotMode.Briefing : CopilotMode.Interview,
                    string.Equals(_settings.InterviewDeliveryStyle, "Desi", StringComparison.OrdinalIgnoreCase) ? InterviewDeliveryStyle.Desi : InterviewDeliveryStyle.Standard);
                _conversationManager.SetRotationManager(rotationManager);
                
                // Subscribe to API switch notifications
                _conversationManager.APISwitchNotification += OnAPISwitchNotification;
                _conversationManager.StageChanged += OnCopilotStageChanged;
                _conversationManager.DecisionParsed += OnCopilotDecisionParsed;
                
                Log.WriteLine("✓ AI service updated - conversation history PRESERVED");
            }
            else
            {
                _conversationManager = new ConversationManager(
                    newAI,
                    string.Empty,
                    modelConfig,
                    _rotationManager,
                    (query, preferredDocumentIds, cancellationToken) => _knowledgeRetrievalService.RetrieveForPromptAsync(
                        _contextPackService.GetSelectedPack(),
                        query,
                        preferredDocumentIds,
                        cancellationToken: cancellationToken),
                    async cancellationToken =>
                    {
                        var session = _authSessionRepository.Load();
                        if (session == null
                            || !session.IsAuthenticated
                            || string.IsNullOrWhiteSpace(session.AccessToken))
                        {
                            return new HostedKnowledgeBaseSummaryDto();
                        }

                        return await _hostedAccountClient.GetKnowledgeBaseAsync(session.AccessToken, cancellationToken);
                    },
                    () => !IsByoAccount() && HasPremiumManagedEntitlement());
                _conversationManager.ConfigureCopilot(
                    string.Equals(_settings.CopilotMode, "Briefing", StringComparison.OrdinalIgnoreCase) ? CopilotMode.Briefing : CopilotMode.Interview,
                    string.Equals(_settings.InterviewDeliveryStyle, "Desi", StringComparison.OrdinalIgnoreCase) ? InterviewDeliveryStyle.Desi : InterviewDeliveryStyle.Standard);
                
                // Subscribe to API switch notifications
                _conversationManager.APISwitchNotification += OnAPISwitchNotification;
                _conversationManager.StageChanged += OnCopilotStageChanged;
                _conversationManager.DecisionParsed += OnCopilotDecisionParsed;
                
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

                _ = PrepareAndPersistContextAsync();

                Log.WriteLine("✓ New conversation manager created with rotation support");
            }

            _currentAI = newAI;
            UpdateTokenCounter();
            ApplyAccountTierChrome();

            if (!_currentAI.IsConfigured())
            {
                Log.WriteLine($"⚠️ {GetCurrentDisplayProvider()} not configured (no API key)");
            }
            else
            {
                Log.WriteLine($"✓ AI service: {GetCurrentDisplayProvider()} (configured)");
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
                APIKeyText.Text = $"K{currentIndex + 1}/{keyCount}";
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
                APIKeyText.Text = "K1";
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

        private void OnCopilotStageChanged(object? sender, string stage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusText.Text = stage;
                StatusIndicator.Fill = stage.StartsWith("Searching", StringComparison.Ordinal)
                    ? Brushes.DeepSkyBlue
                    : Brushes.Yellow;
            }));
        }

        private void OnCopilotDecisionParsed(LiveTurnDecision decision, int modelCallCount)
        {
            if (_activeRequestTrace is not { } trace) return;
            TrackLiveCopilotAsync("control_frame_parsed", trace, new Dictionary<string, string>
            {
                ["question_type"] = decision.QuestionType,
                ["intent"] = decision.Intent,
                ["action"] = decision.Action.ToString().ToLowerInvariant(),
                ["answer_basis"] = decision.AnswerBasis,
                ["confidence_bucket"] = decision.Confidence < .5 ? "low" : decision.Confidence < .8 ? "medium" : "high",
                ["entity_type"] = decision.EntityType,
                ["has_entity_id"] = (!string.IsNullOrEmpty(decision.EntityId)).ToString().ToLowerInvariant(),
                ["protocol_version"] = decision.ProtocolVersion.ToString(),
                ["validation_outcome"] = "accepted",
                ["model_call_count"] = modelCallCount.ToString(),
                ["retrieval_status"] = decision.Action == LiveCopilotAction.Retrieve ? "pending" : "not_requested"
            });
        }

        private void LiveModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null || LiveModeComboBox.SelectedItem is not ComboBoxItem item) return;
            _settings.CopilotMode = item.Content?.ToString() == "Briefing" ? "Briefing" : "Interview";
            SettingsManager.Save(_settings);
            _conversationManager?.ConfigureCopilot(
                _settings.CopilotMode == "Briefing" ? CopilotMode.Briefing : CopilotMode.Interview,
                _settings.InterviewDeliveryStyle == "Desi" ? InterviewDeliveryStyle.Desi : InterviewDeliveryStyle.Standard);
            _ = RefreshChatSurfaceAsync();
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

        private async Task SendMessage(bool captureQuestion = true)
        {
            ClarificationOptionsPanel.Children.Clear();
            ClarificationOptionsPanel.Visibility = Visibility.Collapsed;
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

            if (_currentAI is HostedManagedAiService)
            {
                await _managedCatalogRefreshTask;
            }

            RefreshAccountSnapshot();
            if (ShouldUseByoRuntimeForCurrentSelection(_settings.SelectedAI) != (_currentAI is not HostedManagedAiService))
            {
                InitializeAI();
            }

            var message = InputTextBox.Text.Trim();
            var hasQuestionInput = !string.IsNullOrEmpty(message) && message != "Ask me anything...";
            var questionCaptured = false;
            if (captureQuestion && hasQuestionInput && activeSessionBeforeSend != null)
            {
                _creditMeteringService.TrackQuestionInput(message);
                questionCaptured = true;
            }
            
            if (string.IsNullOrEmpty(message) || message == "Ask me anything...") 
            {
                if (_attachedScreenshots.Count == 0)
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

            using var requestTrace = LiveRequestTrace.Begin(
                GetCurrentRuntimeProviderId(),
                GetCurrentRuntimeModelId(),
                _nextRequestIsVoice,
                _attachedScreenshots.Count > 0,
                _settings.CopilotMode.ToLowerInvariant(),
                _settings.InterviewDeliveryStyle.ToLowerInvariant(),
                _currentAI is HostedManagedAiService ? "managed" : "byo",
                DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI).ToString().ToLowerInvariant());
            _activeRequestTrace = requestTrace;
            TrackLiveCopilotAsync("request_dispatched", requestTrace, new Dictionary<string, string>
            {
                ["stage"] = "dispatch", ["provider"] = requestTrace.Provider, ["model"] = requestTrace.Model,
                ["image_present"] = requestTrace.HasImage.ToString().ToLowerInvariant(),
                ["execution_lane"] = requestTrace.ExecutionLane, ["usage_source"] = requestTrace.UsageSource
            });
            _nextRequestIsVoice = false;

            // BYO credits without keys must fail with a key-required message before metering,
            // even when the runtime still looks "managed" because IsByoLaneActiveNow requires keys.
            if (RequiresByoProviderKeysBeforeSend(out var missingByoProvider))
            {
                Log.WriteLine($"BYO credits available but no {missingByoProvider} key configured - blocking before metering");
                AddToChat(
                    $"⚠️ **{missingByoProvider} key required**\n\n" +
                    $"Add a {missingByoProvider} API key in Settings before starting this interview. BYO credits are available, but Phantom cannot call the provider without a key.",
                    false);
                StatusText.Text = "⚠️ BYO provider key required";
                StatusIndicator.Fill = Brushes.Orange;
                FocusInput();
                return;
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

            if (_attachedScreenshots.Count > 0 && !CurrentModelSupportsVision())
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
                StatusText.Text = $"⚠️ {UserFacingErrorSanitizer.SanitizeUserFacingError(meteringActivation.Title)}";
                StatusIndicator.Fill = Brushes.Orange;
                AddToChat($"⚠️ **{UserFacingErrorSanitizer.SanitizeUserFacingError(meteringActivation.Title)}**\n\n{UserFacingErrorSanitizer.SanitizeUserFacingError(meteringActivation.Message)}", false);
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
                if (captureQuestion && hasQuestionInput && !questionCaptured)
                {
                    _creditMeteringService.TrackQuestionInput(message);
                }
                RecordInterviewActivity("session_active");
                RefreshAccountSnapshot();
                UpdateCreditIndicator();
                UpdateSessionStatus();
            }

            _creditMeteringService.TrackUsageSource(DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI), _settings.SelectedAI);

            var imagesBase64 = new List<string>();
            if (_attachedScreenshots.Count > 0)
            {
                foreach (var item in _attachedScreenshots)
                {
                    item.Base64 ??= await BitmapImageToBase64Async(item.Image);
                    if (!string.IsNullOrWhiteSpace(item.Base64))
                    {
                        imagesBase64.Add(item.Base64);
                    }
                }

                if (imagesBase64.Count > 0)
                {
                    Log.WriteLine($"✓ Encoded {imagesBase64.Count} screenshot(s) for transmission");
                    if (_currentAI is HostedManagedAiService && imagesBase64.Count > 1)
                    {
                        message += $"\n\n({imagesBase64.Count} screenshots attached.)";
                    }
                }
                else
                {
                    Log.WriteLine("✗ Failed to encode screenshots - sending without images");
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
                _streamingChatMarkdown = null;
                await RefreshChatSurfaceAsync();
                
                await Task.Delay(200);
                
                Log.WriteLine("✓ Previous request cleaned up");
            }

            _currentRequestCancellation = new CancellationTokenSource();
            _isProcessingRequest = true;
            RegenerateButton.IsEnabled = false;

            Log.WriteLine($"Sending message length_bucket={LengthBucket(message.Length)}");
            _lastRetryableQuestion = message;
            AddToChat($"**You:** {message}", false);
            InputTextBox.Text = "";

            StatusText.Text = "Understanding…";
            StatusIndicator.Fill = Brushes.Yellow;
            
            var aiName = GetCurrentDisplayProvider();
            
            lock (_streamBuffer)
            {
                _streamBuffer.Clear();
            }
            _streamingChatMarkdown = null;
            await RefreshChatSurfaceAsync();
            _streamMessageId = requestTrace.CorrelationId;
            _streamRenderedLength = 0;
            await MarkdownHelper.BeginAssistantMessageAsync(ChatWebView, _streamMessageId, aiName);
            
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
                        lock (_streamBuffer)
                        {
                            if (requestTrace.Mark("model_first_byte_received", uniquePerOperation: true))
                            {
                                TrackLiveCopilotAsync("model_first_byte_received", requestTrace, new Dictionary<string, string>
                                {
                                    ["stage"] = "answer", ["elapsed_ms"] = requestTrace.ElapsedMilliseconds.ToString("F0")
                                });
                                Dispatcher.BeginInvoke(new Action(() => RecordInterviewActivity("response_stream")));
                            }
                            _streamBuffer.Append(chunk);
                        }
                    }
                };

                var (response, error) = await Task.Run(
                    () => _conversationManager.SendMessageStreamAsync(
                        message,
                        onChunk,
                        _currentRequestCancellation.Token,
                        imagesBase64,
                        ResetCurrentStreamingAttempt),
                    _currentRequestCancellation.Token);
                
                _streamUpdateTimer?.Stop();

                var canRetryWithManagedFallback =
                    !string.IsNullOrEmpty(error) &&
                    !string.Equals(error, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    !(_currentAI is HostedManagedAiService) &&
                    CanUseManagedExtensionFallbackForProvider(_settings.SelectedAI) &&
                    ProviderResiliencePolicy.CanCrossLane(
                        "byo", "managed_extension", _settings.AllowByoSessionExtension,
                        _conversationManager.LastOperationHadOutput);

                if (canRetryWithManagedFallback)
                {
                    Log.WriteLine($"BYO runtime failed for {_settings.SelectedAI}. Retrying same request with managed extension fallback.");
                    ForceManagedExtensionForCurrentProvider(_settings.SelectedAI);
                    _creditMeteringService.TrackUsageSource(DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI), _settings.SelectedAI);
                    requestTrace.SwitchLane(
                        "managed_extension",
                        DetermineUsageSourceForCurrentRuntime(_settings.SelectedAI).ToString().ToLowerInvariant());
                    TrackLiveCopilotAsync("execution_lane_changed", requestTrace, new Dictionary<string, string>
                    {
                        ["outcome"] = "fallback", ["execution_lane"] = requestTrace.ExecutionLane,
                        ["usage_source"] = requestTrace.UsageSource, ["cross_lane_fallback"] = "true"
                    });

                    lock (_streamBuffer)
                    {
                        _streamBuffer.Clear();
                    }

                    StatusText.Text = "🔄 Switching to managed extension...";
                    StatusIndicator.Fill = Brushes.Yellow;

                    aiName = GetCurrentDisplayProvider();
                    _streamRenderedLength = 0;
                    await MarkdownHelper.BeginAssistantMessageAsync(ChatWebView, _streamMessageId!, aiName);

                    _streamUpdateTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(50)
                    };
                    _streamUpdateTimer.Tick += StreamUpdateTimer_Tick;
                    _streamUpdateTimer.Start();

                    (response, error) = await Task.Run(
                        () => _conversationManager.SendMessageStreamAsync(
                            message,
                            onChunk,
                            _currentRequestCancellation.Token,
                            imagesBase64,
                            ResetCurrentStreamingAttempt),
                        _currentRequestCancellation.Token);

                    _streamUpdateTimer?.Stop();
                }
                
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (error == "Cancelled")
                {
                    requestTrace.Complete(0, "cancelled");
                    TrackLiveCopilotAsync("turn_cancelled", requestTrace, new Dictionary<string, string> { ["outcome"] = "cancelled" });
                    Log.WriteLine("✗ Request was cancelled");
                    _streamingChatMarkdown = null;
                    _streamMessageId = null;
                    await RefreshChatSurfaceAsync();
                    
                    AddToChat("_[Request cancelled]_", true);
                    
                    StatusText.Text = "⚠️ Cancelled";
                    StatusIndicator.Fill = Brushes.Orange;
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    requestTrace.Complete(0, "error");
                    TrackLiveCopilotAsync("turn_failed", requestTrace, new Dictionary<string, string> { ["outcome"] = "error", ["error_code"] = "provider_error" });
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
                    _streamingChatMarkdown = null;
                    _streamMessageId = null;
                    await RefreshChatSurfaceAsync();
                    
                    AddToChat($"❌ **Error:** {UserFacingErrorSanitizer.SanitizeUserFacingError(error)}", true);

                    if (isDesktopAuthFailure)
                    {
                        AddToChat("⚠️ **Desktop session expired**\n\nPlease sign in again to continue.", false);
                        RegenerateButton.IsEnabled = false;
                    }
                    else
                    {
                        PauseInterviewSessionForError("runtime_error_response");
                        AddToChat("⏸️ **Interview paused**\n\nPhantom paused the active interview after this error. The session timer and billing stay frozen until a later response succeeds.", false);
                        RegenerateButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastRetryableQuestion);
                    }
                    
                    StatusText.Text = "✗ Error occurred";
                    StatusIndicator.Fill = Brushes.Red;
                    RestoreChatInputForRetry();
                }
                else
                {
                    requestTrace.Complete(response.Length, "success");
                    TrackLiveCopilotAsync("answer_completed", requestTrace, new Dictionary<string, string>
                    {
                        ["outcome"] = "success", ["elapsed_ms"] = ((int)Math.Max(0, elapsed * 1000)).ToString(),
                        ["buffered_characters"] = response.Length.ToString(), ["model_call"] = _conversationManager.LastModelCallCount.ToString(),
                        ["answer_basis"] = _conversationManager.LastDecision?.AnswerBasis ?? string.Empty,
                        ["question_type"] = _conversationManager.LastDecision?.QuestionType ?? string.Empty
                    });
                    ResumeInterviewSessionAfterSuccess();
                    _conversationManager.CompleteLastAssistantTiming((int)Math.Max(0, elapsed * 1000));
                    Log.WriteLine($"✓ Received response ({response.Length} chars) in {elapsed:F1}s");
                    await FlushStreamingDeltaAsync();
                    _streamingChatMarkdown = null;
                    var finalMarkdown = $"**{aiName}:**\n\n{response}\n\n_Response time: {FormatResponseTime((int)Math.Max(0, elapsed * 1000))}_";
                    await MarkdownHelper.FinalizeAssistantMessageAsync(ChatWebView, _streamMessageId!, finalMarkdown);
                    _chatMessages.Add(new MarkdownHelper.ChatRenderMessage(false, finalMarkdown));
                    _streamMessageId = null;
                    ShowClarificationOptions(_conversationManager.PendingClarificationOptions);
                    _lastRetryableQuestion = null;
                    RegenerateButton.IsEnabled = true;
                    
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
                    
                    var needsClarification = _conversationManager.LastDecision?.Action == LiveCopilotAction.Clarify;
                    StatusText.Text = needsClarification ? "Needs clarification" : $"✓ Response in {elapsed:F1}s | Two-cursor active";
                    StatusIndicator.Fill = needsClarification ? Brushes.Orange : Brushes.LightGreen;
                    
                    if (!needsClarification)
                    {
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                        timer.Tick += (s, args) =>
                        {
                            StatusText.Text = "✓ Protected | Two-cursor system active";
                            timer.Stop();
                        };
                        timer.Start();
                    }
                    RecordInterviewActivity("response_completed");
                    
                }
                if (_attachedScreenshots.Count > 0)
                {
                    Log.WriteLine("✓ Clearing screenshot attachments after successful send");
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
                requestTrace.Complete(0, "cancelled");
                Log.WriteLine("✗ Request cancelled (exception)");
                
                _streamUpdateTimer?.Stop();
                _streamingChatMarkdown = null;
                _streamMessageId = null;
                await RefreshChatSurfaceAsync();
                
                StatusText.Text = "⚠️ Cancelled";
                StatusIndicator.Fill = Brushes.Orange;

                if (_attachedScreenshots.Count > 0)
                {
                    ClearAttachedScreenshot();
                }
            }
            catch (Exception ex)
            {
                requestTrace.Complete(0, "error");
                Log.WriteLine($"✗ Live request failed: {ex.GetType().Name}");
                
                _streamUpdateTimer?.Stop();
                _streamingChatMarkdown = null;
                await RefreshChatSurfaceAsync();
                
                AddToChat($"❌ **Exception:** {UserFacingErrorSanitizer.SanitizeUserFacingError(ex.Message)}", true);
                PauseInterviewSessionForError("runtime_exception");
                AddToChat("⏸️ **Interview paused**\n\nPhantom paused the active interview after this exception. The session timer and billing stay frozen until a later response succeeds.", false);
                
                StatusText.Text = "✗ Exception occurred";
                StatusIndicator.Fill = Brushes.Red;
                RegenerateButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastRetryableQuestion);
                RestoreChatInputForRetry();

                if (_attachedScreenshots.Count > 0)
                {
                    ClearAttachedScreenshot();
                }

            }
            finally
            {
                _streamUpdateTimer?.Stop();
                _streamUpdateTimer = null;
                _streamingChatMarkdown = null;
                _streamMessageId = null;
                
                lock (_streamBuffer)
                {
                    _streamBuffer.Clear();
                }
                
                _isProcessingRequest = false;
                _currentRequestCancellation?.Dispose();
                _currentRequestCancellation = null;
                if (ReferenceEquals(_activeRequestTrace, requestTrace))
                {
                    _activeRequestTrace = null;
                }
                
                RestoreChatInputForRetry();
                FocusInput();
            }
        }

        private void RestoreChatInputForRetry()
        {
            if (IsInterviewStartBlocked() && !CanContinueRestrictedInterview())
            {
                return;
            }

            InputTextBox.IsEnabled = true;
            InputTextBox.IsReadOnly = false;
            if (string.IsNullOrWhiteSpace(InputTextBox.Text)
                || InputTextBox.Text == "Interview start is blocked for this account state.")
            {
                InputTextBox.Text = "Ask me anything...";
                InputTextBox.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            }

            SendButton.IsEnabled = true;
            VoiceButton.IsEnabled = _voiceService?.IsInitialized() == true;
            if (CurrentModelSupportsVision())
            {
                ScreenshotButton.IsEnabled = true;
            }
        }

        private async void StreamUpdateTimer_Tick(object? sender, EventArgs e)
        {
            await FlushStreamingDeltaAsync();
        }

        private async Task FlushStreamingDeltaAsync()
        {
            if (_streamDeltaInFlight || string.IsNullOrWhiteSpace(_streamMessageId))
            {
                return;
            }

            string delta;
            lock (_streamBuffer)
            {
                if (_streamBuffer.Length <= _streamRenderedLength)
                {
                    return;
                }

                delta = _streamBuffer.ToString(_streamRenderedLength, _streamBuffer.Length - _streamRenderedLength);
                _streamRenderedLength = _streamBuffer.Length;
            }

            _streamDeltaInFlight = true;
            try
            {
                await MarkdownHelper.AppendAssistantDeltaAsync(ChatWebView, _streamMessageId, delta);
            }
            finally
            {
                _streamDeltaInFlight = false;
            }
        }

        private void AddToChat(string text, bool isResponse)
        {
            try
            {
                _chatMessages.Add(new MarkdownHelper.ChatRenderMessage(!isResponse, text));
                _ = RefreshChatSurfaceAsync();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error adding to chat: {ex.GetType().Name}");
            }
        }

        private async Task EnsureChatSurfaceReadyAsync()
        {
            if (_chatSurfaceInitialized)
            {
                return;
            }

            await MarkdownHelper.InitializeChatWebViewAsync(ChatWebView);
            if (!_chatCursorBridgeInitialized && ChatWebView.CoreWebView2 != null)
            {
                ChatWebView.CoreWebView2.WebMessageReceived += ChatWebView_WebMessageReceived;
                _chatCursorBridgeInitialized = true;
            }

            _chatSurfaceInitialized = true;
        }

        private async void ChatWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_isHidden)
            {
                return;
            }

            string? message;
            try
            {
                message = e.TryGetWebMessageAsString();
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message.StartsWith("CHAT_STREAM_PAINT:", StringComparison.Ordinal))
            {
                var requestId = message["CHAT_STREAM_PAINT:".Length..];
                if (string.Equals(_activeRequestTrace?.CorrelationId, requestId, StringComparison.Ordinal))
                {
                    _activeRequestTrace?.Mark("first_ui_paint");
                }
                return;
            }

            if (message.StartsWith("CHAT_FIX_MERMAID:", StringComparison.Ordinal))
            {
                await CorrectMermaidSyntaxAsync(message["CHAT_FIX_MERMAID:".Length..]);
                return;
            }

            if (!message.StartsWith("CHAT_CURSOR:", StringComparison.Ordinal))
            {
                return;
            }

            if (!_settings.UseFakeCursor || _settings.ClickThroughEnabled || !IsActive)
            {
                SetChatCursorHidden(false);
                _cursorManager?.SetEmbeddedSurfaceCursorActive(false);
                return;
            }

            var payload = message["CHAT_CURSOR:".Length..];
            if (string.Equals(payload, "enter", StringComparison.Ordinal))
            {
                _cursorManager?.ActivateCustomCursor();
                _cursorManager?.SetEmbeddedSurfaceCursorActive(true);
                SetChatCursorHidden(true);
                return;
            }

            if (string.Equals(payload, "leave", StringComparison.Ordinal))
            {
                SetChatCursorHidden(false);
                _cursorManager?.SetEmbeddedSurfaceCursorActive(false);
                return;
            }

            if (!payload.StartsWith("move:", StringComparison.Ordinal))
            {
                return;
            }

            var coordinates = payload["move:".Length..].Split(':');
            if (coordinates.Length != 2 ||
                !double.TryParse(coordinates[0], out var x) ||
                !double.TryParse(coordinates[1], out var y))
            {
                return;
            }

            var webViewPoint = new Point(x, y);
            var windowPoint = ChatWebView.TranslatePoint(webViewPoint, this);
            _cursorManager?.ActivateCustomCursor();
            _cursorManager?.SetEmbeddedSurfaceCursorActive(true);
            _cursorManager?.UpdateCustomCursorPosition(windowPoint);
            SetChatCursorHidden(true);
        }

        private async Task CorrectMermaidSyntaxAsync(string requestJson)
        {
            string diagramId;
            string source;
            try
            {
                using var request = JsonDocument.Parse(requestJson);
                diagramId = request.RootElement.GetProperty("diagramId").GetString() ?? string.Empty;
                source = request.RootElement.GetProperty("source").GetString() ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(diagramId) || string.IsNullOrWhiteSpace(source))
            {
                Log.WriteLine("mermaid:correction:ignored reason=invalid_payload");
                return;
            }

            if (_isProcessingRequest || _mermaidCorrectionInFlight || _currentAI == null || !_currentAI.IsConfigured())
            {
                Log.WriteLine($"mermaid:correction:blocked processing={_isProcessingRequest} in_flight={_mermaidCorrectionInFlight} configured={_currentAI?.IsConfigured() == true}");
                await MarkdownHelper.SetMermaidCorrectionStateAsync(
                    ChatWebView,
                    diagramId,
                    false,
                    _isProcessingRequest ? "Wait for response" : "Correct syntax");
                return;
            }

            _mermaidCorrectionInFlight = true;
            Log.WriteLine($"mermaid:correction:start id={diagramId} chars={source.Length}");
            try
            {
                var correctionMessages = new List<ConversationMessage>
                {
                    new()
                    {
                        Role = "system",
                        Content = "You repair Mermaid syntax only. Preserve every node, label, edge, direction, and meaning. Return only corrected Mermaid source without Markdown fences or explanation."
                    },
                    new()
                    {
                        Role = "user",
                        Content = $"Correct this Mermaid code without changing its content or design:\n\n{source}"
                    }
                };
                var service = _currentAI;
                var response = await Task.Run(() => service.SendMessageAsync(correctionMessages));
                if (response.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(response[6..].Trim());
                }

                var corrected = ExtractCorrectedMermaidSource(response);
                if (string.IsNullOrWhiteSpace(corrected))
                {
                    throw new InvalidOperationException("The model did not return Mermaid source.");
                }

                await MarkdownHelper.ReplaceChatMermaidAsync(ChatWebView, diagramId, corrected);
                Log.WriteLine($"mermaid:correction:success id={diagramId} chars={corrected.Length} changed={!string.Equals(source.Trim(), corrected.Trim(), StringComparison.Ordinal)}");
                StatusText.Text = "✓ Diagram syntax corrected";
                StatusIndicator.Fill = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"mermaid:correction:failed id={diagramId} error={ex.Message}");
                await MarkdownHelper.SetMermaidCorrectionStateAsync(ChatWebView, diagramId, false, "Try correction again");
                StatusText.Text = "⚠️ Diagram correction failed";
                StatusIndicator.Fill = Brushes.Orange;
            }
            finally
            {
                _mermaidCorrectionInFlight = false;
            }
        }

        private static string ExtractCorrectedMermaidSource(string response)
        {
            if (MarkdownHelper.TryExtractFirstMermaidBlock(response, out var fencedMermaid))
            {
                return fencedMermaid;
            }

            var corrected = response.Trim();
            if (corrected.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = corrected.IndexOf('\n');
                var closingFence = corrected.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLineEnd >= 0 && closingFence > firstLineEnd)
                {
                    corrected = corrected[(firstLineEnd + 1)..closingFence].Trim();
                }
            }

            return corrected.StartsWith("mermaid\n", StringComparison.OrdinalIgnoreCase)
                ? corrected[8..].Trim()
                : corrected;
        }

        private void SetChatCursorHidden(bool hidden)
        {
            if (!_chatSurfaceInitialized || _chatCursorHidden == hidden)
            {
                return;
            }

            _chatCursorHidden = hidden;
            _ = MarkdownHelper.SetChatCursorHiddenAsync(ChatWebView, hidden);
        }

        private IReadOnlyList<MarkdownHelper.ChatRenderMessage> BuildDisplayedChatMessages()
        {
            if (!string.IsNullOrWhiteSpace(_streamingChatMarkdown))
            {
                return _chatMessages
                    .Concat(new[] { new MarkdownHelper.ChatRenderMessage(false, _streamingChatMarkdown) })
                    .ToList();
            }

            if (_chatMessages.Count > 0)
            {
                return _chatMessages;
            }

            return new[]
            {
                new MarkdownHelper.ChatRenderMessage(false, MarkdownHelper.GetWelcomeMarkdown())
            };
        }

        private async Task RefreshChatSurfaceAsync()
        {
            await _chatRenderLock.WaitAsync();
            try
            {
                await EnsureChatSurfaceReadyAsync();
                await MarkdownHelper.RenderChatTranscriptAsync(ChatWebView, BuildDisplayedChatMessages());
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Chat surface render failed: {ex.GetType().Name}");
            }
            finally
            {
                _chatRenderLock.Release();
                if (!_isProcessingRequest)
                {
                    FocusInput();
                }
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
            _interviewLockHeartbeatCount = 0;
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
                StatusText.Text = $"⚠️ {UserFacingErrorSanitizer.SanitizeUserFacingError(heartbeat.Title)}";
                StatusIndicator.Fill = Brushes.Orange;
                _telemetryService.Track("lock", "interview_lock_heartbeat_failed", new Dictionary<string, string>
                {
                    ["title"] = heartbeat.Title,
                    ["message"] = heartbeat.Message
                });
                return;
            }

            Log.WriteLine($"Interview lock heartbeat refreshed until {heartbeat.LockExpiresAtUtc:O}");
            _interviewLockHeartbeatCount++;
            if (_interviewLockHeartbeatCount == 1 || _interviewLockHeartbeatCount % 5 == 0)
            {
                _telemetryService.Track("lock", "interview_lock_heartbeat", new Dictionary<string, string>
                {
                    ["expires_at"] = heartbeat.LockExpiresAtUtc?.ToString("O") ?? string.Empty,
                    ["heartbeat_count"] = _interviewLockHeartbeatCount.ToString()
                });
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            Log.WriteLine("Send button clicked");
            await SendMessage();
        }

        private void ShowClarificationOptions(IReadOnlyList<ConversationManager.ClarificationOption> options)
        {
            ClarificationOptionsPanel.Children.Clear();
            if (options.Count == 0)
            {
                ClarificationOptionsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var option in options)
            {
                var button = new Button
                {
                    Content = option.Label,
                    Tag = option.Question,
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(12, 6, 12, 6),
                    Style = (Style)FindResource("ButtonStyle")
                };
                button.Click += async (_, _) =>
                {
                    InputTextBox.Text = (string)button.Tag;
                    await SendMessage();
                };
                ClarificationOptionsPanel.Children.Add(button);
            }
            ClarificationOptionsPanel.Visibility = Visibility.Visible;
        }

        private async void RegenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessingRequest || _conversationManager == null)
            {
                return;
            }

            RestoreChatInputForRetry();
            var question = _conversationManager.RemoveLastExchangeForRegeneration();
            if (string.IsNullOrWhiteSpace(question))
            {
                question = _lastRetryableQuestion;
            }

            if (string.IsNullOrWhiteSpace(question))
            {
                RegenerateButton.IsEnabled = false;
                return;
            }

            if (_chatMessages.Count > 0 && !_chatMessages[^1].IsUser)
            {
                _chatMessages.RemoveAt(_chatMessages.Count - 1);
            }
            if (_chatMessages.Count > 0 && _chatMessages[^1].IsUser)
            {
                _chatMessages.RemoveAt(_chatMessages.Count - 1);
            }

            // Drop trailing error/status system notes that followed a failed turn.
            while (_chatMessages.Count > 0
                && !_chatMessages[^1].IsUser
                && (_chatMessages[^1].Markdown.Contains("❌ **Error:**", StringComparison.Ordinal)
                    || _chatMessages[^1].Markdown.Contains("⏸️ **Interview paused**", StringComparison.Ordinal)
                    || _chatMessages[^1].Markdown.Contains("❌ **Exception:**", StringComparison.Ordinal)))
            {
                _chatMessages.RemoveAt(_chatMessages.Count - 1);
            }

            await RefreshChatSurfaceAsync();
            InputTextBox.Text = question;
            InputTextBox.Foreground = Brushes.White;
            await SendMessage(captureQuestion: false);
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
                _chatMessages.Clear();
                _streamingChatMarkdown = null;
                _ = RefreshChatSurfaceAsync();
                
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
                _chatMessages.Clear();
                _streamingChatMarkdown = null;
                _ = RefreshChatSurfaceAsync();
                
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
                CollapsedHeaderMicButton.IsEnabled = false;
                CollapsedHeaderMicButton.Opacity = 0.5;
                VoiceStatusText.Text = "Disabled";
                return;
            }

            try
            {
                Log.WriteLine("Creating browser-based VoiceInputService...");
                var session = _authSessionRepository.Load();
                var useManagedSpeech = IsPremiumAccount();
                var useByoCloudSpeech = IsByoAccount()
                    && string.Equals(_settings.SpeechRecognitionMode, "Cloud", StringComparison.OrdinalIgnoreCase);
                SpeechTranscriptionClient? cloudSpeech = null;
                if ((useManagedSpeech || useByoCloudSpeech) && session?.IsAuthenticated == true)
                {
                    cloudSpeech = new SpeechTranscriptionClient(
                        _settings,
                        _hostedRuntimeOptions.DesktopBackendBaseUrl,
                        session.AccessToken,
                        useManagedSpeech);
                }
                _voiceService = new VoiceInputService(
                    cloudSpeech == null ? null : cloudSpeech.TranscribePcm16Async,
                    useManagedSpeech || _settings.AutoFallbackToNativeSpeech,
                    cloudSpeech == null ? null : cloudSpeech.ProbeReachabilityAsync);
                
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
                    CollapsedHeaderMicButton.IsEnabled = true;
                    CollapsedHeaderMicButton.Opacity = 1.0;
                    SetVoiceButtonVisualState(isListening: false);
                    VoiceStatusText.Text = "Ready";
                    VoiceStatusText.Foreground = Brushes.LightGreen;
                }
                else
                {
                    Log.WriteLine("✗ Voice service initialization failed");
                    VoiceButton.IsEnabled = false;
                    VoiceButton.Opacity = 0.5;
                    CollapsedHeaderMicButton.IsEnabled = false;
                    CollapsedHeaderMicButton.Opacity = 0.5;
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
                Log.WriteLine($"✗ Voice init exception: {ex.GetType().Name}");
                
                VoiceButton.IsEnabled = false;
                VoiceButton.Opacity = 0.5;
                CollapsedHeaderMicButton.IsEnabled = false;
                CollapsedHeaderMicButton.Opacity = 0.5;
                VoiceStatusText.Text = "Error";
                VoiceStatusText.Foreground = Brushes.Red;
                
                InvisibleMessageBox.Show(
                    $"Voice initialization error:\n\n{UserFacingErrorSanitizer.SanitizeUserFacingError(ex.Message)}", "Error");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════");
        }

        private void OnSpeechRecognized(object? sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                Log.WriteLine("─────────────────────────────────────────────────────");
                Log.WriteLine($"Speech finalized length_bucket={LengthBucket(text.Length)}");
                LiveRequestTrace.Current?.Mark("transcript_finalized");
                
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
                
                if (_autoSendAfterVoice)
                {
                    Log.WriteLine("  Auto-send active - starting/restarting completion timer");
                    StartVoiceCompletionTimer();
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
                else if (status == "Ready"
                    || status.Contains("recognized", StringComparison.OrdinalIgnoreCase)
                    || status.EndsWith("ready", StringComparison.OrdinalIgnoreCase))
                {
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144));
                }
                else
                {
                    VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                }

                // After cloud→native fallback (or cloud failure while waiting), keep auto-send alive.
                if (_autoSendAfterVoice
                    && _voiceService != null
                    && !_voiceService.IsListening()
                    && (status.Contains("Native fallback", StringComparison.OrdinalIgnoreCase)
                        || status.Contains("Cloud speech unavailable", StringComparison.OrdinalIgnoreCase)
                        || status.Contains("recognized", StringComparison.OrdinalIgnoreCase)))
                {
                    StartVoiceCompletionTimer();
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
                var wasCloudSpeech = _voiceService.IsCloudMode();
                _autoSendAfterVoice = _settings.AutoSendAfterVoiceStopEnabled;
                
                _voiceService.StopListening();
                SetVoiceButtonVisualState(isListening: false);
                VoiceStatusText.Text = "Processing speech...";
                VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0));
                
                Log.WriteLine("  ✓ Stopped listening - waiting for ALL speech to complete");

                if (_autoSendAfterVoice)
                {
                    if (!wasCloudSpeech)
                    {
                        Log.WriteLine("  Auto-send enabled - starting completion timer");
                        StartVoiceCompletionTimer();
                        Log.WriteLine("  Started 350 ms completion timer");
                    }
                    else
                    {
                        Log.WriteLine("  Waiting for cloud transcription before auto-send");
                    }
                }
                else
                {
                    _voiceCompletionTimer?.Stop();
                    StatusText.Text = "✓ Speech captured - Press Send to continue";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    VoiceStatusText.Text = "Ready";
                    VoiceStatusText.Foreground = Brushes.LightGreen;
                    Log.WriteLine("  Auto-send disabled - waiting for manual send");
                }
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
                SetVoiceButtonVisualState(isListening: true);
                VoiceStatusText.Text = "🎙️ Getting microphone ready";
                VoiceStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 215, 0));
                
                Log.WriteLine("  ✓ Started listening");
            }
            
            Log.WriteLine("═══════════════════════════════════════════════");
        }

        private void StartVoiceCompletionTimer()
        {
            _voiceCompletionTimer?.Stop();

            _voiceCompletionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
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
                    Log.WriteLine($"  ✓ Speech fully completed length_bucket={LengthBucket(InputTextBox.Text.Length)}");

                    StatusText.Text = "✓ Speech captured - Sending automatically...";
                    StatusIndicator.Fill = Brushes.LightGreen;
                    VoiceStatusText.Text = "Sending...";
                    VoiceStatusText.Foreground = Brushes.LightGreen;

                    _nextRequestIsVoice = true;
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
        }

        private void SetVoiceButtonVisualState(bool isListening)
        {
            var content = isListening ? "⏹️" : "🎤";
            var background = isListening
                ? new SolidColorBrush(Color.FromArgb(80, 255, 0, 0))
                : new SolidColorBrush(Color.FromArgb(80, 0, 170, 0));

            VoiceButton.Content = content;
            VoiceButton.Background = background;
            CollapsedHeaderMicButton.Content = content;
            CollapsedHeaderMicButton.Background = background;
        }

        // ═══════════════════════════════════════════════════════════════
        // DEBUG / CONTEXT
        // ═══════════════════════════════════════════════════════════════

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
                    $"Total tokens: ~{totalTokens}",
                    "Context Viewer");
            }
            catch (Exception ex)
            {
                InvisibleMessageBox.Show($"Error viewing context: {ex.Message}", "Error");
            }
        }

        private void CopyChatButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var messages = _conversationManager?.GetAllMessages()
                    .Where(message => !string.IsNullOrWhiteSpace(message.Content))
                    .ToList() ?? new List<ConversationMessage>();
                if (messages.Count == 0)
                {
                    StatusText.Text = "No chat to copy";
                    return;
                }

                var transcript = string.Join("\n\n", messages.Select(message =>
                {
                    var role = message.Role == "assistant" ? "PHANTOM" : "YOU";
                    var timestamp = message.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                    var timing = message.Role == "assistant" && message.ResponseTimeMs.HasValue
                        ? $" • Response time: {FormatResponseTime(message.ResponseTimeMs.Value)}"
                        : string.Empty;
                    return $"{role} • {timestamp}{timing}\n{message.Content}";
                }));
                Clipboard.SetText(transcript);
                StatusText.Text = "✓ Chat copied with timings";
                Log.WriteLine($"Chat transcript copied message_count={messages.Count}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Failed to copy chat error_code={ex.GetType().Name}");
                StatusText.Text = "✗ Failed to copy chat";
            }
        }

        private static string FormatResponseTime(int milliseconds)
            => milliseconds < 1000 ? $"{milliseconds} ms" : $"{milliseconds / 1000d:F1} s";

        private void TrackLiveCopilotAsync(string eventName, LiveRequestTrace trace, Dictionary<string, string> fields)
        {
            fields["session_id"] = trace.SessionId;
            fields["turn_id"] = trace.TurnId;
            fields["operation_id"] = trace.OperationId;
            fields["mode"] = trace.Mode;
            fields["delivery_style"] = trace.DeliveryStyle;
            fields["execution_lane"] = trace.ExecutionLane;
            fields["usage_source"] = trace.UsageSource;
            _ = Task.Run(() => _telemetryService.Track("live_copilot", eventName, fields));
        }

        // ═══════════════════════════════════════════════════════════════
        // SETTINGS (MULTI-PAGE NAVIGATION)
        // ═══════════════════════════════════════════════════════════════

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            DisableClickThroughForInteraction("settings_opened");
            if (_isHidden)
            {
                Log.WriteLine("Settings requested while hidden - restoring window before opening settings");
                ToggleVisibility();
            }

            Log.WriteLine("Settings button clicked - switching to settings page");
            RefreshAccountSnapshot();
            Activate();

            _settingsPage = new SettingsPage(_accountSnapshot, _settings);
            _settingsPage.SettingsClosed += OnSettingsClosed;
            SettingsPageHost.Content = _settingsPage;

            // Switch pages
            ChatPageContainer.Visibility = Visibility.Collapsed;
            SettingsPageContainer.Visibility = Visibility.Visible;
        }


        private void OnSettingsClosed(object? sender, SettingsCloseResult result)
        {
            Log.WriteLine($"Settings closed - saved: {result.Saved}, context reset required: {result.ContextResetRequired}");

            if (result.Saved)
            {
                var oldProvider = _currentAI?.GetProviderName() ?? "None";
                var oldModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "unknown";
                var oldUseFakeCursor = _settings.UseFakeCursor;
                _forcedManagedExtensionProviderId = null;
                
                // Reload settings
                _settings = SettingsManager.Load();
                _managedCatalogRefreshTask = RefreshDesktopCatalogsAsync(force: false);
                RefreshAccountSnapshot();
                UpdateCreditIndicator();
                HeaderOpacitySlider.Value = _settings.WindowOpacity;
                ApplyWindowOpacity(_settings.WindowOpacity, persistSetting: false);
                ApplyClickThroughState(_settings.ClickThroughEnabled);
                UpdateLegacyFallbackButtonState();
                UpdateClickThroughButtonState();
                
                Log.WriteLine($"Model before settings reload: {oldModel}");
                
                // Reinitialize AI with new settings
                InitializeAI();
                ApplyAccountTierChrome();

                if (_conversationManager != null)
                {
                    var selectedPack = _contextPackService.GetSelectedPack();
                    ApplySelectedContextPackToConversation(selectedPack, result.ContextResetRequired);
                }

                if (oldUseFakeCursor != _settings.UseFakeCursor)
                {
                    _cursorManager?.Dispose();
                    _cursorManager = new CursorManager(
                        this,
                        CustomCursorCanvas,
                        _settings.UseFakeCursor,
                        _settings.FakeCursorSize
                    );
                    _cursorManager.SetClickThroughActive(_settings.ClickThroughEnabled);
                    Log.WriteLine("✓ Cursor manager reinitialized with new settings");
                }
                else
                {
                    _cursorManager?.UpdateFakeCursorSize(_settings.FakeCursorSize);
                }

                var newProvider = _currentAI?.GetProviderName() ?? "None";
                var newModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "unknown";
                
                // Check if model actually changed
                bool modelChanged = oldModel != newModel;
                bool providerChanged = oldProvider != newProvider;
                
                if (_settings.VoiceInputEnabled)
                {
                    Log.WriteLine("Applying voice recognizer settings");
                    _voiceService?.Dispose();
                    _voiceService = null;
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
                    var historyNotice = result.ContextResetRequired
                        ? "The active context changed, so the current conversation was reset."
                        : "Your conversation history has been preserved!";

                    InvisibleMessageBox.Show(
                        $"✓ Settings Applied\n\n" +
                        $"Provider: {newProvider}\n" +
                        $"Model: {GetModelDisplayName(_settings.SelectedAI, newModel)}\n\n" +
                        historyNotice,
                        "Settings Saved"
                    );
                }
                else if (result.ContextResetRequired)
                {
                    InvisibleMessageBox.Show(
                        "Settings saved. The active context pack changed, so the current conversation was reset.",
                        "Settings Saved");
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
            _debugLogger.SetUiCollectionEnabled(false);

            this.Activate();
            FocusInput();
        }

        private void ApplySelectedContextPackToConversation(ContextPack selectedPack, bool resetConversation)
        {
            if (_conversationManager == null)
            {
                return;
            }

            if (resetConversation)
            {
                _conversationManager.UpdateResume(string.Empty, string.Empty);
                _conversationManager.ClearJobDescription();
                _conversationManager.ClearConversation();

                if (!string.IsNullOrWhiteSpace(selectedPack.ResumeText))
                {
                    _conversationManager.UpdateResume(selectedPack.ResumeText, string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(selectedPack.JobDescriptionText))
                {
                    _conversationManager.UpdateJobDescription(selectedPack.JobDescriptionText, string.Empty);
                }

                SettingsManager.ClearConversationCache();
                _chatMessages.Clear();
                _streamingChatMarkdown = null;
                _ = RefreshChatSurfaceAsync();
                UpdateTokenCounter();
                Log.WriteLine("✓ Active context reapplied and conversation reset");
                _ = PrepareAndPersistContextAsync();
                return;
            }

            _conversationManager.UpdateResume(selectedPack.ResumeText, selectedPack.ResumeSummary);

            if (string.IsNullOrWhiteSpace(selectedPack.JobDescriptionText))
            {
                _conversationManager.ClearJobDescription();
            }
            else
            {
                _conversationManager.UpdateJobDescription(selectedPack.JobDescriptionText, selectedPack.JobDescriptionSummary);
            }

            Log.WriteLine("✓ Resume and job description updated in conversation manager");
            _ = PrepareAndPersistContextAsync();
        }

        private async Task PrepareAndPersistContextAsync()
        {
            if (_conversationManager == null)
            {
                return;
            }

            try
            {
                await _conversationManager.WarmLiveContextAsync();
                var selectedPack = _contextPackService.GetSelectedPack();
                selectedPack.ResumeSummary = _conversationManager.GetResumeSummary();
                selectedPack.JobDescriptionSummary = _conversationManager.GetJobDescriptionSummary();
                _contextPackService.SaveSelectedPack(selectedPack);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Context preparation will retry later: {ex.GetType().Name}");
            }
        }

        private void UpdateLegacyFallbackButtonState()
        {
            if (LegacyFallbackButton == null)
            {
                return;
            }

            var hasLegacyFallbackPath = !string.IsNullOrWhiteSpace(_settings.LegacyFallbackAppPath);
            LegacyFallbackButton.Visibility = _accountSnapshot?.CanUseDesktopPowerFeatures == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            LegacyFallbackButton.Opacity = hasLegacyFallbackPath ? 1.0 : 0.55;
            LegacyFallbackButton.ToolTip = null;
        }

        private void LegacyFallbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_accountSnapshot?.CanUseDesktopPowerFeatures != true)
            {
                return;
            }

            try
            {
                var legacyFallbackAppPath = _settings.LegacyFallbackAppPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(legacyFallbackAppPath))
                {
                    InvisibleMessageBox.Show(
                        "Set the legacy app path in Settings first.",
                        "Legacy App Not Configured");
                    return;
                }

                if (!File.Exists(legacyFallbackAppPath))
                {
                    InvisibleMessageBox.Show(
                        "The configured legacy app path no longer exists.\n\nUpdate it in Settings.",
                        "Legacy App Missing");
                    return;
                }

                Log.WriteLine($"Launching legacy fallback app: {legacyFallbackAppPath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = legacyFallbackAppPath,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(legacyFallbackAppPath) ?? Environment.CurrentDirectory
                });

                Log.WriteLine("Legacy fallback app started. Closing hosted app.");
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Failed to launch legacy fallback app: {ex.Message}");
                InvisibleMessageBox.Show(
                    $"Could not launch the legacy app.\n\n{ex.Message}",
                    "Legacy App Launch Failed");
            }
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

        private void HeaderOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }

            ApplyWindowOpacity(e.NewValue, persistSetting: true);
        }

        private void ApplyWindowOpacity(double opacity, bool persistSetting)
        {
            const double minOpacity = 0.30;
            const double maxOpacity = 1.20;
            var clampedOpacity = Math.Max(minOpacity, Math.Min(maxOpacity, opacity));
            var normalizedOpacity = (clampedOpacity - minOpacity) / (maxOpacity - minOpacity);

            OuterShadowBorder.Opacity = 0.24 + (normalizedOpacity * 0.76);
            MainContentGrid.Opacity = 0.90 + (normalizedOpacity * 0.10);

            var backgroundAlpha = (byte)Math.Round(92 + (normalizedOpacity * 132));
            WindowChromeBorder.Background = new SolidColorBrush(Color.FromArgb(backgroundAlpha, 0, 0, 0));
            WindowChromeBorder.BorderBrush = Brushes.Transparent;

            if (persistSetting)
            {
                _settings.WindowOpacity = clampedOpacity;
                SettingsManager.Save(_settings);
            }
        }

        private void ChatCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            SetChatSectionCollapsed(!_isChatSectionCollapsed);
        }

        private void SetChatSectionCollapsed(bool collapsed)
        {
            _isChatSectionCollapsed = collapsed;
            ChatSectionContainer.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            ChatSectionRow.Height = collapsed ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            ChatCollapseButton.Content = collapsed ? "▾" : "▴";
            CollapsedHeaderMicButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            MinHeight = collapsed ? CollapsedWindowMinHeight : ExpandedWindowMinHeight;
            MainContentGrid.Margin = collapsed
                ? new Thickness(12, 8, 12, 6)
                : new Thickness(18, 14, 18, 12);
            TitleBarGrid.Margin = collapsed
                ? new Thickness(0, 0, 0, 2)
                : new Thickness(0, 0, 0, 10);

            if (collapsed)
            {
                Height = CollapsedWindowMinHeight;
            }
            else if (Height < 500)
            {
                Height = 500;
            }
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Tag is not string tag)
            {
                return;
            }

            var minWidth = MinWidth;
            var minHeight = MinHeight;
            var currentLeft = Left;
            var currentTop = Top;
            var currentWidth = Width;
            var currentHeight = Height;

            if (tag.Contains("Left", StringComparison.Ordinal))
            {
                var nextWidth = Math.Max(minWidth, currentWidth - e.HorizontalChange);
                var widthDelta = currentWidth - nextWidth;
                Width = nextWidth;
                Left = currentLeft + widthDelta;
            }

            if (tag.Contains("Right", StringComparison.Ordinal))
            {
                Width = Math.Max(minWidth, currentWidth + e.HorizontalChange);
            }

            if (tag.Contains("Top", StringComparison.Ordinal))
            {
                var nextHeight = Math.Max(minHeight, currentHeight - e.VerticalChange);
                var heightDelta = currentHeight - nextHeight;
                Height = nextHeight;
                Top = currentTop + heightDelta;
            }

            if (tag.Contains("Bottom", StringComparison.Ordinal))
            {
                Height = Math.Max(minHeight, currentHeight + e.VerticalChange);
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

                // Ctrl + Alt + ` - disable click-through first; otherwise toggle visibility
                if (vkCode == NativeMethods.VK_OEM_3)
                {
                    if (NativeMethods.IsKeyPressed(NativeMethods.VK_CONTROL) && 
                        NativeMethods.IsKeyPressed(NativeMethods.VK_MENU))
                    {
                        Log.WriteLine("Hotkey: Ctrl+Alt+` pressed");
                        Dispatcher.Invoke(() => HandleVisibilityShortcut("ctrl_alt_backtick"));
                        return (IntPtr)1;
                    }
                }

                // F13 - same hardware-key behavior as Ctrl+Alt+`
                if (vkCode == NativeMethods.VK_F13)
                {
                    Log.WriteLine("Hotkey: F13 pressed");
                    Dispatcher.Invoke(() => HandleVisibilityShortcut("f13"));
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

                // Ctrl + Alt + = - Settings
                if (vkCode == NativeMethods.VK_OEM_PLUS)
                {
                    if (NativeMethods.IsKeyPressed(NativeMethods.VK_CONTROL) &&
                        NativeMethods.IsKeyPressed(NativeMethods.VK_MENU))
                    {
                        Log.WriteLine("Hotkey: Ctrl+Alt+= pressed");
                        Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
                        return (IntPtr)1;
                    }
                }

                // Ctrl + Alt + D - Open Settings (debug logs live there)
                if (vkCode == (int)Key.D)
                {
                    if (NativeMethods.IsKeyPressed(NativeMethods.VK_CONTROL) && 
                        NativeMethods.IsKeyPressed(NativeMethods.VK_MENU))
                    {
                        Log.WriteLine("Hotkey: Ctrl+Alt+D pressed");
                        Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
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
                DisableClickThroughForInteraction("window_restored");
                this.Show();
                this.Opacity = 1.0;
                IsHitTestVisible = true;
                _isHidden = false;
                
                this.Activate();
                this.Topmost = true;
                
                FocusInput();
                _cursorManager?.EnsureLiveCursorAbove();
                
                Log.WriteLine("✓ Window shown and focused");
            }
            else
            {
                Log.WriteLine("Hiding window...");
                CloseCurrentDropdownMenu();
                SetChatCursorHidden(false);
                this.Opacity = 0.0;
                IsHitTestVisible = false;
                _isHidden = true;
                
                _cursorManager?.DeactivateCustomCursor();
                // ponytail: WebView2 is its own HWND; opacity alone leaves it visible, so hide the whole window.
                this.Hide();
                
                Log.WriteLine("✓ Window hidden");
            }
        }

        private void HandleVisibilityShortcut(string source)
        {
            if (_settings.ClickThroughEnabled)
            {
                DisableClickThroughForInteraction(source);
                if (_isHidden)
                {
                    ToggleVisibility();
                }
                else
                {
                    Show();
                    Activate();
                    FocusInput();
                }
                return;
            }

            ToggleVisibility();
        }

        private void ClickThroughButton_Click(object sender, RoutedEventArgs e)
        {
            CloseCurrentDropdownMenu();
            _settings.ClickThroughEnabled = !_settings.ClickThroughEnabled;
            SettingsManager.Save(_settings);
            ApplyClickThroughState(_settings.ClickThroughEnabled);
            UpdateClickThroughButtonState();
            Log.WriteLine($"Click-through {(_settings.ClickThroughEnabled ? "enabled" : "disabled")} reason=header_button");
        }

        private void ApplyClickThroughState(bool enabled)
        {
            if (enabled)
            {
                _cursorManager?.SetClickThroughActive(true);
                WindowProtection.SetClickThrough(_windowHandle, true);
                return;
            }

            WindowProtection.SetClickThrough(_windowHandle, false);
            _cursorManager?.SetClickThroughActive(false);
        }

        private void UpdateClickThroughButtonState()
        {
            if (ClickThroughButton == null) return;

            var enabled = _settings.ClickThroughEnabled;
            ClickThroughButton.Background = new SolidColorBrush(enabled
                ? Color.FromArgb(96, 34, 197, 94)
                : Color.FromArgb(80, 80, 80, 80));
            ClickThroughButton.BorderBrush = new SolidColorBrush(enabled
                ? Color.FromArgb(210, 74, 222, 128)
                : Color.FromArgb(144, 255, 255, 255));
            System.Windows.Automation.AutomationProperties.SetName(
                ClickThroughButton,
                enabled ? "Click-through enabled. Press Control Alt backtick to disable." : "Enable click-through");
        }

        private void DisableClickThroughForInteraction(string reason)
        {
            if (!_settings.ClickThroughEnabled) return;
            _settings.ClickThroughEnabled = false;
            SettingsManager.Save(_settings);
            ApplyClickThroughState(false);
            UpdateClickThroughButtonState();
            Log.WriteLine($"Click-through disabled reason={reason}");
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
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                
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
                var restartedProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--restart-main-window",
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });

                if (restartedProcess == null)
                {
                    throw new InvalidOperationException("Windows did not create the replacement Phantom process.");
                }

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
            CaptureScreenshot();
        }

        private void ClearAttachedScreenshotsButton_Click(object sender, RoutedEventArgs e)
        {
            ClearAttachedScreenshot();
        }

        private void RefreshAttachedScreenshotsStrip()
        {
            if (AttachedScreenshotsBorder == null || AttachedScreenshotsPanel == null)
            {
                return;
            }

            AttachedScreenshotsPanel.Children.Clear();
            if (_attachedScreenshots.Count == 0)
            {
                AttachedScreenshotsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            AttachedScreenshotsBorder.Visibility = Visibility.Visible;
            if (AttachedScreenshotsCountText != null)
            {
                AttachedScreenshotsCountText.Text = $"{_attachedScreenshots.Count}/{MaxAttachedScreenshots}";
            }

            for (var index = 0; index < _attachedScreenshots.Count; index++)
            {
                var captureIndex = index;
                var thumb = new Border
                {
                    Width = 56,
                    Height = 38,
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0, 170, 255)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ClipToBounds = true
                };

                thumb.Child = new System.Windows.Controls.Image
                {
                    Source = _attachedScreenshots[index].Image,
                    Stretch = Stretch.UniformToFill
                };
                thumb.MouseLeftButtonDown += (_, _) => PreviewScreenshot(captureIndex);

                var remove = new Button
                {
                    Content = "×",
                    Width = 18,
                    Height = 18,
                    FontSize = 11,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, -6, -6, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromArgb(220, 40, 40, 40)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"Remove screenshot {captureIndex + 1}"
                };
                remove.Click += (_, _) => RemoveAttachedScreenshotAt(captureIndex);

                var cell = new Grid { Width = 56, Height = 38, Margin = new Thickness(0, 0, 8, 0) };
                cell.Children.Add(thumb);
                cell.Children.Add(remove);
                AttachedScreenshotsPanel.Children.Add(cell);
            }
        }

        private void PreviewScreenshot(int index = 0)
        {
            if (_attachedScreenshots.Count == 0 || index < 0 || index >= _attachedScreenshots.Count)
            {
                return;
            }

            var previewWindow = new Window
            {
                Title = "",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                Width = 720,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            previewWindow.Loaded += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(previewWindow).Handle;
                WindowProtection.ApplyProtection(hwnd);
            };

            var image = new System.Windows.Controls.Image
            {
                Source = _attachedScreenshots[index].Image,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(16)
            };

            var close = new Button
            {
                Content = "Close",
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(14, 6, 14, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            close.Click += (_, _) => previewWindow.Close();

            var panel = new DockPanel { LastChildFill = true };
            var title = new TextBlock
            {
                Text = $"Screenshot {index + 1}/{_attachedScreenshots.Count}",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(16, 14, 16, 8)
            };
            DockPanel.SetDock(title, Dock.Top);
            DockPanel.SetDock(close, Dock.Bottom);
            panel.Children.Add(title);
            panel.Children.Add(close);
            panel.Children.Add(image);

            previewWindow.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 20, 20, 20)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 0, 170, 255)),
                BorderThickness = new Thickness(1.5),
                Child = panel
            };
            previewWindow.ShowDialog();
        }

        private void CaptureScreenshot()
        {
            try
            {
                if (_attachedScreenshots.Count >= MaxAttachedScreenshots)
                {
                    StatusText.Text = "⚠️ Max 3 screenshots attached";
                    StatusIndicator.Fill = Brushes.Orange;
                    InvisibleMessageBox.Show(
                        "You can attach up to 3 screenshots per message.\n\nRemove one before capturing another.",
                        "Screenshot Limit");
                    return;
                }

                ResetEmbeddedCursorState();
                _cursorManager?.DeactivateCustomCursor();
                this.Hide();
                System.Threading.Thread.Sleep(200);

                var screenshot = ScreenshotCapture.CaptureScreenshot();
                this.Show();
                this.Activate();
                _cursorManager?.EnsureLiveCursorAbove();

                if (screenshot != null)
                {
                    _attachedScreenshots.Add(new AttachedScreenshotItem { Image = screenshot });
                    Log.WriteLine($"✓ Screenshot attached ({_attachedScreenshots.Count}/{MaxAttachedScreenshots})");
                    UpdateScreenshotButtonChrome();
                    StatusText.Text = $"✓ Screenshot {_attachedScreenshots.Count}/{MaxAttachedScreenshots} attached";
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
                Log.WriteLine($"Error capturing screenshot: {ex.GetType().Name}");
                this.Show();
            }
        }

        private void RemoveAttachedScreenshotAt(int index)
        {
            if (index < 0 || index >= _attachedScreenshots.Count)
            {
                return;
            }

            _attachedScreenshots.RemoveAt(index);
            UpdateScreenshotButtonChrome();
            StatusText.Text = _attachedScreenshots.Count == 0
                ? "Screenshot removed"
                : $"Screenshots: {_attachedScreenshots.Count}/{MaxAttachedScreenshots}";
            StatusIndicator.Fill = Brushes.Orange;
        }

        private void ClearAttachedScreenshot()
        {
            if (_attachedScreenshots.Count > 0)
            {
                Log.WriteLine("Clearing attached screenshots");
            }

            _attachedScreenshots.Clear();
            UpdateScreenshotButtonChrome();

            StatusText.Text = "Screenshots removed";
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

        private void UpdateScreenshotButtonChrome()
        {
            if (ScreenshotButtonText == null || ScreenshotButton == null)
            {
                return;
            }

            ScreenshotButtonText.Text = _attachedScreenshots.Count == 0
                ? "📸"
                : $"📸 {_attachedScreenshots.Count}/{MaxAttachedScreenshots}";
            ScreenshotButton.Background = new SolidColorBrush(Color.FromArgb(80, 0, 170, 255));
            ScreenshotButton.IsEnabled = _attachedScreenshots.Count < MaxAttachedScreenshots
                && CurrentModelSupportsVision();
            ScreenshotButton.Opacity = ScreenshotButton.IsEnabled ? 1.0 : 0.5;
            RefreshAttachedScreenshotsStrip();
        }

        // ═══════════════════════════════════════════════════════════════
        // CHECK IF CURRENT MODEL SUPPORTS VISION
        // ═══════════════════════════════════════════════════════════════

        private bool CurrentModelSupportsVision()
        {
            if (_settings == null)
            {
                Log.WriteLine("Settings not initialized");
                return false;
            }

            var provider = GetCurrentRuntimeProviderId();
            var currentModel = GetCurrentRuntimeModelId();
            if (string.IsNullOrWhiteSpace(currentModel) && _rotationManager != null)
            {
                currentModel = _currentAI is HostedManagedAiService
                    ? GetManagedRuntimeModelId(provider)
                    : _rotationManager.GetCurrentModel(provider);
            }

            Log.WriteLine($"Checking vision support for: {provider} - {currentModel}");

            var useByoCatalog = _currentAI is not HostedManagedAiService;
            var model = ProviderModelCatalogCache.GetModel(_settings, provider, currentModel, byo: useByoCatalog)
                ?? ProviderModelCatalogCache.GetModel(_settings, provider, currentModel, byo: !useByoCatalog);
            bool supportsVision = model?.SupportsVision == true;

            Log.WriteLine($"  Result: {(supportsVision ? "✓ Supports vision" : "✗ No vision support")} (catalog only)");
            return supportsVision;
        }


        // ═══════════════════════════════════════════════════════════════
        // IMAGE CONVERSION HELPER
        // ═══════════════════════════════════════════════════════════════

        private static async Task<string?> BitmapImageToBase64Async(BitmapImage? bitmapImage)
        {
            if (bitmapImage == null) return null;

            try
            {
                var source = bitmapImage.Clone();
                source.Freeze();
                return await Task.Run(() =>
                {
                    BitmapSource encodedSource = source;
                    var scale = Math.Min(1d, 1600d / Math.Max(source.PixelWidth, source.PixelHeight));
                    if (scale < 1d)
                    {
                        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                        resized.Freeze();
                        encodedSource = resized;
                    }

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(encodedSource));
                    using var memoryStream = new MemoryStream();
                    encoder.Save(memoryStream);
                    return Convert.ToBase64String(memoryStream.ToArray());
                });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Error converting screenshot to base64: {ex.GetType().Name}");
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
                UpdateScreenshotButtonChrome();
                Log.WriteLine("✓ Screenshot button VISIBLE (model supports vision)");
            }
            else
            {
                ScreenshotButton.Visibility = Visibility.Collapsed;
                if (_attachedScreenshots.Count > 0)
                {
                    _attachedScreenshots.Clear();
                    UpdateScreenshotButtonChrome();
                }
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
                WindowStartupLocation = WindowStartupLocation.Manual,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Cursor = Cursors.None,
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
                MinWidth = 140,
                MaxWidth = 220
            };

            var menuStack = new StackPanel();
            var menuScrollViewer = new ScrollViewer
            {
                Content = menuStack,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true
            };

            var providers = GetAvailableProvidersForCurrentTier();
            
            foreach (var provider in providers)
            {
                var label = provider == _settings.SelectedAI ? $"✓ {provider}" : $"   {provider}";
                var button = new Button
                {
                    Content = new TextBlock
                    {
                        Text = label,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        MaxWidth = 190
                    },
                    Foreground = provider == _settings.SelectedAI ? 
                        new SolidColorBrush(Color.FromRgb(255, 215, 0)) : Brushes.White,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 13,
                    FontWeight = provider == _settings.SelectedAI ? FontWeights.Bold : FontWeights.Normal,
                    Padding = new Thickness(15, 8, 15, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MaxWidth = 210,
                    Cursor = Cursors.None,
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

            // ✅ SHOW WINDOW FIRST
            menuWindow.Show();
            PositionDropdownMenu(menuWindow, ProviderSelectorBorder);
            menuWindow.Activate();
            AttachDropdownCursorTracking(menuWindow);

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
                WindowStartupLocation = WindowStartupLocation.Manual,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Cursor = Cursors.None,
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
                MinWidth = 180,
                MaxWidth = 320
            };

            var menuStack = new StackPanel();
            var menuScrollViewer = new ScrollViewer
            {
                Content = menuStack,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true
            };

            // BYO / managed: models come from catalog cache only (empty is valid).
            string[] models = GetAvailableModelsForSelectedProvider();
            string currentModel = _rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "";

            if (models.Length == 0)
            {
                menuStack.Children.Add(new TextBlock
                {
                    Text = "(no models)",
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    Padding = new Thickness(15, 8, 15, 8),
                    MaxWidth = 290,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            
            foreach (var model in models)
            {
                // ✅ USE REGISTRY - Get display name
            var displayName = GetModelDisplayName(_settings.SelectedAI, model);
                var label = model == currentModel ? $"✓ {displayName}" : $"   {displayName}";
                
                var button = new Button
                {
                    Content = new TextBlock
                    {
                        Text = label,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        MaxWidth = 290
                    },
                    Foreground = model == currentModel ? 
                        new SolidColorBrush(Color.FromRgb(0, 170, 255)) : Brushes.White,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 12,
                    FontWeight = model == currentModel ? FontWeights.Bold : FontWeights.Normal,
                    Padding = new Thickness(15, 8, 15, 8),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MaxWidth = 310,
                    Cursor = Cursors.None,
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

            // ✅ SHOW WINDOW FIRST
            menuWindow.Show();
            PositionDropdownMenu(menuWindow, ModelSelectorBorder);
            menuWindow.Activate();
            AttachDropdownCursorTracking(menuWindow);

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

        private void AttachDropdownCursorTracking(Window menuWindow)
        {
            _cursorManager?.SetOwnedOverlayActive(true);
            menuWindow.MouseEnter += (_, _) => _cursorManager?.EnsureLiveCursorAbove();
            menuWindow.MouseMove += (_, _) => _cursorManager?.EnsureLiveCursorAbove();
            menuWindow.PreviewMouseMove += (_, _) => _cursorManager?.EnsureLiveCursorAbove();
            menuWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_currentDropdownMenu, menuWindow))
                    _currentDropdownMenu = null;

                _cursorManager?.SetOwnedOverlayActive(false);

                // Resume normal main-window cursor tracking after the menu closes.
                if (IsActive || _cursorManager?.IsPointerInsideParentWindow() == true)
                    _cursorManager?.EnsureLiveCursorAbove();
            };

            // Menu Activate() puts the popup above the live cursor; reassert immediately.
            _cursorManager?.EnsureLiveCursorAbove();
            Dispatcher.BeginInvoke(
                new Action(() => _cursorManager?.EnsureLiveCursorAbove()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private bool IsPointerOverCurrentDropdownMenu()
        {
            var menu = _currentDropdownMenu;
            if (menu?.IsVisible != true)
                return false;

            try
            {
                var screen = FormsControl.MousePosition;
                var local = menu.PointFromScreen(new Point(screen.X, screen.Y));
                return local.X >= 0
                    && local.Y >= 0
                    && local.X <= menu.ActualWidth
                    && local.Y <= menu.ActualHeight;
            }
            catch
            {
                return false;
            }
        }

        private void PositionDropdownMenu(Window menuWindow, FrameworkElement anchor)
        {
            menuWindow.UpdateLayout();

            var anchorTopLeftPx = anchor.PointToScreen(new Point(0, 0));
            var anchorBottomLeftPx = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
            var menuWidth = Math.Max(menuWindow.ActualWidth, menuWindow.Width);
            var menuHeight = Math.Max(menuWindow.ActualHeight, menuWindow.Height);
            var screenPoint = new System.Drawing.Point((int)anchorTopLeftPx.X, (int)anchorTopLeftPx.Y);
            var workingAreaPx = FormsScreen.FromPoint(screenPoint).WorkingArea;
            var anchorTopLeft = ScreenPixelsToDip(anchorTopLeftPx);
            var anchorBottomLeft = ScreenPixelsToDip(anchorBottomLeftPx);
            var workingAreaTopLeft = ScreenPixelsToDip(new Point(workingAreaPx.Left, workingAreaPx.Top));
            var workingAreaBottomRight = ScreenPixelsToDip(new Point(workingAreaPx.Right, workingAreaPx.Bottom));

            var desiredLeft = anchorTopLeft.X;
            var minLeft = workingAreaTopLeft.X + 8;
            var maxLeft = workingAreaBottomRight.X - menuWidth - 8;
            var left = Math.Max(minLeft, Math.Min(desiredLeft, Math.Max(minLeft, maxLeft)));

            var belowTop = anchorBottomLeft.Y + 6;
            var aboveTop = anchorTopLeft.Y - menuHeight - 6;
            var canOpenBelow = belowTop + menuHeight <= workingAreaBottomRight.Y - 8;
            var canOpenAbove = aboveTop >= workingAreaTopLeft.Y + 8;

            double top;
            if (canOpenBelow)
            {
                top = belowTop;
            }
            else if (canOpenAbove)
            {
                top = aboveTop;
            }
            else
            {
                top = Math.Max(workingAreaTopLeft.Y + 8, Math.Min(belowTop, workingAreaBottomRight.Y - menuHeight - 8));
            }

            menuWindow.Left = left;
            menuWindow.Top = top;
        }

        private Point ScreenPixelsToDip(Point pixelPoint)
        {
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            return transform.Transform(pixelPoint);
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
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                lock (_streamBuffer)
                {
                    _streamBuffer.Clear();
                }
                _streamRenderedLength = 0;
                if (!string.IsNullOrWhiteSpace(_streamMessageId))
                {
                    await MarkdownHelper.BeginAssistantMessageAsync(ChatWebView, _streamMessageId, GetCurrentDisplayProvider());
                }
            }));
        }



        private void UpdateProviderAndModelDisplay()
        {
            if (AIProviderText == null || ModelText == null) return;
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("UPDATING PROVIDER AND MODEL DISPLAY");

            // Update provider display
            var providerName = GetCurrentDisplayProvider();
            AIProviderText.Text = providerName;
            Log.WriteLine($"  Provider display: {providerName}");
            
            // Update model display
            var runtimeProvider = GetCurrentRuntimeProviderId();
            var currentModel = _currentAI is HostedManagedAiService
                ? GetManagedRuntimeModelId(runtimeProvider)
                : (_rotationManager?.GetCurrentModel(_settings.SelectedAI) ?? "");
            Log.WriteLine($"  Current model ID: {currentModel}");
            
            // ✅ USE REGISTRY - Get display name
            var displayModel = GetModelDisplayName(runtimeProvider, currentModel);
            Log.WriteLine($"  Display name: {displayModel}");
            
            ModelText.Text = GetCompactModelDisplayName(displayModel);
            ModelSelectorBorder.ToolTip = null;
            ProviderSelectorBorder.ToolTip = null;
            
            Log.WriteLine($"✓ Title bar updated: {providerName} | {displayModel}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        private string GetCompactModelDisplayName(string displayModel)
        {
            if (string.IsNullOrWhiteSpace(displayModel))
            {
                return string.Empty;
            }

            const int maxLength = 16;
            return displayModel.Length <= maxLength
                ? displayModel
                : $"{displayModel[..13]}...";
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
                            ConsumedProCredits = completion.ConsumedProCredits,
                            ConsumedPremiumCredits = completion.ConsumedPremiumCredits,
                            PremiumDebtAdded = completion.PremiumDebtAdded,
                            QuestionInputs = completion.QuestionInputs
                        });
                        Log.WriteLine(
                            $"  ✓ Interview finalized: session={completion.SessionId}, blocks={completion.ChargedBlocks}, " +
                            $"charged={completion.ChargedCredits:0.##}, premiumDebt={completion.PremiumDebtAdded:0.##}");
                        _usageReconciliationService.FlushPendingInBackground();
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
                            ["pending"] = "background",
                            ["synced"] = "background",
                            ["failed"] = "background"
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

                if (_chatCursorBridgeInitialized && ChatWebView.CoreWebView2 != null)
                {
                    ChatWebView.CoreWebView2.WebMessageReceived -= ChatWebView_WebMessageReceived;
                    _chatCursorBridgeInitialized = false;
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

        private static string LengthBucket(int length) => length switch
        {
            <= 0 => "empty",
            <= 40 => "1-40",
            <= 160 => "41-160",
            <= 640 => "161-640",
            _ => "641+"
        };
        
    }
}
