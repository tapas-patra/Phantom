using Npgsql;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class PostgresBackendStore
{
    private readonly string _connectionString;

    public PostgresBackendStore(BackendOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabaseUrl))
        {
            throw new InvalidOperationException(
                "Backend database URL is not configured. Set PHANTOM_WINDOWS_BACKEND_DATABASE_URL.");
        }

        _connectionString = BuildConnectionString(options.DatabaseUrl);
        EnsureSchema();
    }

    public NpgsqlConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public bool CanConnect()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            command.ExecuteScalar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS desktop_accounts (
    user_id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    email_verified_at_utc TIMESTAMPTZ NULL,
    access_tier TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    phone_verified BOOLEAN NOT NULL,
    pro_available_credits NUMERIC(18,2) NOT NULL,
    premium_available_credits NUMERIC(18,2) NOT NULL,
    premium_negative_credits NUMERIC(18,2) NOT NULL,
    lease_expires_at_utc TIMESTAMPTZ NOT NULL,
    offline_mode_enabled BOOLEAN NOT NULL,
    last_validated_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS access_tier TEXT NOT NULL DEFAULT 'free';
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS email_verified BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS email_verified_at_utc TIMESTAMPTZ NULL;

CREATE TABLE IF NOT EXISTS auth_sessions (
    session_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    access_token_hash TEXT NOT NULL UNIQUE,
    refresh_token_hash TEXT NOT NULL UNIQUE,
    auth_method TEXT NOT NULL,
    device_install_id TEXT NOT NULL,
    device_fingerprint_hash TEXT NOT NULL,
    authenticated_at_utc TIMESTAMPTZ NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    is_authenticated BOOLEAN NOT NULL,
    revoked_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_auth_sessions_user_id ON auth_sessions(user_id);
CREATE INDEX IF NOT EXISTS idx_auth_sessions_refresh_token_hash ON auth_sessions(refresh_token_hash);

CREATE TABLE IF NOT EXISTS magic_links (
    token_hash TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    install_id TEXT NOT NULL,
    device_fingerprint_hash TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    consumed BOOLEAN NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL,
    delivery_status TEXT NOT NULL,
    delivery_error TEXT NULL
);

CREATE TABLE IF NOT EXISTS interview_locks (
    session_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    lock_token TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    last_heartbeat_at_utc TIMESTAMPTZ NOT NULL,
    app_version TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_interview_locks_user_id ON interview_locks(user_id);

CREATE TABLE IF NOT EXISTS usage_ledger (
    ledger_entry_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    session_id TEXT NOT NULL UNIQUE,
    started_at_utc TIMESTAMPTZ NOT NULL,
    ended_at_utc TIMESTAMPTZ NOT NULL,
    charged_credits NUMERIC(18,2) NOT NULL,
    charged_blocks INTEGER NOT NULL,
    added_premium_debt NUMERIC(18,2) NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_usage_ledger_user_id ON usage_ledger(user_id);

CREATE TABLE IF NOT EXISTS telemetry_events (
    event_id TEXT PRIMARY KEY,
    category TEXT NOT NULL,
    event_name TEXT NOT NULL,
    payload_json JSONB NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS auth_login_attempts (
    attempt_id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    ip_address TEXT NOT NULL,
    attempted_at_utc TIMESTAMPTZ NOT NULL,
    succeeded BOOLEAN NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_auth_login_attempts_email_ip_time
    ON auth_login_attempts(email, ip_address, attempted_at_utc DESC);

CREATE TABLE IF NOT EXISTS email_verification_tokens (
    token_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    consumed BOOLEAN NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL,
    delivery_status TEXT NOT NULL,
    delivery_error TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_email_verification_tokens_email
    ON email_verification_tokens(email, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS integration_secrets (
    secret_key TEXT PRIMARY KEY,
    encrypted_value TEXT NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS oauth_pending_states (
    state_token TEXT PRIMARY KEY,
    provider TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_oauth_pending_states_provider_expiry
    ON oauth_pending_states(provider, expires_at_utc DESC);
";
        command.ExecuteNonQuery();
    }

    private static string BuildConnectionString(string databaseUrl)
    {
        if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
        {
            return databaseUrl;
        }

        if (databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return BuildConnectionStringFromUriLikeValue(databaseUrl);
        }

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must be a valid PostgreSQL URI.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }

    private static string BuildConnectionStringFromUriLikeValue(string databaseUrl)
    {
        var withoutScheme = databaseUrl
            .Replace("postgresql://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("postgres://", string.Empty, StringComparison.OrdinalIgnoreCase);

        var atIndex = withoutScheme.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= withoutScheme.Length - 1)
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must include credentials and host.");
        }

        var userInfo = withoutScheme[..atIndex];
        var hostAndDatabase = withoutScheme[(atIndex + 1)..];

        var colonIndex = userInfo.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= userInfo.Length - 1)
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must include username and password.");
        }

        var username = Uri.UnescapeDataString(userInfo[..colonIndex]);
        var password = Uri.UnescapeDataString(userInfo[(colonIndex + 1)..]);

        var slashIndex = hostAndDatabase.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= hostAndDatabase.Length - 1)
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must include database name.");
        }

        var hostPort = hostAndDatabase[..slashIndex];
        var databaseAndQuery = hostAndDatabase[(slashIndex + 1)..];
        var queryIndex = databaseAndQuery.IndexOf('?');
        var databaseName = queryIndex >= 0
            ? databaseAndQuery[..queryIndex]
            : databaseAndQuery;

        var host = hostPort;
        var port = 5432;
        var lastColonIndex = hostPort.LastIndexOf(':');
        if (lastColonIndex > 0 && lastColonIndex < hostPort.Length - 1
            && int.TryParse(hostPort[(lastColonIndex + 1)..], out var parsedPort))
        {
            host = hostPort[..lastColonIndex];
            port = parsedPort;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = Uri.UnescapeDataString(databaseName),
            Username = username,
            Password = password,
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}
