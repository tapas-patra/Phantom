using Microsoft.Data.Sqlite;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class SqliteBackendStore
{
    private readonly string _databasePath;

    public SqliteBackendStore(BackendOptions options)
    {
        _databasePath = Path.IsPathRooted(options.DatabasePath)
            ? options.DatabasePath
            : Path.Combine(AppContext.BaseDirectory, options.DatabasePath);
        EnsureSchema();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS desktop_accounts (
    user_id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    phone_verified INTEGER NOT NULL,
    pro_available_credits TEXT NOT NULL,
    premium_available_credits TEXT NOT NULL,
    premium_negative_credits TEXT NOT NULL,
    lease_expires_at_utc TEXT NOT NULL,
    offline_mode_enabled INTEGER NOT NULL,
    last_validated_at_utc TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS auth_sessions (
    session_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    access_token TEXT NOT NULL,
    refresh_token TEXT NOT NULL,
    auth_method TEXT NOT NULL,
    device_install_id TEXT NOT NULL,
    device_fingerprint_hash TEXT NOT NULL,
    authenticated_at_utc TEXT NOT NULL,
    expires_at_utc TEXT NOT NULL,
    is_authenticated INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS interview_locks (
    session_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    lock_token TEXT NOT NULL,
    expires_at_utc TEXT NOT NULL,
    last_heartbeat_at_utc TEXT NOT NULL,
    app_version TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS usage_ledger (
    ledger_entry_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    session_id TEXT NOT NULL UNIQUE,
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NOT NULL,
    charged_credits TEXT NOT NULL,
    charged_blocks INTEGER NOT NULL,
    added_premium_debt TEXT NOT NULL,
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS telemetry_events (
    event_id TEXT PRIMARY KEY,
    category TEXT NOT NULL,
    event_name TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL
);
";
        command.ExecuteNonQuery();
    }
}
