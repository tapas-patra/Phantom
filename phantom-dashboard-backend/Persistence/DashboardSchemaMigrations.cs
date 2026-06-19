namespace Phantom.Dashboard.Backend.Persistence;

public static class DashboardSchemaMigrations
{
    public static IReadOnlyList<SchemaMigration> All { get; } = new[]
    {
        new SchemaMigration("001_dashboard_projection_schema", DashboardProjectionSchemaSql)
    };

    private const string DashboardProjectionSchemaSql = @"
CREATE TABLE IF NOT EXISTS dashboard_account_summaries (
    user_id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    effective_access_tier TEXT NOT NULL,
    plan_label TEXT NOT NULL,
    phone_verified BOOLEAN NOT NULL,
    pro_available_credits NUMERIC(18,2) NOT NULL,
    premium_available_credits NUMERIC(18,2) NOT NULL,
    premium_negative_credits NUMERIC(18,2) NOT NULL,
    lease_expires_at_utc TIMESTAMPTZ NOT NULL,
    offline_mode_enabled BOOLEAN NOT NULL,
    last_validated_at_utc TIMESTAMPTZ NOT NULL,
    active_device_count INTEGER NOT NULL,
    last_activity_at_utc TIMESTAMPTZ NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_dashboard_account_summaries_email
    ON dashboard_account_summaries(lower(email));

CREATE TABLE IF NOT EXISTS dashboard_wallet_history (
    ledger_entry_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    session_id TEXT NOT NULL,
    charged_credits NUMERIC(18,2) NOT NULL,
    charged_blocks INTEGER NOT NULL,
    added_premium_debt NUMERIC(18,2) NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_dashboard_wallet_history_user_created
    ON dashboard_wallet_history(user_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS dashboard_wallet_purchases (
    checkout_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    target TEXT NOT NULL,
    pack_code TEXT NOT NULL,
    display_label TEXT NOT NULL,
    amount_minor INTEGER NOT NULL,
    credits NUMERIC(18,2) NOT NULL,
    premium_debt_credits_covered NUMERIC(18,2) NOT NULL,
    status TEXT NOT NULL,
    client_confirmed BOOLEAN NOT NULL,
    credited_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_dashboard_wallet_purchases_user_created
    ON dashboard_wallet_purchases(user_id, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS dashboard_device_inventory (
    user_id TEXT NOT NULL,
    device_install_id TEXT NOT NULL,
    device_fingerprint_hash TEXT NOT NULL,
    last_authenticated_at_utc TIMESTAMPTZ NOT NULL,
    auth_method TEXT NOT NULL,
    is_active BOOLEAN NOT NULL,
    PRIMARY KEY(user_id, device_install_id, device_fingerprint_hash)
);

CREATE INDEX IF NOT EXISTS idx_dashboard_device_inventory_user
    ON dashboard_device_inventory(user_id, last_authenticated_at_utc DESC);

CREATE TABLE IF NOT EXISTS dashboard_support_previews (
    user_id TEXT PRIMARY KEY,
    open_lock_session_id TEXT NOT NULL,
    last_usage_charge_credits NUMERIC(18,2) NOT NULL,
    lease_expires_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);";
}
