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
            StateTitleText.Text = context.Title;
            StateMessageText.Text = context.Message;
            InlineStatusText.Text = context.Detail ?? string.Empty;

            LoginButton.Visibility = context.CanAttemptLogin ? Visibility.Visible : Visibility.Collapsed;
            MagicLinkButton.Visibility = context.CanAttemptLogin ? Visibility.Visible : Visibility.Collapsed;
            RegisterButton.Visibility = context.CanRegister ? Visibility.Visible : Visibility.Collapsed;
            ContinueButton.Visibility = context.CanOpenMainApp ? Visibility.Visible : Visibility.Collapsed;
            RetryButton.Visibility = context.CanRetry ? Visibility.Visible : Visibility.Collapsed;

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
