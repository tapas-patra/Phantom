using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class OAuthPendingStateRepository
{
    private readonly PostgresBackendStore _store;

    public OAuthPendingStateRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Save(OAuthPendingStateRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO oauth_pending_states (provider, state_token, expires_at_utc, created_at_utc)
VALUES (@provider, @stateToken, @expiresAtUtc, @createdAtUtc)
ON CONFLICT(state_token) DO UPDATE SET
    provider = EXCLUDED.provider,
    expires_at_utc = EXCLUDED.expires_at_utc;";
        command.Parameters.AddWithValue("provider", record.Provider);
        command.Parameters.AddWithValue("stateToken", record.StateToken);
        command.Parameters.AddWithValue("expiresAtUtc", record.ExpiresAtUtc);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.ExecuteNonQuery();
    }

    public OAuthPendingStateRecord? Find(string provider, string stateToken)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM oauth_pending_states WHERE provider = @provider AND state_token = @stateToken LIMIT 1;";
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("stateToken", stateToken);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new OAuthPendingStateRecord
            {
                Provider = reader.GetString(reader.GetOrdinal("provider")),
                StateToken = reader.GetString(reader.GetOrdinal("state_token")),
                ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
                CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
            }
            : null;
    }

    public void Delete(string stateToken)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM oauth_pending_states WHERE state_token = @stateToken;";
        command.Parameters.AddWithValue("stateToken", stateToken);
        command.ExecuteNonQuery();
    }
}
