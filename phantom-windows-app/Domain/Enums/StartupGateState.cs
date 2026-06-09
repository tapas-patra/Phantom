namespace SecureOverlay.Domain.Enums
{
    public enum StartupGateState
    {
        AuthChoice = 0,
        Login = 1,
        CheckingAccount = 2,
        VerificationRequired = 3,
        NoCredits = 4,
        NegativeBalance = 5,
        OfflineLeaseExpired = 6,
        ReadOnlySafeMode = 7,
        Ready = 8,
        BackendUnavailable = 9
    }
}
