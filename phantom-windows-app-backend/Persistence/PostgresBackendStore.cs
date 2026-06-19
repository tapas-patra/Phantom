using Npgsql;
using Phantom.WindowsApp.Backend.Infrastructure;
using System.Net.Sockets;

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
        try
        {
            var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            return connection;
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                "Could not resolve the PostgreSQL host from PHANTOM_WINDOWS_BACKEND_DATABASE_URL. " +
                "Check the Host value in local-dev.env.ps1 or your shell environment.",
                ex);
        }
        catch (NpgsqlException ex) when (ex.InnerException is SocketException)
        {
            throw new InvalidOperationException(
                "Could not connect to PostgreSQL using PHANTOM_WINDOWS_BACKEND_DATABASE_URL. " +
                "Check the hostname, port, and network reachability for the configured database.",
                ex);
        }
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
    phone_number_e164 TEXT NOT NULL DEFAULT '',
    phone_verified BOOLEAN NOT NULL,
    phone_verified_at_utc TIMESTAMPTZ NULL,
    registration_device_fingerprint_hash TEXT NOT NULL DEFAULT '',
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
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS phone_number_e164 TEXT NOT NULL DEFAULT '';
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS phone_verified_at_utc TIMESTAMPTZ NULL;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS registration_device_fingerprint_hash TEXT NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS idx_desktop_accounts_phone_number_e164
    ON desktop_accounts(phone_number_e164);
CREATE INDEX IF NOT EXISTS idx_desktop_accounts_registration_fingerprint
    ON desktop_accounts(registration_device_fingerprint_hash);

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

CREATE TABLE IF NOT EXISTS admin_accounts (
    admin_id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    role TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    last_login_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

ALTER TABLE admin_accounts
    ADD COLUMN IF NOT EXISTS display_name TEXT NOT NULL DEFAULT 'Admin';
ALTER TABLE admin_accounts
    ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'super_admin';
ALTER TABLE admin_accounts
    ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE admin_accounts
    ADD COLUMN IF NOT EXISTS last_login_at_utc TIMESTAMPTZ NULL;

CREATE UNIQUE INDEX IF NOT EXISTS idx_admin_accounts_email
    ON admin_accounts(lower(email));

CREATE TABLE IF NOT EXISTS admin_password_reset_tokens (
    token_hash TEXT PRIMARY KEY,
    admin_id TEXT NOT NULL,
    email TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    consumed BOOLEAN NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL,
    delivery_status TEXT NOT NULL,
    delivery_error TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_admin_password_reset_tokens_email_time
    ON admin_password_reset_tokens(email, created_at_utc DESC);

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

CREATE TABLE IF NOT EXISTS phone_verification_challenges (
    challenge_id TEXT PRIMARY KEY,
    phone_number_e164 TEXT NOT NULL,
    phone_number_masked TEXT NOT NULL,
    device_fingerprint_hash TEXT NOT NULL,
    install_id TEXT NOT NULL,
    email_hint TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    provider_session_id TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    verified_at_utc TIMESTAMPTZ NULL,
    consumed_at_utc TIMESTAMPTZ NULL,
    cooldown_until_utc TIMESTAMPTZ NULL,
    send_attempt_count INTEGER NOT NULL,
    verify_attempt_count INTEGER NOT NULL,
    status TEXT NOT NULL,
    verification_token_hash TEXT NOT NULL,
    failure_reason TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_phone_verification_challenges_phone_time
    ON phone_verification_challenges(phone_number_e164, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_phone_verification_challenges_fingerprint_time
    ON phone_verification_challenges(device_fingerprint_hash, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS payment_orders (
    checkout_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    target TEXT NOT NULL,
    pack_code TEXT NOT NULL,
    display_label TEXT NOT NULL,
    currency TEXT NOT NULL,
    amount_minor INTEGER NOT NULL,
    credits NUMERIC(18,2) NOT NULL,
    premium_debt_credits_covered NUMERIC(18,2) NOT NULL,
    razorpay_order_id TEXT NOT NULL UNIQUE,
    razorpay_payment_id TEXT NOT NULL DEFAULT '',
    razorpay_signature TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL,
    client_confirmed BOOLEAN NOT NULL,
    credited_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_payment_orders_user_created
    ON payment_orders(user_id, created_at_utc DESC);
CREATE UNIQUE INDEX IF NOT EXISTS idx_payment_orders_payment_id
    ON payment_orders(razorpay_payment_id)
    WHERE razorpay_payment_id <> '';

CREATE TABLE IF NOT EXISTS payment_webhook_events (
    event_record_id TEXT PRIMARY KEY,
    external_event_id TEXT NOT NULL UNIQUE,
    event_type TEXT NOT NULL,
    payload_json JSONB NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    processed_at_utc TIMESTAMPTZ NULL
);

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

CREATE TABLE IF NOT EXISTS managed_provider_credentials (
    credential_id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    label TEXT NOT NULL,
    encrypted_api_key TEXT NOT NULL,
    is_enabled BOOLEAN NOT NULL,
    priority INTEGER NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_managed_provider_credentials_provider_priority
    ON managed_provider_credentials(provider_id, priority, updated_at_utc DESC);

CREATE TABLE IF NOT EXISTS managed_provider_catalog (
    provider_id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    models_json JSONB NOT NULL,
    refreshed_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS hosted_knowledge_bases (
    knowledge_base_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    description TEXT NOT NULL,
    status TEXT NOT NULL,
    document_count INTEGER NOT NULL,
    chunk_count INTEGER NOT NULL,
    last_processed_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_hosted_knowledge_bases_user_id
    ON hosted_knowledge_bases(user_id);

CREATE TABLE IF NOT EXISTS hosted_kb_documents (
    document_id TEXT PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    source_type TEXT NOT NULL,
    character_count INTEGER NOT NULL,
    chunk_count INTEGER NOT NULL,
    status TEXT NOT NULL,
    error TEXT NOT NULL,
    uploaded_at_utc TIMESTAMPTZ NOT NULL,
    processed_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_documents_kb
    ON hosted_kb_documents(knowledge_base_id, uploaded_at_utc DESC);

CREATE TABLE IF NOT EXISTS hosted_kb_chunks (
    chunk_id TEXT PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    document_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    document_title TEXT NOT NULL,
    text TEXT NOT NULL,
    search_text TEXT NOT NULL,
    embedding_json JSONB NOT NULL,
    token_count INTEGER NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_chunks_kb
    ON hosted_kb_chunks(knowledge_base_id, document_id, chunk_index);

CREATE TABLE IF NOT EXISTS desktop_context_packs (
    pack_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    name TEXT NOT NULL,
    resume_text TEXT NOT NULL,
    job_description_text TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_desktop_context_packs_user_id
    ON desktop_context_packs(user_id, updated_at_utc DESC, name ASC);
";
        command.ExecuteNonQuery();
    }

    private static string BuildConnectionString(string databaseUrl)
    {
        if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
        {
            var hostBuilder = new NpgsqlConnectionStringBuilder(databaseUrl);
            ApplyRecommendedDefaults(hostBuilder);
            return hostBuilder.ConnectionString;
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

        ApplyRecommendedDefaults(builder);
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

        ApplyRecommendedDefaults(builder);
        return builder.ConnectionString;
    }

    private static void ApplyRecommendedDefaults(NpgsqlConnectionStringBuilder builder)
    {
        if (builder.Timeout <= 0)
        {
            builder.Timeout = 15;
        }

        if (builder.CommandTimeout <= 0)
        {
            builder.CommandTimeout = 60;
        }

        if (builder.KeepAlive <= 0)
        {
            builder.KeepAlive = 30;
        }

        builder.Pooling = true;
    }
}
