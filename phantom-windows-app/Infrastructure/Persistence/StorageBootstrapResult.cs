using SecureOverlay.Domain.ValueObjects;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class StorageBootstrapResult
    {
        public StorageBootstrapResult(StorageMode mode, string databasePath, string? safeModeReason = null)
        {
            Mode = mode;
            DatabasePath = databasePath;
            SafeModeReason = safeModeReason;
        }

        public StorageMode Mode { get; }
        public string DatabasePath { get; }
        public string? SafeModeReason { get; }
        public bool IsReadOnlySafeMode => Mode == StorageMode.ReadOnlySafeMode;
    }
}
