using System;
using System.Collections.Generic;
using System.Reflection;
using SecureOverlay.Application.Auth;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Telemetry;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Domain.Enums;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using SecureOverlay.Infrastructure.Persistence;
using SecureOverlay.Infrastructure.Telemetry;
using SecureOverlay.Platform.Windows.Device;
using SecureOverlay.Platform.Windows.Secrets;
using SecureOverlay.Services;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class LocalStartupGateService : IStartupGateService
    {
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IAccountCacheRepository _accountCacheRepository;
        private readonly IHostedAuthClient _authClient;
        private readonly IHostedAccountClient _accountClient;
        private readonly ITelemetryService _telemetryService;
        private readonly string? _pendingCallbackUri;
        private readonly DeviceProfile _deviceProfile;
        private readonly HostedRuntimeOptions _hostedRuntimeOptions;
        public LocalStartupGateService(string? pendingCallbackUri)
        {
            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            _authSessionRepository = new SqliteAuthSessionRepository(store);
            _accountCacheRepository = new SqliteAccountCacheRepository(store);
            _telemetryService = new LocalTelemetryService(new SqliteTelemetryRepository(store));
            var deviceIdentityService = new WindowsDeviceIdentityService(
                new SqliteDeviceProfileRepository(store),
                new WindowsSecretVault(store));
            _deviceProfile = deviceIdentityService.GetOrCreateProfile();
            _hostedRuntimeOptions = HostedClientFactory.LoadOptions();
            _authClient = HostedClientFactory.CreateAuthClient(_deviceProfile, _hostedRuntimeOptions);
            _accountClient = HostedClientFactory.CreateAccountClient(_hostedRuntimeOptions);
            _pendingCallbackUri = pendingCallbackUri;
        }

        public StartupGateContext GetInitialContext()
        {
            if (SettingsManager.IsReadOnlySafeMode())
            {
                return BuildContext(
                    StartupGateState.ReadOnlySafeMode,
                    "Read-Only Safe Mode",
                    SettingsManager.GetSafeModeReason() ?? "Storage startup failed. The app is running in read-only safe mode.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: "Writes are disabled until storage bootstrap succeeds again.");
            }

            if (!string.IsNullOrWhiteSpace(_pendingCallbackUri))
            {
                return ProcessAuthCallback(_pendingCallbackUri);
            }

            var session = _authSessionRepository.Load();
            if (IsLegacyStubSession(session))
            {
                _authSessionRepository.Clear();
                _accountCacheRepository.Clear();
                session = null;
            }

            if (session == null || !session.IsAuthenticated)
            {
                return BuildContext(
                    StartupGateState.AuthChoice,
                    "Welcome To Phantom",
                    "Sign in to continue, or register on the website to create and verify a new account.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: true,
                    canRegister: true,
                    canRetry: false,
                    detail: "No local authenticated session was found.");
            }

            var cachedSnapshot = _accountCacheRepository.Load();
            try
            {
                var refreshedSnapshot = MapAccountSnapshot(_accountClient.GetStartupAccountCheck(MapSessionForHostedCheck(session)));
                _accountCacheRepository.Save(refreshedSnapshot);
                _telemetryService.Track("auth", "startup_account_check_refreshed", new Dictionary<string, string>
                {
                    ["source"] = refreshedSnapshot.UserId,
                    ["mode"] = _hostedRuntimeOptions.Mode
                });
                return EvaluateAccountState(session, refreshedSnapshot);
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Hosted startup refresh failed: {ex.Message}");
                _telemetryService.Track("auth", "startup_account_check_failed", new Dictionary<string, string>
                {
                    ["mode"] = _hostedRuntimeOptions.Mode,
                    ["reason"] = ex.Message
                });

                if (cachedSnapshot != null && CanUseCachedSnapshotOffline(cachedSnapshot))
                {
                    return EvaluateAccountState(
                        session,
                        cachedSnapshot,
                        "Backend unavailable. Using cached account validation for offline launch rules.");
                }

                return BuildBackendUnavailableContext(
                    "Backend Unavailable",
                    "The desktop backend could not be reached to refresh your account state.",
                    "Reconnect and press Retry. Offline launch requires a valid cached lease or resumable locked session.");
            }
        }

        public StartupGateContext BeginLogin()
        {
            return BuildContext(
                StartupGateState.Login,
                "Login",
                "Use the in-app login flow backed by the hosted Phantom backend.",
                canOpenMainApp: false,
                canResumeLockedInterview: false,
                canAttemptLogin: true,
                canRegister: true,
                canRetry: false,
                detail: "Submit Login or Magic Link to complete authentication and refresh the startup account snapshot.");
        }

        public StartupGateContext CompleteLogin(string email, bool useMagicLink)
        {
            return CompleteLogin(email, string.Empty, useMagicLink);
        }

        public AuthMagicLinkIssuedDto RequestMagicLink(string email)
        {
            return _authClient.RequestMagicLink(new AuthMagicLinkRequestDto
            {
                Email = email,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                InstallId = _deviceProfile.InstallId,
                DeviceLabel = _deviceProfile.DeviceLabel,
                DeviceFingerprintHash = _deviceProfile.MachineFingerprintHash,
                SecretFingerprintHint = _deviceProfile.SecretFingerprintHint
            });
        }

        public StartupGateContext CompleteLogin(string email, string password, bool useMagicLink)
        {
            try
            {
                var sessionDto = _authClient.CreateSession(new AuthLoginRequestDto
                {
                    Email = email,
                    Password = password,
                    UseMagicLink = useMagicLink,
                    AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                    InstallId = _deviceProfile.InstallId,
                    DeviceLabel = _deviceProfile.DeviceLabel,
                    DeviceFingerprintHash = _deviceProfile.MachineFingerprintHash,
                    SecretFingerprintHint = _deviceProfile.SecretFingerprintHint
                });
                var accountCheckDto = _accountClient.GetStartupAccountCheck(sessionDto);

                _authSessionRepository.Save(MapAuthSession(sessionDto));
                _accountCacheRepository.Save(MapAccountSnapshot(accountCheckDto));
                _telemetryService.Track("auth", "login_completed", new Dictionary<string, string>
                {
                    ["method"] = useMagicLink ? "magic_link" : "password",
                    ["user"] = sessionDto.Email
                });

                return BuildContext(
                    StartupGateState.CheckingAccount,
                    "Checking Account",
                    "Validating auth, entitlement, wallet, lease, and session-lock state against the local startup cache.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: false,
                    detail: "Authentication completed. Refreshing account validation now.");
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Login failed against hosted seam: {ex.Message}");
                return BuildContext(
                    StartupGateState.Login,
                    "Login Failed",
                    "Authentication could not be completed.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: true,
                    canRegister: true,
                    canRetry: true,
                    detail: ex.Message);
            }
        }

        public StartupGateContext ProcessAuthCallback(string callbackUri)
        {
            try
            {
                var callbackCompletion = _authClient.CompleteCallback(new AuthCallbackCompletionRequestDto
                {
                    CallbackUri = callbackUri,
                    InstallId = _deviceProfile.InstallId,
                    DeviceFingerprintHash = _deviceProfile.MachineFingerprintHash
                });
                var sessionDto = callbackCompletion.Session;
                var callbackResult = callbackCompletion.CallbackResult;
                var accountCheckDto = _accountClient.GetStartupAccountCheck(sessionDto);

                _authSessionRepository.Save(MapAuthSession(sessionDto));
                var snapshot = MapAccountSnapshot(accountCheckDto);
                _accountCacheRepository.Save(snapshot);
                _telemetryService.Track("auth", "callback_processed", new Dictionary<string, string>
                {
                    ["status"] = callbackResult.Status,
                    ["user"] = callbackResult.Email
                });
                return EvaluateAccountState(_authSessionRepository.Load()!, snapshot);
            }
            catch (HostedServiceException ex)
            {
                Log.WriteLine($"Auth callback completion failed: {ex.Message}");
                return BuildBackendUnavailableContext(
                    "Callback Validation Failed",
                    "The desktop app could not complete the website sign-in callback.",
                    ex.Message);
            }
        }

        public StartupGateContext Retry()
        {
            return GetInitialContext();
        }

        public StartupGateContext ResetToAuthChoice()
        {
            _authSessionRepository.Clear();
            _accountCacheRepository.Clear();

            return BuildContext(
                StartupGateState.AuthChoice,
                "Welcome To Phantom",
                "Sign in to continue, or register on the website to create and verify a new account.",
                canOpenMainApp: false,
                canResumeLockedInterview: false,
                canAttemptLogin: true,
                canRegister: true,
                canRetry: false,
                detail: "Choose a sign-in method to continue.");
        }

        private static StartupGateContext EvaluateAccountState(AuthSessionCache session, AccountCacheSnapshot? snapshot, string? detailPrefix = null)
        {
            if (snapshot == null)
            {
                return BuildContext(
                    StartupGateState.CheckingAccount,
                    "Checking Account",
                    "Authenticated session found, but no local account snapshot is available yet.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, $"Signed in as {session.Email}. Account cache still needs to be hydrated."));
            }

            if (!snapshot.EmailVerified)
            {
                return BuildContext(
                    StartupGateState.VerificationRequired,
                    "Email Verification Required",
                    "Your account is signed in, but email verification is still required before the app can be used.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, $"Signed in as {session.Email}."));
            }

            if (LeaseExpired(snapshot))
            {
                if (snapshot.HasResumableLockedSession)
                {
                    return BuildContext(
                        StartupGateState.OfflineLeaseExpired,
                        "Offline Lease Expired",
                        "The cached offline lease has expired. You may resume the currently locked local session on this device, but new interviews must remain blocked.",
                        canOpenMainApp: true,
                        canResumeLockedInterview: true,
                        canAttemptLogin: false,
                        canRegister: false,
                        canRetry: true,
                        detail: ComposeDetail(detailPrefix, "Reconnect and refresh account validation before starting another interview."));
                }

                return BuildContext(
                    StartupGateState.OfflineLeaseExpired,
                    "Offline Lease Expired",
                    "The cached offline lease has expired and there is no resumable locked session on this device.",
                    canOpenMainApp: false,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, "A new interview must stay blocked until backend validation succeeds."));
            }

            if (snapshot.PremiumNegativeCredits > 0m)
            {
                return BuildContext(
                    StartupGateState.NegativeBalance,
                    "Negative Premium Balance",
                    "The app shell may load later, but starting a new interview must remain blocked until the negative Premium balance is cleared.",
                    canOpenMainApp: true,
                    canResumeLockedInterview: snapshot.HasResumableLockedSession,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, $"Outstanding Premium balance: {snapshot.PremiumNegativeCredits:0.##} credit."));
            }

            var isFreeTier = IsFreeTier(snapshot);
            var hasMeteringBlock = snapshot.ProAvailableCredits >= 0.25m || snapshot.PremiumAvailableCredits >= 0.25m;
            var hasFreeTrialBlock = snapshot.PremiumAvailableCredits >= 0.25m;

            if (isFreeTier && !hasFreeTrialBlock && !snapshot.HasResumableLockedSession)
            {
                return BuildContext(
                    StartupGateState.NoCredits,
                    "Free Trial Exhausted",
                    "The free trial does not have a full 15 minute demo block remaining for a new interview.",
                    canOpenMainApp: true,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, "The app shell can open, but a new interview stays blocked until trial credits are restored by Phantom."));
            }

            if (isFreeTier && !hasFreeTrialBlock)
            {
                return BuildContext(
                    StartupGateState.NoCredits,
                    "Free Trial Exhausted",
                    "The free trial has no full demo block left for a new interview, but the current locked session may continue on this device.",
                    canOpenMainApp: true,
                    canResumeLockedInterview: true,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, "Resume the current locked session or restore trial credits before starting another interview."));
            }

            if (!isFreeTier && !hasMeteringBlock && !snapshot.HasResumableLockedSession)
            {
                return BuildContext(
                    StartupGateState.NoCredits,
                    "No Credits Available",
                    "You are signed in, but no plan has at least one full first metering block available for a new interview.",
                    canOpenMainApp: true,
                    canResumeLockedInterview: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, "The app shell can open, but interview start must remain blocked until credits are added."));
            }

            if (!isFreeTier && !hasMeteringBlock)
            {
                return BuildContext(
                    StartupGateState.NoCredits,
                    "No Credits Available",
                    "No plan has one full first metering block available for a new interview, but the current locked session may continue on this device.",
                    canOpenMainApp: true,
                    canResumeLockedInterview: true,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: ComposeDetail(detailPrefix, "Add credits before attempting to start another interview."));
            }

            return BuildContext(
                StartupGateState.Ready,
                "Ready",
                "Startup checks passed from the local account cache. Continue into the desktop app.",
                canOpenMainApp: true,
                canResumeLockedInterview: snapshot.HasResumableLockedSession,
                canAttemptLogin: false,
                canRegister: false,
                canRetry: true,
                detail: ComposeDetail(detailPrefix, $"Signed in as {session.Email}."));
        }

        private static bool CanUseCachedSnapshotOffline(AccountCacheSnapshot snapshot)
        {
            if (snapshot.HasResumableLockedSession)
            {
                return true;
            }

            return snapshot.LeaseExpiresAtUtc.HasValue
                && snapshot.LeaseExpiresAtUtc.Value > DateTime.UtcNow;
        }

        private static bool IsFreeTier(AccountCacheSnapshot snapshot)
        {
            return string.Equals(snapshot.AccessTier, "free", StringComparison.OrdinalIgnoreCase);
        }

        private StartupGateContext BuildBackendUnavailableContext(string title, string message, string detail)
        {
            return BuildContext(
                StartupGateState.BackendUnavailable,
                title,
                message,
                canOpenMainApp: false,
                canResumeLockedInterview: false,
                canAttemptLogin: true,
                canRegister: true,
                canRetry: true,
                detail: ComposeDetail(
                    $"Mode: {_hostedRuntimeOptions.ModeLabel}.",
                    detail));
        }

        private static string ComposeDetail(string? prefix, string detail)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return detail;
            }

            return $"{prefix} {detail}".Trim();
        }

        private static AuthSessionDto MapSessionForHostedCheck(AuthSessionCache session)
        {
            return new AuthSessionDto
            {
                UserId = session.UserId,
                Email = session.Email,
                AccessToken = session.AccessToken,
                RefreshToken = session.RefreshToken,
                AuthMethod = session.AuthMethod,
                DeviceInstallId = session.DeviceInstallId,
                DeviceFingerprintHash = session.DeviceFingerprintHash,
                AuthenticatedAtUtc = session.AuthenticatedAtUtc,
                ExpiresAtUtc = session.ExpiresAtUtc,
                IsAuthenticated = session.IsAuthenticated
            };
        }

        private static bool IsLegacyStubSession(AuthSessionCache? session)
        {
            if (session == null)
            {
                return false;
            }

            return (!string.IsNullOrWhiteSpace(session.AccessToken) &&
                    (session.AccessToken.StartsWith("local-", StringComparison.OrdinalIgnoreCase)
                     || session.AccessToken.StartsWith("callback-", StringComparison.OrdinalIgnoreCase)))
                || string.Equals(session.Email, "local-user@phantom.app", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LeaseExpired(AccountCacheSnapshot snapshot)
        {
            return snapshot.LeaseExpiresAtUtc.HasValue
                && snapshot.LeaseExpiresAtUtc.Value <= DateTime.UtcNow
                && snapshot.OfflineModeEnabled;
        }

        private static StartupGateContext BuildContext(
            StartupGateState state,
            string title,
            string message,
            bool canOpenMainApp,
            bool canResumeLockedInterview,
            bool canAttemptLogin,
            bool canRegister,
            bool canRetry,
            string? detail = null)
        {
            return new StartupGateContext(
                state,
                title,
                message,
                canOpenMainApp,
                canResumeLockedInterview,
                canAttemptLogin,
                canRegister,
                canRetry,
                detail);
        }

        private static AuthSessionCache MapAuthSession(AuthSessionDto sessionDto)
        {
            return new AuthSessionCache
            {
                UserId = sessionDto.UserId,
                Email = sessionDto.Email,
                AccessToken = sessionDto.AccessToken,
                RefreshToken = sessionDto.RefreshToken,
                DeviceInstallId = sessionDto.DeviceInstallId,
                DeviceFingerprintHash = sessionDto.DeviceFingerprintHash,
                AuthMethod = sessionDto.AuthMethod,
                AuthenticatedAtUtc = sessionDto.AuthenticatedAtUtc,
                ExpiresAtUtc = sessionDto.ExpiresAtUtc,
                IsAuthenticated = sessionDto.IsAuthenticated
            };
        }

        private static AccountCacheSnapshot MapAccountSnapshot(StartupAccountCheckResultDto dto)
        {
            return new AccountCacheSnapshot
            {
                UserId = dto.UserId,
                AccessTier = string.IsNullOrWhiteSpace(dto.AccessTier) ? "free" : dto.AccessTier,
                EmailVerified = dto.EmailVerified,
                PhoneVerified = dto.PhoneVerified,
                ProAvailableCredits = dto.Wallet.ProAvailableCredits,
                PremiumAvailableCredits = dto.Wallet.PremiumAvailableCredits,
                PremiumNegativeCredits = dto.Wallet.PremiumNegativeCredits,
                LeaseExpiresAtUtc = dto.LeaseExpiresAtUtc,
                HasResumableLockedSession = dto.HasResumableLockedSession,
                LastLockTokenHash = dto.LastLockTokenHash,
                LastLockedSessionId = dto.LastLockedSessionId,
                OfflineModeEnabled = dto.OfflineModeEnabled,
                LastValidatedAtUtc = dto.LastValidatedAtUtc
            };
        }
    }
}
