using System;
using System.Collections.Generic;
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

        public LocalStartupGateService(string? pendingCallbackUri)
        {
            var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
            _authSessionRepository = new SqliteAuthSessionRepository(store);
            _accountCacheRepository = new SqliteAccountCacheRepository(store);
            _telemetryService = new LocalTelemetryService(new SqliteTelemetryRepository(store));
            var deviceIdentityService = new WindowsDeviceIdentityService(
                new SqliteDeviceProfileRepository(store),
                new WindowsSecretVault(store));
            var deviceProfile = deviceIdentityService.GetOrCreateProfile();
            _authClient = new LocalHostedAuthClient(deviceProfile);
            _accountClient = new LocalHostedAccountClient();
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
            if (session == null || !session.IsAuthenticated)
            {
                return BuildContext(
                    StartupGateState.AuthChoice,
                    "Welcome To Phantom",
                    "Sign in to continue, or register on the website to create and verify a new account.",
                    canOpenMainApp: false,
                    canAttemptLogin: true,
                    canRegister: true,
                    canRetry: false,
                    detail: "No local authenticated session was found.");
            }

            return EvaluateAccountState(session, _accountCacheRepository.Load());
        }

        public StartupGateContext BeginLogin()
        {
            return BuildContext(
                StartupGateState.Login,
                "Login",
                "Use the in-app login flow. Hosted auth is not wired yet, so this phase persists a local session and account snapshot through the production startup seam.",
                canOpenMainApp: false,
                canAttemptLogin: true,
                canRegister: true,
                canRetry: false,
                detail: "Submit Login or Magic Link to persist a local authenticated session.");
        }

        public StartupGateContext CompleteLogin(string email, bool useMagicLink)
        {
            var sessionDto = _authClient.CreateLocalSession(email, useMagicLink);
            var accountCheckDto = _accountClient.BuildLocalAccountCheck(sessionDto);

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
                canAttemptLogin: false,
                canRegister: false,
                canRetry: false,
                detail: "Hosted validation will replace this local cache evaluation later.");
        }

        public StartupGateContext ProcessAuthCallback(string callbackUri)
        {
            var callbackResult = _authClient.ParseCallback(callbackUri);
            var sessionDto = new AuthSessionDto
            {
                UserId = callbackResult.Email.ToLowerInvariant(),
                Email = callbackResult.Email,
                AccessToken = $"callback-access::{Guid.NewGuid():N}",
                RefreshToken = $"callback-refresh::{Guid.NewGuid():N}",
                AuthMethod = "callback",
                DeviceInstallId = callbackResult.DeviceInstallId,
                DeviceFingerprintHash = callbackResult.DeviceFingerprintHash,
                AuthenticatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(12),
                IsAuthenticated = true
            };
            var accountCheckDto = _accountClient.BuildCallbackAccountCheck(callbackResult);

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
                canAttemptLogin: true,
                canRegister: true,
                canRetry: false,
                detail: "Choose a sign-in method to continue.");
        }

        private static StartupGateContext EvaluateAccountState(AuthSessionCache session, AccountCacheSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                return BuildContext(
                    StartupGateState.CheckingAccount,
                    "Checking Account",
                    "Authenticated session found, but no local account snapshot is available yet.",
                    canOpenMainApp: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: $"Signed in as {session.Email}. Account cache still needs to be hydrated.");
            }

            if (!snapshot.PhoneVerified)
            {
                return BuildContext(
                    StartupGateState.VerificationRequired,
                    "Phone Verification Required",
                    "Your account is signed in, but phone verification is still required before the app can be used.",
                    canOpenMainApp: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: $"Signed in as {session.Email}.");
            }

            if (snapshot.PremiumNegativeCredits > 0m)
            {
                return BuildContext(
                    StartupGateState.NegativeBalance,
                    "Negative Premium Balance",
                    "The app shell may load later, but starting a new interview must remain blocked until the negative Premium balance is cleared.",
                    canOpenMainApp: true,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: $"Outstanding Premium balance: {snapshot.PremiumNegativeCredits:0.##} credit.");
            }

            if (LeaseExpired(snapshot) && !snapshot.HasResumableLockedSession)
            {
                return BuildContext(
                    StartupGateState.OfflineLeaseExpired,
                    "Offline Lease Expired",
                    "The cached offline lease has expired and there is no resumable locked session on this device.",
                    canOpenMainApp: false,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: "A new interview must stay blocked until backend validation succeeds.");
            }

            if (snapshot.ProAvailableCredits < 0.25m && snapshot.PremiumAvailableCredits < 0.25m && !snapshot.HasResumableLockedSession)
            {
                return BuildContext(
                    StartupGateState.NoCredits,
                    "No Credits Available",
                    "You are signed in, but no plan has at least one full first metering block available for a new interview.",
                    canOpenMainApp: true,
                    canAttemptLogin: false,
                    canRegister: false,
                    canRetry: true,
                    detail: "The app shell can open, but interview start must remain blocked until credits are added.");
            }

            return BuildContext(
                StartupGateState.Ready,
                "Ready",
                "Startup checks passed from the local account cache. Continue into the desktop app.",
                canOpenMainApp: true,
                canAttemptLogin: false,
                canRegister: false,
                canRetry: true,
                detail: $"Signed in as {session.Email}.");
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
