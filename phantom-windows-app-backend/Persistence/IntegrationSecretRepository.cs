using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class IntegrationSecretRepository
{
    private readonly PostgresBackendStore _store;

    public IntegrationSecretRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public IntegrationSecretRecord? FindByKey(string secretKey)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM integration_secrets WHERE secret_key = @secretKey LIMIT 1;";
        command.Parameters.AddWithValue("secretKey", secretKey);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new IntegrationSecretRecord
            {
                SecretKey = reader.GetString(reader.GetOrdinal("secret_key")),
                EncryptedValue = reader.GetString(reader.GetOrdinal("encrypted_value")),
                UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
            }
            : null;
    }

    public void Save(IntegrationSecretRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO integration_secrets (secret_key, encrypted_value, updated_at_utc)
VALUES (@secretKey, @encryptedValue, @updatedAtUtc)
ON CONFLICT(secret_key) DO UPDATE SET
    encrypted_value = EXCLUDED.encrypted_value,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("secretKey", record.SecretKey);
        command.Parameters.AddWithValue("encryptedValue", record.EncryptedValue);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
        command.ExecuteNonQuery();
    }
}
