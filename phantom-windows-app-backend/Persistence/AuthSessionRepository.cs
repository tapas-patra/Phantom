using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AuthSessionRepository
{
    private readonly PostgresBackendStore _store;

    public AuthSessionRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Save(DesktopSessionRecord session)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO auth_sessions (
    session_id, user_id, email, access_token_hash, refresh_token_hash, auth_method, device_install_id,
    device_fingerprint_hash, authenticated_at_utc, expires_at_utc, is_authenticated, revoked_at_utc
) VALUES (
    @sessionId, @userId, @email, @accessTokenHash, @refreshTokenHash, @authMethod, @deviceInstallId,
    @deviceFingerprintHash, @authenticatedAt, @expiresAt, @isAuthenticated, @revokedAt
)
ON CONFLICT(session_id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    email = EXCLUDED.email,
    access_token_hash = EXCLUDED.access_token_hash,
    refresh_token_hash = EXCLUDED.refresh_token_hash,
    auth_method = EXCLUDED.auth_method,
    device_install_id = EXCLUDED.device_install_id,
    device_fingerprint_hash = EXCLUDED.device_fingerprint_hash,
    authenticated_at_utc = EXCLUDED.authenticated_at_utc,
    expires_at_utc = EXCLUDED.expires_at_utc,
    is_authenticated = EXCLUDED.is_authenticated,
    revoked_at_utc = EXCLUDED.revoked_at_utc;";
        Bind(command, session);
        command.ExecuteNonQuery();
    }

    public DesktopSessionRecord? FindByRefreshTokenHash(string refreshTokenHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM auth_sessions WHERE refresh_token_hash = @refreshTokenHash LIMIT 1;";
        command.Parameters.AddWithValue("refreshTokenHash", refreshTokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DesktopSessionRecord? FindLatestByUser(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM auth_sessions
WHERE user_id = @userId
ORDER BY authenticated_at_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DesktopSessionRecord? FindByAccessTokenHash(string accessTokenHash)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM auth_sessions WHERE access_token_hash = @accessTokenHash LIMIT 1;";
        command.Parameters.AddWithValue("accessTokenHash", accessTokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void RevokeBySessionId(string sessionId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE auth_sessions
SET is_authenticated = FALSE, revoked_at_utc = @revokedAt
WHERE session_id = @sessionId;";
        command.Parameters.AddWithValue("revokedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("sessionId", sessionId);
        command.ExecuteNonQuery();
    }

    public void RevokeAllByUserId(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE auth_sessions
SET is_authenticated = FALSE, revoked_at_utc = @revokedAt
WHERE user_id = @userId
  AND is_authenticated = TRUE;";
        command.Parameters.AddWithValue("revokedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("userId", userId);
        command.ExecuteNonQuery();
    }

    public void DeleteExpired()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM auth_sessions WHERE expires_at_utc < @cutoff;";
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow.AddDays(-1));
        command.ExecuteNonQuery();
    }

    private static void Bind(NpgsqlCommand command, DesktopSessionRecord session)
    {
        command.Parameters.AddWithValue("sessionId", session.SessionId);
        command.Parameters.AddWithValue("userId", session.UserId);
        command.Parameters.AddWithValue("email", session.Email);
        command.Parameters.AddWithValue("accessTokenHash", session.AccessTokenHash);
        command.Parameters.AddWithValue("refreshTokenHash", session.RefreshTokenHash);
        command.Parameters.AddWithValue("authMethod", session.AuthMethod);
        command.Parameters.AddWithValue("deviceInstallId", session.DeviceInstallId);
        command.Parameters.AddWithValue("deviceFingerprintHash", session.DeviceFingerprintHash);
        command.Parameters.AddWithValue("authenticatedAt", session.AuthenticatedAtUtc);
        command.Parameters.AddWithValue("expiresAt", session.ExpiresAtUtc);
        command.Parameters.AddWithValue("isAuthenticated", session.IsAuthenticated);
        command.Parameters.AddWithValue("revokedAt", session.RevokedAtUtc ?? (object)DBNull.Value);
    }

    private static DesktopSessionRecord Map(NpgsqlDataReader reader)
    {
        var revokedOrdinal = reader.GetOrdinal("revoked_at_utc");
        return new DesktopSessionRecord
        {
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            AccessTokenHash = reader.GetString(reader.GetOrdinal("access_token_hash")),
            RefreshTokenHash = reader.GetString(reader.GetOrdinal("refresh_token_hash")),
            AuthMethod = reader.GetString(reader.GetOrdinal("auth_method")),
            DeviceInstallId = reader.GetString(reader.GetOrdinal("device_install_id")),
            DeviceFingerprintHash = reader.GetString(reader.GetOrdinal("device_fingerprint_hash")),
            AuthenticatedAtUtc = reader.GetDateTime(reader.GetOrdinal("authenticated_at_utc")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("expires_at_utc")),
            IsAuthenticated = reader.GetBoolean(reader.GetOrdinal("is_authenticated")),
            RevokedAtUtc = reader.IsDBNull(revokedOrdinal) ? null : reader.GetDateTime(revokedOrdinal)
        };
    }
}
