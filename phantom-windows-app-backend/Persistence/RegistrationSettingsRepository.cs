namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class RegistrationSettingsRepository
{
    private const string GlobalSettingsId = "global";
    private readonly PostgresBackendStore _store;

    public RegistrationSettingsRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public (bool PhoneVerificationRequired, DateTime? UpdatedAtUtc) Get()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT phone_verification_required, updated_at_utc
FROM registration_settings
WHERE settings_id = @settingsId
LIMIT 1;";
        command.Parameters.AddWithValue("settingsId", GlobalSettingsId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetBoolean(0), reader.GetDateTime(1))
            : (false, null);
    }

    public DateTime Save(bool phoneVerificationRequired)
    {
        var now = DateTime.UtcNow;
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO registration_settings (settings_id, phone_verification_required, updated_at_utc)
VALUES (@settingsId, @phoneVerificationRequired, @updatedAtUtc)
ON CONFLICT (settings_id) DO UPDATE SET
    phone_verification_required = EXCLUDED.phone_verification_required,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("settingsId", GlobalSettingsId);
        command.Parameters.AddWithValue("phoneVerificationRequired", phoneVerificationRequired);
        command.Parameters.AddWithValue("updatedAtUtc", now);
        command.ExecuteNonQuery();
        return now;
    }
}
