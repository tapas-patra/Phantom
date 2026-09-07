namespace Phantom.Dashboard.Backend.Persistence;

public static class DashboardSchemaMigrations
{
    public static IReadOnlyList<SchemaMigration> All { get; } = new[]
    {
        new SchemaMigration("001_dashboard_projection_schema", DashboardProjectionSchemaSql),
        new SchemaMigration("002_interview_question_banks", InterviewQuestionBanksSql),
        new SchemaMigration("003_interview_question_bank_names", InterviewQuestionBankNamesSql),
        new SchemaMigration("004_power_features", PowerFeaturesSql),
        new SchemaMigration("005_email_verification", EmailVerificationSql)
    };

    private const string DashboardProjectionSchemaSql = @"
CREATE TABLE IF NOT EXISTS dashboard_account_summaries (
    user_id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    effective_access_tier TEXT NOT NULL,
    plan_label TEXT NOT NULL,
    email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    phone_verified BOOLEAN NOT NULL,
    pro_available_credits NUMERIC(18,2) NOT NULL,
    premium_available_credits NUMERIC(18,2) NOT NULL,
    premium_negative_credits NUMERIC(18,2) NOT NULL,
    lease_expires_at_utc TIMESTAMPTZ NOT NULL,
    offline_mode_enabled BOOLEAN NOT NULL,
    can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE,
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
    charged_pro_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
    charged_premium_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
    added_premium_debt NUMERIC(18,2) NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

ALTER TABLE dashboard_wallet_history
    ADD COLUMN IF NOT EXISTS charged_pro_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE dashboard_wallet_history
    ADD COLUMN IF NOT EXISTS charged_premium_credits NUMERIC(18,2) NOT NULL DEFAULT 0;

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

    private const string InterviewQuestionBanksSql = @"
CREATE TABLE IF NOT EXISTS dashboard_interview_question_banks (
    session_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    questions_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    interview_started_at_utc TIMESTAMPTZ NOT NULL,
    interview_ended_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_dashboard_interview_question_banks_user_ended
    ON dashboard_interview_question_banks(user_id, interview_ended_at_utc DESC);
";

    private const string InterviewQuestionBankNamesSql = @"
ALTER TABLE dashboard_interview_question_banks
    ADD COLUMN IF NOT EXISTS interview_name TEXT NOT NULL DEFAULT '';
";

    private const string PowerFeaturesSql = @"
ALTER TABLE dashboard_account_summaries
    ADD COLUMN IF NOT EXISTS can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE;
";

    private const string EmailVerificationSql = @"
ALTER TABLE dashboard_account_summaries
    ADD COLUMN IF NOT EXISTS email_verified BOOLEAN NOT NULL DEFAULT FALSE;
";
}
