using Microsoft.Data.Sqlite;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class LockRepository
{
    private readonly SqliteBackendStore _store;

    public LockRepository(SqliteBackendStore store)
    {
        _store = store;
    }

    public DesktopLockRecord? FindActiveByUser(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM interview_locks WHERE user_id = $userId ORDER BY expires_at_utc DESC LIMIT 1;";
        command.Parameters.AddWithValue("$userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DesktopLockRecord? FindBySessionId(string sessionId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM interview_locks WHERE session_id = $sessionId LIMIT 1;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(DesktopLockRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO interview_locks (
    session_id, user_id, device_id, lock_token, expires_at_utc, last_heartbeat_at_utc, app_version
) VALUES (
    $sessionId, $userId, $deviceId, $lockToken, $expiresAt, $lastHeartbeatAt, $appVersion
)
ON CONFLICT(session_id) DO UPDATE SET
    user_id = excluded.user_id,
    device_id = excluded.device_id,
    lock_token = excluded.lock_token,
    expires_at_utc = excluded.expires_at_utc,
    last_heartbeat_at_utc = excluded.last_heartbeat_at_utc,
    app_version = excluded.app_version;";
        command.Parameters.AddWithValue("$sessionId", record.SessionId);
        command.Parameters.AddWithValue("$userId", record.UserId);
        command.Parameters.AddWithValue("$deviceId", record.DeviceId);
        command.Parameters.AddWithValue("$lockToken", record.LockToken);
        command.Parameters.AddWithValue("$expiresAt", record.ExpiresAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$lastHeartbeatAt", record.LastHeartbeatAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$appVersion", record.AppVersion);
        command.ExecuteNonQuery();
    }

    public void Delete(string sessionId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM interview_locks WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.ExecuteNonQuery();
    }

    private static DesktopLockRecord Map(SqliteDataReader reader)
    {
        return new DesktopLockRecord
        {
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            LockToken = reader.GetString(reader.GetOrdinal("lock_token")),
            ExpiresAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("expires_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastHeartbeatAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("last_heartbeat_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            AppVersion = reader.GetString(reader.GetOrdinal("app_version"))
        };
    }
}
