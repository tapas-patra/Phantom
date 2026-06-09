using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using SecureOverlay.Application.Auth;
using SecureOverlay.Application.Device;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Domain.Enums;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using SecureOverlay.Infrastructure.Persistence;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Platform.Windows;
using SecureOverlay.Platform.Windows.Device;
using SecureOverlay.Platform.Windows.Secrets;
using SecureOverlay.Services;

namespace SecureOverlay
{
    public partial class StartupWindow : Window
    {
        private readonly IStartupGateService _startupGateService;
        private readonly IDeviceIdentityService _deviceIdentityService;
        private readonly HostedRuntimeOptions _hostedRuntimeOptions;
        private StartupGateContext _currentContext;
        private string? _pendingMagicLinkCallbackUri;
        private string? _pendingMagicLinkUrl;

        public StartupWindow(IStartupGateService startupGateService)
        {
            InitializeComponent();
            _startupGateService = startupGateService;
            _hostedRuntimeOptions = HostedClientFactory.LoadOptions();
            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            IDeviceProfileRepository deviceProfileRepository = new SqliteDeviceProfileRepository(store);
            _deviceIdentityService = new WindowsDeviceIdentityService(deviceProfileRepository, new WindowsSecretVault(store));
            ClearCredentials();
            BackendModeText.Text = _hostedRuntimeOptions.UseRemoteBackend
                ? $"{_hostedRuntimeOptions.ModeLabel}\n{_hostedRuntimeOptions.DesktopBackendBaseUrl}"
                : $"{_hostedRuntimeOptions.ModeLabel}\nNo backend URL configured.";
            _currentContext = startupGateService.GetInitialContext();
            ApplyContext(_currentContext);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContext.State == StartupGateState.Login)
            {
                _currentContext = _startupGateService.CompleteLogin(EmailTextBox.Text, PasswordTextBox.Password, useMagicLink: false);
                ApplyContext(_currentContext);
                InlineStatusText.Text = "Completing password login and refreshing account validation...";
                await AdvanceToEvaluatedStateAsync();
                return;
            }

            _currentContext = _startupGateService.BeginLogin();
            ApplyContext(_currentContext);
            InlineStatusText.Text = "Enter credentials and press Login again to continue.";
        }

        private async void MagicLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContext.State != StartupGateState.Login)
            {
                _currentContext = _startupGateService.BeginLogin();
                ApplyContext(_currentContext);
                InlineStatusText.Text = "Enter your email, then press Send Magic Link. You can return to the previous screen with Back.";
                return;
            }

            if (_hostedRuntimeOptions.UseRemoteBackend)
            {
                try
                {
                    var issuedLink = _startupGateService.RequestMagicLink(EmailTextBox.Text);
                    _pendingMagicLinkCallbackUri = issuedLink.CallbackUri;
                    _pendingMagicLinkUrl = issuedLink.MagicLinkUrl;
                    MagicLinkUrlText.Text = $"{issuedLink.MagicLinkUrl}\nExpires: {issuedLink.ExpiresAtUtc:yyyy-MM-dd HH:mm:ss}";
                    MagicLinkResultPanel.Visibility = Visibility.Visible;
                    OpenMagicLinkButton.Visibility = Visibility.Visible;
                    CompleteMagicLinkButton.Visibility = Visibility.Visible;
                    InlineStatusText.Text = $"Magic link issued for {issuedLink.Email}. Use Complete Magic Link to finish in-app, or open the link externally.";
                }
                catch (Exception ex)
                {
                    InlineStatusText.Text = $"Failed to issue magic link: {ex.Message}";
                }

                return;
            }

            _currentContext = _startupGateService.CompleteLogin(EmailTextBox.Text, PasswordTextBox.Password, useMagicLink: true);
            ApplyContext(_currentContext);
            InlineStatusText.Text = "Completing magic-link login and refreshing account validation...";
            await AdvanceToEvaluatedStateAsync();
        }

        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenMainWindowAsync();
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _currentContext = _startupGateService.Retry();
            ApplyContext(_currentContext);
        }

        private void CompleteMagicLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_pendingMagicLinkCallbackUri))
            {
                InlineStatusText.Text = "No pending magic link callback is available.";
                return;
            }

            _currentContext = _startupGateService.ProcessAuthCallback(_pendingMagicLinkCallbackUri);
            _pendingMagicLinkCallbackUri = null;
            _pendingMagicLinkUrl = null;
            ApplyContext(_currentContext);
        }

        private void OpenMagicLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_pendingMagicLinkUrl))
            {
                InlineStatusText.Text = "No issued magic link URL is available.";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _pendingMagicLinkUrl,
                    UseShellExecute = true
                });
                InlineStatusText.Text = "Magic link opened in your browser. Complete the hosted page, then return to Phantom.";
            }
            catch (Exception ex)
            {
                InlineStatusText.Text = $"Failed to open magic link: {ex.Message}";
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ClearCredentials();
            _pendingMagicLinkCallbackUri = null;
            _pendingMagicLinkUrl = null;
            _currentContext = _startupGateService.ResetToAuthChoice();
            ApplyContext(_currentContext);
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            ClearCredentials();
            _pendingMagicLinkCallbackUri = null;
            _pendingMagicLinkUrl = null;
            _currentContext = _startupGateService.ResetToAuthChoice();
            ApplyContext(_currentContext);
            InlineStatusText.Text = "Signed out from the current desktop session.";
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                var deviceProfile = _deviceIdentityService.GetOrCreateProfile();
                var url = HostedWebRoutes.BuildRegisterUrl(new DeviceRegistrationMetadataDto
                {
                    AppVersion = version,
                    InstallId = deviceProfile.InstallId,
                    DeviceLabel = deviceProfile.DeviceLabel,
                    MachineFingerprintHash = deviceProfile.MachineFingerprintHash,
                    SecretFingerprintHint = deviceProfile.SecretFingerprintHint
                });

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                InlineStatusText.Text = $"Registration opened for {deviceProfile.DeviceLabel}. Return to the app after account creation.";
            }
            catch (Exception ex)
            {
                InlineStatusText.Text = $"Failed to open registration page: {ex.Message}";
            }
        }

        private void ApplyContext(StartupGateContext context)
        {
            var isLoginState = context.State == StartupGateState.Login;
            var isAuthChoiceState = context.State == StartupGateState.AuthChoice;
            var showSignOut = !isAuthChoiceState && !isLoginState;

            StateTitleText.Text = context.Title;
            StateMessageText.Text = context.Message;
            InlineStatusText.Text = context.Detail ?? string.Empty;

            LoginButton.Visibility = context.CanAttemptLogin ? Visibility.Visible : Visibility.Collapsed;
            MagicLinkButton.Visibility = context.CanAttemptLogin ? Visibility.Visible : Visibility.Collapsed;
            RegisterButton.Visibility = context.CanRegister ? Visibility.Visible : Visibility.Collapsed;
            BackButton.Visibility = isLoginState ? Visibility.Visible : Visibility.Collapsed;
            SignOutButton.Visibility = showSignOut ? Visibility.Visible : Visibility.Collapsed;
            ContinueButton.Visibility = context.CanOpenMainApp ? Visibility.Visible : Visibility.Collapsed;
            RetryButton.Visibility = context.CanRetry ? Visibility.Visible : Visibility.Collapsed;
            CredentialsPanel.Visibility = isLoginState ? Visibility.Visible : Visibility.Collapsed;
            ChoicePanel.Visibility = isAuthChoiceState ? Visibility.Visible : Visibility.Collapsed;
            if (!isLoginState)
            {
                MagicLinkResultPanel.Visibility = Visibility.Collapsed;
                OpenMagicLinkButton.Visibility = Visibility.Collapsed;
                CompleteMagicLinkButton.Visibility = Visibility.Collapsed;
            }

            LeftPanelTitleText.Text = isLoginState ? "Credentials" : "Login Methods";
            LeftPanelMessageText.Text = isLoginState
                ? "Enter your email and continue with either password login or a magic link. This flow validates against the configured hosted backend."
                : "Choose a sign-in method. Login stays in-app. Registration opens on the hosted website.";
            PasswordLabel.Visibility = isLoginState ? Visibility.Visible : Visibility.Collapsed;
            PasswordTextBox.Visibility = isLoginState ? Visibility.Visible : Visibility.Collapsed;

            if (context.State == StartupGateState.ReadOnlySafeMode)
            {
                SafeModeText.Text = $"Safe mode reason: {context.Message}";
                SafeModeText.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrWhiteSpace(context.Detail))
            {
                SafeModeText.Text = context.Detail;
                SafeModeText.Visibility = Visibility.Visible;
            }
            else
            {
                SafeModeText.Visibility = Visibility.Collapsed;
            }

            LoginButton.Content = context.State == StartupGateState.Login ? "Submit Login" : "Login";
            MagicLinkButton.Content = context.State == StartupGateState.Login ? "Send Magic Link" : "Magic Link";
        }

        private void ClearCredentials()
        {
            EmailTextBox.Text = string.Empty;
            PasswordTextBox.Password = string.Empty;
        }

        private async Task AdvanceToEvaluatedStateAsync()
        {
            await Task.Delay(900);
            _currentContext = _startupGateService.Retry();
            ApplyContext(_currentContext);
        }

        private Task OpenMainWindowAsync()
        {
            var mainWindow = new MainWindow(CreateLaunchContext(_currentContext));
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
            return Task.CompletedTask;
        }

        private static AppLaunchContext CreateLaunchContext(StartupGateContext context)
        {
            return new AppLaunchContext
            {
                GateState = context.State,
                Title = context.Title,
                Message = context.Message,
                Detail = context.Detail ?? string.Empty,
                CanStartInterview = context.State == StartupGateState.Ready,
                CanResumeLockedInterview = context.CanResumeLockedInterview
            };
        }
    }
}
