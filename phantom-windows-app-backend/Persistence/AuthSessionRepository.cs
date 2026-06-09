using Microsoft.Data.Sqlite;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AuthSessionRepository
{
    private readonly SqliteBackendStore _store;

    public AuthSessionRepository(SqliteBackendStore store)
    {
        _store = store;
    }

    public void Save(DesktopSessionRecord session)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO auth_sessions (
    session_id, user_id, email, access_token, refresh_token, auth_method, device_install_id,
    device_fingerprint_hash, authenticated_at_utc, expires_at_utc, is_authenticated
) VALUES (
    $sessionId, $userId, $email, $accessToken, $refreshToken, $authMethod, $deviceInstallId,
    $deviceFingerprintHash, $authenticatedAt, $expiresAt, $isAuthenticated
);";
        command.Parameters.AddWithValue("$sessionId", session.SessionId);
        command.Parameters.AddWithValue("$userId", session.UserId);
        command.Parameters.AddWithValue("$email", session.Email);
        command.Parameters.AddWithValue("$accessToken", session.AccessToken);
        command.Parameters.AddWithValue("$refreshToken", session.RefreshToken);
        command.Parameters.AddWithValue("$authMethod", session.AuthMethod);
        command.Parameters.AddWithValue("$deviceInstallId", session.DeviceInstallId);
        command.Parameters.AddWithValue("$deviceFingerprintHash", session.DeviceFingerprintHash);
        command.Parameters.AddWithValue("$authenticatedAt", session.AuthenticatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$expiresAt", session.ExpiresAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$isAuthenticated", session.IsAuthenticated ? 1 : 0);
        command.ExecuteNonQuery();
    }
}
