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
using SecureOverlay.Platform.Windows;
using SecureOverlay.Platform.Windows.Device;
using SecureOverlay.Platform.Windows.Secrets;
using SecureOverlay.Services;

namespace SecureOverlay
{
    public partial class StartupWindow : Window
    {
        private const string DefaultStubEmail = "local-user@phantom.app";
        private const string DefaultStubPassword = "phantom123";
        private readonly IStartupGateService _startupGateService;
        private readonly IDeviceIdentityService _deviceIdentityService;
        private StartupGateContext _currentContext;

        public StartupWindow(IStartupGateService startupGateService)
        {
            InitializeComponent();
            _startupGateService = startupGateService;
            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            IDeviceProfileRepository deviceProfileRepository = new SqliteDeviceProfileRepository(store);
            _deviceIdentityService = new WindowsDeviceIdentityService(deviceProfileRepository, new WindowsSecretVault(store));
            SeedStubCredentials();
            _currentContext = startupGateService.GetInitialContext();
            ApplyContext(_currentContext);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContext.State == StartupGateState.Login)
            {
                _currentContext = _startupGateService.CompleteLogin(EmailTextBox.Text, useMagicLink: false);
                ApplyContext(_currentContext);
                InlineStatusText.Text = "Simulating password login and account validation...";
                await AdvanceToEvaluatedStateAsync();
                return;
            }

            _currentContext = _startupGateService.BeginLogin();
            ApplyContext(_currentContext);
            InlineStatusText.Text = "Enter credentials and press Login again to continue in local stub mode.";
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

            _currentContext = _startupGateService.CompleteLogin(EmailTextBox.Text, useMagicLink: true);
            ApplyContext(_currentContext);
            InlineStatusText.Text = "Simulating login and account validation...";
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            SeedStubCredentials();
            _currentContext = _startupGateService.ResetToAuthChoice();
            ApplyContext(_currentContext);
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            SeedStubCredentials();
            _currentContext = _startupGateService.ResetToAuthChoice();
            ApplyContext(_currentContext);
            InlineStatusText.Text = "Signed out from the local stub session.";
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

            LeftPanelTitleText.Text = isLoginState ? "Credentials" : "Login Methods";
            LeftPanelMessageText.Text = isLoginState
                ? "Enter your email and continue with either password login or a magic link. A default stub user is prefilled so you can test the flow immediately."
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

        private void SeedStubCredentials()
        {
            EmailTextBox.Text = DefaultStubEmail;
            PasswordTextBox.Password = DefaultStubPassword;
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
                CanStartInterview = context.State == StartupGateState.Ready
            };
        }
    }
}
