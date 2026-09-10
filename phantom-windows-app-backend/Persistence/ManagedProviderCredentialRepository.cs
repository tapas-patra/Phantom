using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class ManagedProviderCredentialRepository
{
    private readonly PostgresBackendStore _store;

    public ManagedProviderCredentialRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ManagedProviderCredentialRecord> ListByProvider(string providerId, string workload = "chat")
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM managed_provider_credentials
WHERE provider_id = @providerId
  AND workload = @workload
ORDER BY priority ASC, updated_at_utc DESC;";
        command.Parameters.AddWithValue("providerId", providerId);
        command.Parameters.AddWithValue("workload", workload);
        using var reader = command.ExecuteReader();
        var items = new List<ManagedProviderCredentialRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public IReadOnlyList<ManagedProviderCredentialRecord> ListAll(string workload = "chat")
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM managed_provider_credentials
WHERE workload = @workload
ORDER BY provider_id ASC, priority ASC, updated_at_utc DESC;";
        command.Parameters.AddWithValue("workload", workload);
        using var reader = command.ExecuteReader();
        var items = new List<ManagedProviderCredentialRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public ManagedProviderCredentialRecord? FindById(string credentialId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM managed_provider_credentials WHERE credential_id = @credentialId LIMIT 1;";
        command.Parameters.AddWithValue("credentialId", credentialId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(ManagedProviderCredentialRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO managed_provider_credentials (
    credential_id, workload, provider_id, label, encrypted_api_key, is_enabled, priority, created_at_utc, updated_at_utc
) VALUES (
    @credentialId, @workload, @providerId, @label, @encryptedApiKey, @isEnabled, @priority, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (credential_id) DO UPDATE SET
    workload = EXCLUDED.workload,
    provider_id = EXCLUDED.provider_id,
    label = EXCLUDED.label,
    encrypted_api_key = EXCLUDED.encrypted_api_key,
    is_enabled = EXCLUDED.is_enabled,
    priority = EXCLUDED.priority,
    cooldown_until_utc = NULL,
    last_failure_code = '',
    consecutive_failure_count = 0,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("credentialId", record.CredentialId);
        command.Parameters.AddWithValue("workload", record.Workload);
        command.Parameters.AddWithValue("providerId", record.ProviderId);
        command.Parameters.AddWithValue("label", record.Label);
        command.Parameters.AddWithValue("encryptedApiKey", record.EncryptedApiKey);
        command.Parameters.AddWithValue("isEnabled", record.IsEnabled);
        command.Parameters.AddWithValue("priority", record.Priority);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
        command.ExecuteNonQuery();
    }

    public void Delete(string credentialId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM managed_provider_credentials WHERE credential_id = @credentialId;";
        command.Parameters.AddWithValue("credentialId", credentialId);
        command.ExecuteNonQuery();
    }

    public void RecordFailure(string credentialId, string errorCode, DateTime cooldownUntilUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE managed_provider_credentials
SET cooldown_until_utc = @cooldownUntilUtc,
    last_failure_code = @errorCode,
    consecutive_failure_count = consecutive_failure_count + 1,
    updated_at_utc = NOW()
WHERE credential_id = @credentialId;";
        command.Parameters.AddWithValue("credentialId", credentialId);
        command.Parameters.AddWithValue("errorCode", errorCode);
        command.Parameters.AddWithValue("cooldownUntilUtc", cooldownUntilUtc);
        command.ExecuteNonQuery();
    }

    public void RecordSuccess(string credentialId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE managed_provider_credentials
SET cooldown_until_utc = NULL,
    last_failure_code = '',
    consecutive_failure_count = 0,
    updated_at_utc = NOW()
WHERE credential_id = @credentialId
  AND (cooldown_until_utc IS NOT NULL OR consecutive_failure_count <> 0 OR last_failure_code <> '');";
        command.Parameters.AddWithValue("credentialId", credentialId);
        command.ExecuteNonQuery();
    }

    private static ManagedProviderCredentialRecord Map(NpgsqlDataReader reader)
    {
        return new ManagedProviderCredentialRecord
        {
            CredentialId = reader.GetString(reader.GetOrdinal("credential_id")),
            Workload = reader.GetString(reader.GetOrdinal("workload")),
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            Label = reader.GetString(reader.GetOrdinal("label")),
            EncryptedApiKey = reader.GetString(reader.GetOrdinal("encrypted_api_key")),
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            Priority = reader.GetInt32(reader.GetOrdinal("priority")),
            CooldownUntilUtc = reader.IsDBNull(reader.GetOrdinal("cooldown_until_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("cooldown_until_utc")),
            LastFailureCode = reader.GetString(reader.GetOrdinal("last_failure_code")),
            ConsecutiveFailureCount = reader.GetInt32(reader.GetOrdinal("consecutive_failure_count")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}
