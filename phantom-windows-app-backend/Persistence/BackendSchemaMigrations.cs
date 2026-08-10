using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Persistence;

public static class BackendSchemaMigrations
{
    public static IReadOnlyList<SchemaMigration> All => new[]
    {
        new SchemaMigration("001_backend_core_schema", CoreSchemaSql),
        new SchemaMigration("002_dashboard_projection_schema", DashboardProjectionSchemaSql),
        new SchemaMigration("003_operational_indexes", OperationalIndexesSql),
        new SchemaMigration("004_managed_ai_runtime_selection", ManagedAiRuntimeSelectionSql),
        new SchemaMigration("005_support_and_auth_schema_patch", SupportAndAuthSchemaPatchSql),
        new SchemaMigration("006_usage_credit_split", UsageCreditSplitSql),
        new SchemaMigration("007_hosted_kb_search_index", HostedKnowledgeBaseSearchIndexSql),
        new SchemaMigration("008_hosted_kb_vector_upgrade", HostedKnowledgeBaseVectorUpgradeSql),
        new SchemaMigration("009_hosted_kb_embedding_admin_config", HostedKnowledgeBaseEmbeddingAdminConfigSql),
        new SchemaMigration("010_hosted_kb_reindex_jobs", HostedKnowledgeBaseReindexJobsSql),
        new SchemaMigration("011_hosted_kb_variable_embedding_dimensions", HostedKnowledgeBaseVariableEmbeddingDimensionsSql),
        new SchemaMigration("012_hosted_kb_online_hnsw_index", HostedKnowledgeBaseOnlineHnswIndexSql),
        new SchemaMigration("013_hosted_kb_typed_memory", HostedKnowledgeBaseTypedMemorySql),
        new SchemaMigration("014_managed_ai_latency_checks", ManagedAiLatencyChecksSql),
        new SchemaMigration("015_interview_question_banks", InterviewQuestionBanksSql),
        new SchemaMigration("016_interview_question_bank_names", InterviewQuestionBankNamesSql),
        new SchemaMigration("017_account_terms_acceptance", AccountTermsAcceptanceSql),
        new SchemaMigration("018_registration_settings", RegistrationSettingsSql),
        new SchemaMigration("019_power_features_and_manual_locks", PowerFeaturesAndManualLocksSql),
        new SchemaMigration("020_hosted_kb_experience_cards", HostedKnowledgeBaseExperienceCardsSql),
        new SchemaMigration("021_single_experience_current", SingleExperienceCurrentSql)
    };

    public static IReadOnlyList<SchemaMigration> DashboardProjectionOnly { get; } = new[]
    {
        new SchemaMigration("001_dashboard_projection_schema", DashboardProjectionReplicaSchemaSql),
        new SchemaMigration("002_dashboard_usage_credit_split", DashboardProjectionUsageCreditSplitSql),
        new SchemaMigration("003_dashboard_interview_question_banks", DashboardInterviewQuestionBanksSql),
        new SchemaMigration("004_dashboard_interview_question_bank_names", DashboardInterviewQuestionBankNamesSql),
        new SchemaMigration("005_dashboard_power_features", DashboardPowerFeaturesSql)
    };

    private const string AccountTermsAcceptanceSql = @"
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS terms_accepted_at_utc TIMESTAMPTZ NULL;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS terms_version TEXT NOT NULL DEFAULT '';
";

    private const string RegistrationSettingsSql = @"
CREATE TABLE IF NOT EXISTS registration_settings (
    settings_id TEXT PRIMARY KEY,
    phone_verification_required BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

INSERT INTO registration_settings (settings_id, phone_verification_required, updated_at_utc)
VALUES ('global', FALSE, NOW())
ON CONFLICT (settings_id) DO NOTHING;
";

    private const string DashboardPowerFeaturesSql = @"
ALTER TABLE dashboard_account_summaries
    ADD COLUMN IF NOT EXISTS can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE;
";

    private const string PowerFeaturesAndManualLocksSql = @"
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS manual_lock_expires_at_utc TIMESTAMPTZ NULL;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS manual_lock_reason TEXT NOT NULL DEFAULT '';
ALTER TABLE dashboard_account_summaries
    ADD COLUMN IF NOT EXISTS can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE;

CREATE OR REPLACE FUNCTION refresh_dashboard_account_summary(p_user_id TEXT)
RETURNS VOID AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM desktop_accounts WHERE user_id = p_user_id) THEN
        DELETE FROM dashboard_account_summaries WHERE user_id = p_user_id;
        RETURN;
    END IF;

    INSERT INTO dashboard_account_summaries (
        user_id, email, effective_access_tier, plan_label, phone_verified,
        pro_available_credits, premium_available_credits, premium_negative_credits,
        lease_expires_at_utc, offline_mode_enabled, can_use_desktop_power_features,
        last_validated_at_utc, active_device_count, last_activity_at_utc, updated_at_utc
    )
    SELECT
        a.user_id,
        a.email,
        CASE
            WHEN a.premium_available_credits > 0 THEN 'premium'
            WHEN a.pro_available_credits > 0 THEN 'pro_byo'
            ELSE 'free'
        END,
        CASE
            WHEN a.premium_available_credits > 0 THEN 'Premium'
            WHEN a.pro_available_credits > 0 THEN 'Pro BYO'
            ELSE 'Free'
        END,
        a.phone_verified,
        a.pro_available_credits,
        a.premium_available_credits,
        a.premium_negative_credits,
        a.lease_expires_at_utc,
        a.offline_mode_enabled,
        a.can_use_desktop_power_features,
        a.last_validated_at_utc,
        COALESCE((
            SELECT COUNT(*)
            FROM dashboard_device_inventory d
            WHERE d.user_id = a.user_id
        ), 0),
        (
            SELECT MAX(h.created_at_utc)
            FROM dashboard_wallet_history h
            WHERE h.user_id = a.user_id
        ),
        NOW()
    FROM desktop_accounts a
    WHERE a.user_id = p_user_id
    ON CONFLICT (user_id) DO UPDATE SET
        email = EXCLUDED.email,
        effective_access_tier = EXCLUDED.effective_access_tier,
        plan_label = EXCLUDED.plan_label,
        phone_verified = EXCLUDED.phone_verified,
        pro_available_credits = EXCLUDED.pro_available_credits,
        premium_available_credits = EXCLUDED.premium_available_credits,
        premium_negative_credits = EXCLUDED.premium_negative_credits,
        lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
        offline_mode_enabled = EXCLUDED.offline_mode_enabled,
        can_use_desktop_power_features = EXCLUDED.can_use_desktop_power_features,
        last_validated_at_utc = EXCLUDED.last_validated_at_utc,
        active_device_count = EXCLUDED.active_device_count,
        last_activity_at_utc = EXCLUDED.last_activity_at_utc,
        updated_at_utc = EXCLUDED.updated_at_utc;
END;
$$ LANGUAGE plpgsql;

SELECT refresh_dashboard_account_summary(user_id) FROM desktop_accounts;
";

    private const string CoreSchemaSql = @"
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
    terms_accepted_at_utc TIMESTAMPTZ NULL,
    terms_version TEXT NOT NULL DEFAULT '',
    pro_available_credits NUMERIC(18,2) NOT NULL,
    premium_available_credits NUMERIC(18,2) NOT NULL,
    premium_negative_credits NUMERIC(18,2) NOT NULL,
    lease_expires_at_utc TIMESTAMPTZ NOT NULL,
    offline_mode_enabled BOOLEAN NOT NULL,
    can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE,
    manual_lock_expires_at_utc TIMESTAMPTZ NULL,
    manual_lock_reason TEXT NOT NULL DEFAULT '',
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
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS terms_accepted_at_utc TIMESTAMPTZ NULL;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS terms_version TEXT NOT NULL DEFAULT '';
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS can_use_desktop_power_features BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS manual_lock_expires_at_utc TIMESTAMPTZ NULL;
ALTER TABLE desktop_accounts
    ADD COLUMN IF NOT EXISTS manual_lock_reason TEXT NOT NULL DEFAULT '';

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

CREATE TABLE IF NOT EXISTS user_password_reset_tokens (
    token_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    consumed BOOLEAN NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL,
    delivery_status TEXT NOT NULL,
    delivery_error TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_user_password_reset_tokens_email_time
    ON user_password_reset_tokens(email, created_at_utc DESC);

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

DELETE FROM interview_locks a
USING interview_locks b
WHERE a.user_id = b.user_id
  AND a.session_id <> b.session_id
  AND a.expires_at_utc < b.expires_at_utc;

CREATE INDEX IF NOT EXISTS idx_interview_locks_user_id ON interview_locks(user_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_interview_locks_user_unique
    ON interview_locks(user_id);

CREATE TABLE IF NOT EXISTS usage_ledger (
    ledger_entry_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    session_id TEXT NOT NULL UNIQUE,
    started_at_utc TIMESTAMPTZ NOT NULL,
    ended_at_utc TIMESTAMPTZ NOT NULL,
    charged_credits NUMERIC(18,2) NOT NULL,
    charged_blocks INTEGER NOT NULL,
    charged_pro_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
    charged_premium_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
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

CREATE TABLE IF NOT EXISTS support_tickets (
    ticket_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    subject TEXT NOT NULL,
    category TEXT NOT NULL,
    priority TEXT NOT NULL,
    description TEXT NOT NULL,
    status TEXT NOT NULL,
    admin_notes TEXT NOT NULL DEFAULT '',
    resolution_summary TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    resolved_at_utc TIMESTAMPTZ NULL,
    last_admin_action_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_support_tickets_user_updated
    ON support_tickets(user_id, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_status_updated
    ON support_tickets(status, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_email_updated
    ON support_tickets(lower(email), updated_at_utc DESC);

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

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS hosted_knowledge_bases (
    knowledge_base_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    description TEXT NOT NULL,
    status TEXT NOT NULL,
    embedding_model TEXT NOT NULL DEFAULT '',
    embedding_version INTEGER NOT NULL DEFAULT 0,
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
    extracted_text TEXT NOT NULL DEFAULT '',
    content_sha256 TEXT NOT NULL DEFAULT '',
    embedding_model TEXT NOT NULL DEFAULT '',
    embedding_version INTEGER NOT NULL DEFAULT 0,
    character_count INTEGER NOT NULL,
    chunk_count INTEGER NOT NULL,
    status TEXT NOT NULL,
    error TEXT NOT NULL,
    uploaded_at_utc TIMESTAMPTZ NOT NULL,
    processed_at_utc TIMESTAMPTZ NULL,
    indexed_at_utc TIMESTAMPTZ NULL
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
    section_title TEXT NOT NULL DEFAULT '',
    text TEXT NOT NULL,
    search_text TEXT NOT NULL,
    embedding_json JSONB NOT NULL,
    content_sha256 TEXT NOT NULL DEFAULT '',
    metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    embedding_model TEXT NOT NULL DEFAULT '',
    embedding_version INTEGER NOT NULL DEFAULT 0,
    embedding vector NULL,
    token_count INTEGER NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    indexed_at_utc TIMESTAMPTZ NULL
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

CREATE TABLE IF NOT EXISTS hosted_kb_embedding_config (
    config_id TEXT PRIMARY KEY,
    is_enabled BOOLEAN NOT NULL,
    provider_id TEXT NOT NULL,
    base_url TEXT NOT NULL,
    model_id TEXT NOT NULL,
    dimensions INTEGER NOT NULL,
    version INTEGER NOT NULL,
    batch_size INTEGER NOT NULL,
    encrypted_api_key TEXT NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);
";

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
);

CREATE TABLE IF NOT EXISTS dashboard_projection_outbox (
    change_id BIGSERIAL PRIMARY KEY,
    entity_type TEXT NOT NULL,
    operation TEXT NOT NULL,
    payload_json JSONB NOT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_dashboard_projection_outbox_change_id
    ON dashboard_projection_outbox(change_id);

CREATE OR REPLACE FUNCTION refresh_dashboard_account_summary(p_user_id TEXT)
RETURNS VOID AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM desktop_accounts WHERE user_id = p_user_id) THEN
        DELETE FROM dashboard_account_summaries WHERE user_id = p_user_id;
        RETURN;
    END IF;

    INSERT INTO dashboard_account_summaries (
        user_id, email, effective_access_tier, plan_label, phone_verified,
        pro_available_credits, premium_available_credits, premium_negative_credits,
        lease_expires_at_utc, offline_mode_enabled, can_use_desktop_power_features, last_validated_at_utc,
        active_device_count, last_activity_at_utc, updated_at_utc
    )
    SELECT
        a.user_id,
        a.email,
        CASE
            WHEN a.premium_available_credits > 0 THEN 'premium'
            WHEN a.pro_available_credits > 0 THEN 'pro_byo'
            ELSE 'free'
        END AS effective_access_tier,
        CASE
            WHEN a.premium_available_credits > 0 THEN 'Premium'
            WHEN a.pro_available_credits > 0 THEN 'Pro BYO'
            ELSE 'Free'
        END AS plan_label,
        a.phone_verified,
        a.pro_available_credits,
        a.premium_available_credits,
        a.premium_negative_credits,
        a.lease_expires_at_utc,
        a.offline_mode_enabled,
        a.can_use_desktop_power_features,
        a.last_validated_at_utc,
        COALESCE((
            SELECT COUNT(*)
            FROM dashboard_device_inventory d
            WHERE d.user_id = a.user_id
        ), 0),
        (
            SELECT MAX(h.created_at_utc)
            FROM dashboard_wallet_history h
            WHERE h.user_id = a.user_id
        ),
        NOW()
    FROM desktop_accounts a
    WHERE a.user_id = p_user_id
    ON CONFLICT (user_id) DO UPDATE SET
        email = EXCLUDED.email,
        effective_access_tier = EXCLUDED.effective_access_tier,
        plan_label = EXCLUDED.plan_label,
        phone_verified = EXCLUDED.phone_verified,
        pro_available_credits = EXCLUDED.pro_available_credits,
        premium_available_credits = EXCLUDED.premium_available_credits,
        premium_negative_credits = EXCLUDED.premium_negative_credits,
        lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
        offline_mode_enabled = EXCLUDED.offline_mode_enabled,
        can_use_desktop_power_features = EXCLUDED.can_use_desktop_power_features,
        last_validated_at_utc = EXCLUDED.last_validated_at_utc,
        active_device_count = EXCLUDED.active_device_count,
        last_activity_at_utc = EXCLUDED.last_activity_at_utc,
        updated_at_utc = EXCLUDED.updated_at_utc;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION refresh_dashboard_device_inventory(p_user_id TEXT)
RETURNS VOID AS $$
BEGIN
    DELETE FROM dashboard_device_inventory WHERE user_id = p_user_id;

    INSERT INTO dashboard_device_inventory (
        user_id, device_install_id, device_fingerprint_hash,
        last_authenticated_at_utc, auth_method, is_active
    )
    SELECT DISTINCT ON (s.device_install_id, s.device_fingerprint_hash)
        s.user_id,
        s.device_install_id,
        s.device_fingerprint_hash,
        s.authenticated_at_utc,
        s.auth_method,
        (s.is_authenticated = TRUE AND s.revoked_at_utc IS NULL) AS is_active
    FROM auth_sessions s
    WHERE s.user_id = p_user_id
      AND s.auth_method NOT LIKE 'admin:%'
    ORDER BY s.device_install_id, s.device_fingerprint_hash, s.authenticated_at_utc DESC;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION refresh_dashboard_support_preview(p_user_id TEXT)
RETURNS VOID AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM desktop_accounts WHERE user_id = p_user_id) THEN
        DELETE FROM dashboard_support_previews WHERE user_id = p_user_id;
        RETURN;
    END IF;

    INSERT INTO dashboard_support_previews (
        user_id, open_lock_session_id, last_usage_charge_credits, lease_expires_at_utc, updated_at_utc
    )
    SELECT
        a.user_id,
        COALESCE((
            SELECT l.session_id
            FROM interview_locks l
            WHERE l.user_id = a.user_id
            ORDER BY l.expires_at_utc DESC
            LIMIT 1
        ), ''),
        COALESCE((
            SELECT h.charged_credits
            FROM dashboard_wallet_history h
            WHERE h.user_id = a.user_id
            ORDER BY h.created_at_utc DESC
            LIMIT 1
        ), 0),
        a.lease_expires_at_utc,
        NOW()
    FROM desktop_accounts a
    WHERE a.user_id = p_user_id
    ON CONFLICT (user_id) DO UPDATE SET
        open_lock_session_id = EXCLUDED.open_lock_session_id,
        last_usage_charge_credits = EXCLUDED.last_usage_charge_credits,
        lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
        updated_at_utc = EXCLUDED.updated_at_utc;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION sync_dashboard_wallet_history_row()
RETURNS TRIGGER AS $$
DECLARE
    affected_user_id TEXT;
BEGIN
    affected_user_id := COALESCE(NEW.user_id, OLD.user_id);

    IF TG_OP = 'DELETE' THEN
        DELETE FROM dashboard_wallet_history WHERE ledger_entry_id = OLD.ledger_entry_id;
    ELSE
        DELETE FROM dashboard_wallet_history WHERE ledger_entry_id = NEW.ledger_entry_id;
        IF NEW.session_id NOT LIKE 'payment:%' THEN
            INSERT INTO dashboard_wallet_history (
                ledger_entry_id, user_id, session_id, charged_credits, charged_blocks, added_premium_debt, created_at_utc
            ) VALUES (
                NEW.ledger_entry_id, NEW.user_id, NEW.session_id, NEW.charged_credits, NEW.charged_blocks, NEW.added_premium_debt, NEW.created_at_utc
            );
        END IF;
    END IF;

    PERFORM refresh_dashboard_account_summary(affected_user_id);
    PERFORM refresh_dashboard_support_preview(affected_user_id);
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION sync_dashboard_wallet_purchase_row()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM dashboard_wallet_purchases WHERE checkout_id = OLD.checkout_id;
    ELSE
        INSERT INTO dashboard_wallet_purchases (
            checkout_id, user_id, target, pack_code, display_label,
            amount_minor, credits, premium_debt_credits_covered, status,
            client_confirmed, credited_at_utc, created_at_utc
        ) VALUES (
            NEW.checkout_id, NEW.user_id, NEW.target, NEW.pack_code, NEW.display_label,
            NEW.amount_minor, NEW.credits, NEW.premium_debt_credits_covered,
            CASE WHEN NEW.credited_at_utc IS NULL THEN NEW.status ELSE 'credited' END,
            NEW.client_confirmed, NEW.credited_at_utc, NEW.created_at_utc
        )
        ON CONFLICT (checkout_id) DO UPDATE SET
            user_id = EXCLUDED.user_id,
            target = EXCLUDED.target,
            pack_code = EXCLUDED.pack_code,
            display_label = EXCLUDED.display_label,
            amount_minor = EXCLUDED.amount_minor,
            credits = EXCLUDED.credits,
            premium_debt_credits_covered = EXCLUDED.premium_debt_credits_covered,
            status = EXCLUDED.status,
            client_confirmed = EXCLUDED.client_confirmed,
            credited_at_utc = EXCLUDED.credited_at_utc,
            created_at_utc = EXCLUDED.created_at_utc;
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION refresh_dashboard_account_from_account_trigger()
RETURNS TRIGGER AS $$
DECLARE
    affected_user_id TEXT;
BEGIN
    affected_user_id := COALESCE(NEW.user_id, OLD.user_id);
    PERFORM refresh_dashboard_account_summary(affected_user_id);
    PERFORM refresh_dashboard_support_preview(affected_user_id);
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION refresh_dashboard_account_from_auth_trigger()
RETURNS TRIGGER AS $$
DECLARE
    affected_user_id TEXT;
BEGIN
    affected_user_id := COALESCE(NEW.user_id, OLD.user_id);
    PERFORM refresh_dashboard_device_inventory(affected_user_id);
    PERFORM refresh_dashboard_account_summary(affected_user_id);
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION refresh_dashboard_support_from_lock_trigger()
RETURNS TRIGGER AS $$
DECLARE
    affected_user_id TEXT;
BEGIN
    affected_user_id := COALESCE(NEW.user_id, OLD.user_id);
    PERFORM refresh_dashboard_support_preview(affected_user_id);
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION enqueue_dashboard_projection_change()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO dashboard_projection_outbox (
        entity_type, operation, payload_json, occurred_at_utc
    ) VALUES (
        TG_TABLE_NAME,
        TG_OP,
        CASE
            WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD)
            ELSE to_jsonb(NEW)
        END,
        NOW()
    );

    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_dashboard_account_summary_from_accounts ON desktop_accounts;
CREATE TRIGGER trg_dashboard_account_summary_from_accounts
AFTER INSERT OR UPDATE OR DELETE ON desktop_accounts
FOR EACH ROW EXECUTE FUNCTION refresh_dashboard_account_from_account_trigger();

DROP TRIGGER IF EXISTS trg_dashboard_device_inventory_from_auth_sessions ON auth_sessions;
CREATE TRIGGER trg_dashboard_device_inventory_from_auth_sessions
AFTER INSERT OR UPDATE OR DELETE ON auth_sessions
FOR EACH ROW EXECUTE FUNCTION refresh_dashboard_account_from_auth_trigger();

DROP TRIGGER IF EXISTS trg_dashboard_wallet_history_from_usage_ledger ON usage_ledger;
CREATE TRIGGER trg_dashboard_wallet_history_from_usage_ledger
AFTER INSERT OR UPDATE OR DELETE ON usage_ledger
FOR EACH ROW EXECUTE FUNCTION sync_dashboard_wallet_history_row();

DROP TRIGGER IF EXISTS trg_dashboard_wallet_purchases_from_payment_orders ON payment_orders;
CREATE TRIGGER trg_dashboard_wallet_purchases_from_payment_orders
AFTER INSERT OR UPDATE OR DELETE ON payment_orders
FOR EACH ROW EXECUTE FUNCTION sync_dashboard_wallet_purchase_row();

DROP TRIGGER IF EXISTS trg_dashboard_support_preview_from_interview_locks ON interview_locks;
CREATE TRIGGER trg_dashboard_support_preview_from_interview_locks
AFTER INSERT OR UPDATE OR DELETE ON interview_locks
FOR EACH ROW EXECUTE FUNCTION refresh_dashboard_support_from_lock_trigger();

DROP TRIGGER IF EXISTS trg_dashboard_projection_outbox_account_summaries ON dashboard_account_summaries;
CREATE TRIGGER trg_dashboard_projection_outbox_account_summaries
AFTER INSERT OR UPDATE OR DELETE ON dashboard_account_summaries
FOR EACH ROW EXECUTE FUNCTION enqueue_dashboard_projection_change();

DROP TRIGGER IF EXISTS trg_dashboard_projection_outbox_wallet_history ON dashboard_wallet_history;
CREATE TRIGGER trg_dashboard_projection_outbox_wallet_history
AFTER INSERT OR UPDATE OR DELETE ON dashboard_wallet_history
FOR EACH ROW EXECUTE FUNCTION enqueue_dashboard_projection_change();

DROP TRIGGER IF EXISTS trg_dashboard_projection_outbox_wallet_purchases ON dashboard_wallet_purchases;
CREATE TRIGGER trg_dashboard_projection_outbox_wallet_purchases
AFTER INSERT OR UPDATE OR DELETE ON dashboard_wallet_purchases
FOR EACH ROW EXECUTE FUNCTION enqueue_dashboard_projection_change();

DROP TRIGGER IF EXISTS trg_dashboard_projection_outbox_device_inventory ON dashboard_device_inventory;
CREATE TRIGGER trg_dashboard_projection_outbox_device_inventory
AFTER INSERT OR UPDATE OR DELETE ON dashboard_device_inventory
FOR EACH ROW EXECUTE FUNCTION enqueue_dashboard_projection_change();

DROP TRIGGER IF EXISTS trg_dashboard_projection_outbox_support_previews ON dashboard_support_previews;
CREATE TRIGGER trg_dashboard_projection_outbox_support_previews
AFTER INSERT OR UPDATE OR DELETE ON dashboard_support_previews
FOR EACH ROW EXECUTE FUNCTION enqueue_dashboard_projection_change();

INSERT INTO dashboard_wallet_history (
    ledger_entry_id, user_id, session_id, charged_credits, charged_blocks, added_premium_debt, created_at_utc
)
SELECT
    ledger_entry_id, user_id, session_id, charged_credits, charged_blocks, added_premium_debt, created_at_utc
FROM usage_ledger
WHERE session_id NOT LIKE 'payment:%'
ON CONFLICT (ledger_entry_id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    session_id = EXCLUDED.session_id,
    charged_credits = EXCLUDED.charged_credits,
    charged_blocks = EXCLUDED.charged_blocks,
    added_premium_debt = EXCLUDED.added_premium_debt,
    created_at_utc = EXCLUDED.created_at_utc;

INSERT INTO dashboard_wallet_purchases (
    checkout_id, user_id, target, pack_code, display_label,
    amount_minor, credits, premium_debt_credits_covered, status,
    client_confirmed, credited_at_utc, created_at_utc
)
SELECT
    checkout_id, user_id, target, pack_code, display_label,
    amount_minor, credits, premium_debt_credits_covered,
    CASE WHEN credited_at_utc IS NULL THEN status ELSE 'credited' END,
    client_confirmed, credited_at_utc, created_at_utc
FROM payment_orders
ON CONFLICT (checkout_id) DO UPDATE SET
    user_id = EXCLUDED.user_id,
    target = EXCLUDED.target,
    pack_code = EXCLUDED.pack_code,
    display_label = EXCLUDED.display_label,
    amount_minor = EXCLUDED.amount_minor,
    credits = EXCLUDED.credits,
    premium_debt_credits_covered = EXCLUDED.premium_debt_credits_covered,
    status = EXCLUDED.status,
    client_confirmed = EXCLUDED.client_confirmed,
    credited_at_utc = EXCLUDED.credited_at_utc,
    created_at_utc = EXCLUDED.created_at_utc;

SELECT refresh_dashboard_device_inventory(user_id) FROM desktop_accounts;
SELECT refresh_dashboard_account_summary(user_id) FROM desktop_accounts;
SELECT refresh_dashboard_support_preview(user_id) FROM desktop_accounts;
";

    private const string DashboardProjectionReplicaSchemaSql = @"
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

    private const string OperationalIndexesSql = @"
CREATE INDEX IF NOT EXISTS idx_auth_sessions_user_authenticated
    ON auth_sessions(user_id, authenticated_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_auth_sessions_expires_at
    ON auth_sessions(expires_at_utc);
CREATE INDEX IF NOT EXISTS idx_magic_links_expires_at
    ON magic_links(expires_at_utc);
CREATE INDEX IF NOT EXISTS idx_email_verification_tokens_expires_at
    ON email_verification_tokens(expires_at_utc);
CREATE INDEX IF NOT EXISTS idx_interview_locks_expires_at
    ON interview_locks(expires_at_utc);
CREATE INDEX IF NOT EXISTS idx_telemetry_events_created_at
    ON telemetry_events(created_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_payment_webhook_events_created_at
    ON payment_webhook_events(created_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_payment_webhook_events_processed_at
    ON payment_webhook_events(processed_at_utc);
CREATE INDEX IF NOT EXISTS idx_dashboard_projection_outbox_occurred_at
    ON dashboard_projection_outbox(occurred_at_utc);";

    private const string ManagedAiRuntimeSelectionSql = @"
CREATE TABLE IF NOT EXISTS managed_ai_runtime_selection (
    selection_id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    model_id TEXT NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);";

    private const string SupportAndAuthSchemaPatchSql = @"
CREATE TABLE IF NOT EXISTS user_password_reset_tokens (
    token_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    expires_at_utc TIMESTAMPTZ NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    consumed BOOLEAN NOT NULL,
    consumed_at_utc TIMESTAMPTZ NULL,
    delivery_status TEXT NOT NULL,
    delivery_error TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_user_password_reset_tokens_email_time
    ON user_password_reset_tokens(email, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS support_tickets (
    ticket_id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    email TEXT NOT NULL,
    subject TEXT NOT NULL,
    category TEXT NOT NULL,
    priority TEXT NOT NULL,
    description TEXT NOT NULL,
    status TEXT NOT NULL,
    admin_notes TEXT NOT NULL DEFAULT '',
    resolution_summary TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    resolved_at_utc TIMESTAMPTZ NULL,
    last_admin_action_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_support_tickets_user_updated
    ON support_tickets(user_id, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_status_updated
    ON support_tickets(status, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_email_updated
    ON support_tickets(lower(email), updated_at_utc DESC);

CREATE OR REPLACE FUNCTION refresh_dashboard_device_inventory(p_user_id TEXT)
RETURNS VOID AS $$
BEGIN
    DELETE FROM dashboard_device_inventory d
    WHERE d.user_id = p_user_id
      AND NOT EXISTS (
          SELECT 1
          FROM auth_sessions s
          WHERE s.user_id = p_user_id
            AND s.auth_method NOT LIKE 'admin:%'
            AND s.device_install_id = d.device_install_id
            AND s.device_fingerprint_hash = d.device_fingerprint_hash
      );

    INSERT INTO dashboard_device_inventory (
        user_id, device_install_id, device_fingerprint_hash,
        last_authenticated_at_utc, auth_method, is_active
    )
    SELECT
        ranked.user_id,
        ranked.device_install_id,
        ranked.device_fingerprint_hash,
        ranked.authenticated_at_utc,
        ranked.auth_method,
        (ranked.is_authenticated = TRUE AND ranked.revoked_at_utc IS NULL) AS is_active
    FROM (
        SELECT
            s.user_id,
            s.device_install_id,
            s.device_fingerprint_hash,
            s.authenticated_at_utc,
            s.auth_method,
            s.is_authenticated,
            s.revoked_at_utc,
            ROW_NUMBER() OVER (
                PARTITION BY s.user_id, s.device_install_id, s.device_fingerprint_hash
                ORDER BY s.authenticated_at_utc DESC, s.session_id DESC
            ) AS row_number
        FROM auth_sessions s
        WHERE s.user_id = p_user_id
          AND s.auth_method NOT LIKE 'admin:%'
    ) ranked
    WHERE ranked.row_number = 1
    ON CONFLICT (user_id, device_install_id, device_fingerprint_hash) DO UPDATE SET
        last_authenticated_at_utc = EXCLUDED.last_authenticated_at_utc,
        auth_method = EXCLUDED.auth_method,
        is_active = EXCLUDED.is_active;
END;
$$ LANGUAGE plpgsql;
";

    private const string UsageCreditSplitSql = @"
ALTER TABLE usage_ledger
    ADD COLUMN IF NOT EXISTS charged_pro_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE usage_ledger
    ADD COLUMN IF NOT EXISTS charged_premium_credits NUMERIC(18,2) NOT NULL DEFAULT 0;

ALTER TABLE dashboard_wallet_history
    ADD COLUMN IF NOT EXISTS charged_pro_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE dashboard_wallet_history
    ADD COLUMN IF NOT EXISTS charged_premium_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
";

    private const string HostedKnowledgeBaseSearchIndexSql = @"
CREATE INDEX IF NOT EXISTS idx_hosted_kb_chunks_search_vector
    ON hosted_kb_chunks
    USING GIN (to_tsvector('simple', coalesce(document_title, '') || ' ' || search_text));
";

    private const string HostedKnowledgeBaseVectorUpgradeSql = @"
CREATE EXTENSION IF NOT EXISTS vector;

ALTER TABLE hosted_knowledge_bases
    ADD COLUMN IF NOT EXISTS embedding_model TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_knowledge_bases
    ADD COLUMN IF NOT EXISTS embedding_version INTEGER NOT NULL DEFAULT 0;

ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS extracted_text TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS content_sha256 TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS embedding_model TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS embedding_version INTEGER NOT NULL DEFAULT 0;
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS indexed_at_utc TIMESTAMPTZ NULL;

ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS section_title TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS content_sha256 TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS embedding_model TEXT NOT NULL DEFAULT '';
ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS embedding_version INTEGER NOT NULL DEFAULT 0;
ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS indexed_at_utc TIMESTAMPTZ NULL;
ALTER TABLE hosted_kb_chunks
    ADD COLUMN IF NOT EXISTS embedding vector(1536);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_documents_status
    ON hosted_kb_documents(knowledge_base_id, status, uploaded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_chunks_indexed
    ON hosted_kb_chunks(knowledge_base_id, indexed_at_utc, document_id, chunk_index);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_chunks_embedding_profile
    ON hosted_kb_chunks(knowledge_base_id, embedding_model, embedding_version, indexed_at_utc, document_id, chunk_index);
";

    private const string HostedKnowledgeBaseEmbeddingAdminConfigSql = @"
CREATE TABLE IF NOT EXISTS hosted_kb_embedding_config (
    config_id TEXT PRIMARY KEY,
    is_enabled BOOLEAN NOT NULL,
    provider_id TEXT NOT NULL,
    base_url TEXT NOT NULL,
    model_id TEXT NOT NULL,
    dimensions INTEGER NOT NULL,
    version INTEGER NOT NULL,
    batch_size INTEGER NOT NULL,
    encrypted_api_key TEXT NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);
";

    private const string HostedKnowledgeBaseReindexJobsSql = @"
CREATE TABLE IF NOT EXISTS hosted_kb_reindex_jobs (
    job_id TEXT PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    status TEXT NOT NULL,
    error TEXT NOT NULL DEFAULT '',
    target_embedding_model TEXT NOT NULL DEFAULT '',
    target_embedding_version INTEGER NOT NULL DEFAULT 0,
    total_documents INTEGER NOT NULL DEFAULT 0,
    processed_documents INTEGER NOT NULL DEFAULT 0,
    requested_at_utc TIMESTAMPTZ NOT NULL,
    started_at_utc TIMESTAMPTZ NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_reindex_jobs_kb_requested
    ON hosted_kb_reindex_jobs(knowledge_base_id, requested_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_reindex_jobs_status_requested
    ON hosted_kb_reindex_jobs(status, requested_at_utc ASC);

CREATE UNIQUE INDEX IF NOT EXISTS idx_hosted_kb_reindex_jobs_active_kb
    ON hosted_kb_reindex_jobs(knowledge_base_id)
    WHERE status IN ('queued', 'running');
";

    private const string HostedKnowledgeBaseVariableEmbeddingDimensionsSql = @"
DROP INDEX IF EXISTS idx_hosted_kb_chunks_embedding_hnsw;

ALTER TABLE hosted_kb_chunks
    ALTER COLUMN embedding TYPE vector
    USING embedding::vector;

CREATE INDEX IF NOT EXISTS idx_hosted_kb_chunks_embedding_profile
    ON hosted_kb_chunks(knowledge_base_id, embedding_model, embedding_version, indexed_at_utc, document_id, chunk_index);
";

    private static readonly string HostedKnowledgeBaseOnlineHnswIndexSql = @"
CREATE INDEX IF NOT EXISTS idx_hosted_kb_chunks_embedding_hnsw_default
    ON hosted_kb_chunks
    USING hnsw ((CAST(embedding AS vector(" + HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions + @"))) vector_cosine_ops)
    WHERE indexed_at_utc IS NOT NULL
      AND embedding IS NOT NULL
      AND vector_dims(embedding) = " + HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions + @";
";

    private const string HostedKnowledgeBaseTypedMemorySql = @"
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS section TEXT NOT NULL DEFAULT 'general_reference';
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS source_kind TEXT NOT NULL DEFAULT 'upload';
ALTER TABLE hosted_kb_documents
    ADD COLUMN IF NOT EXISTS source_label TEXT NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS idx_hosted_kb_documents_section_uploaded
    ON hosted_kb_documents(knowledge_base_id, section, uploaded_at_utc DESC);

CREATE TABLE IF NOT EXISTS hosted_kb_profile_cards (
    profile_card_id TEXT PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    full_name TEXT NOT NULL DEFAULT '',
    resume_text TEXT NOT NULL DEFAULT '',
    short_intro TEXT NOT NULL DEFAULT '',
    current_role_text TEXT NOT NULL DEFAULT '',
    years_of_experience INTEGER NOT NULL DEFAULT 0,
    strengths_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    skills_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    domains_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    source_document_ids_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_hosted_kb_profile_cards_kb
    ON hosted_kb_profile_cards(knowledge_base_id);

CREATE TABLE IF NOT EXISTS hosted_kb_project_cards (
    project_card_id TEXT PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    title TEXT NOT NULL DEFAULT '',
    slug TEXT NOT NULL DEFAULT '',
    is_recent BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    role TEXT NOT NULL DEFAULT '',
    summary TEXT NOT NULL DEFAULT '',
    stack_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    architecture TEXT NOT NULL DEFAULT '',
    challenges TEXT NOT NULL DEFAULT '',
    impact TEXT NOT NULL DEFAULT '',
    source_document_ids_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_project_cards_kb_recent_order
    ON hosted_kb_project_cards(knowledge_base_id, is_recent DESC, sort_order ASC, updated_at_utc DESC);
";

    private const string HostedKnowledgeBaseExperienceCardsSql = @"
CREATE TABLE IF NOT EXISTS hosted_kb_experience_cards (
    experience_card_id TEXT PRIMARY KEY,
    knowledge_base_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    company TEXT NOT NULL DEFAULT '',
    role TEXT NOT NULL DEFAULT '',
    is_current BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    start_date TEXT NOT NULL DEFAULT '',
    end_date TEXT NOT NULL DEFAULT '',
    summary TEXT NOT NULL DEFAULT '',
    responsibilities TEXT NOT NULL DEFAULT '',
    skills_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    source_document_ids_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_hosted_kb_experience_cards_kb_order
    ON hosted_kb_experience_cards(knowledge_base_id, is_current DESC, sort_order ASC, updated_at_utc DESC);

CREATE UNIQUE INDEX IF NOT EXISTS idx_hosted_kb_experience_cards_one_current
    ON hosted_kb_experience_cards(knowledge_base_id)
    WHERE is_current;
";

    private const string SingleExperienceCurrentSql = @"
UPDATE hosted_kb_experience_cards experience
SET is_current = TRUE,
    end_date = '',
    updated_at_utc = NOW()
WHERE NOT experience.is_current
  AND 1 = (
      SELECT COUNT(*)
      FROM hosted_kb_experience_cards sibling
      WHERE sibling.knowledge_base_id = experience.knowledge_base_id
  );
";

    private const string DashboardProjectionUsageCreditSplitSql = @"
ALTER TABLE dashboard_wallet_history
    ADD COLUMN IF NOT EXISTS charged_pro_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE dashboard_wallet_history
    ADD COLUMN IF NOT EXISTS charged_premium_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
";

    private const string ManagedAiLatencyChecksSql = @"
CREATE TABLE IF NOT EXISTS managed_ai_latency_runs (
    job_id TEXT PRIMARY KEY,
    status TEXT NOT NULL,
    total_models INTEGER NOT NULL DEFAULT 0,
    processed_models INTEGER NOT NULL DEFAULT 0,
    error TEXT NOT NULL DEFAULT '',
    requested_at_utc TIMESTAMPTZ NOT NULL,
    started_at_utc TIMESTAMPTZ NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_managed_ai_latency_runs_requested
    ON managed_ai_latency_runs(requested_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_managed_ai_latency_runs_status_requested
    ON managed_ai_latency_runs(status, requested_at_utc ASC);

CREATE UNIQUE INDEX IF NOT EXISTS idx_managed_ai_latency_runs_active_singleton
    ON managed_ai_latency_runs((status))
    WHERE status IN ('queued', 'running');

CREATE TABLE IF NOT EXISTS managed_ai_model_latency_status (
    provider_id TEXT NOT NULL,
    model_id TEXT NOT NULL,
    model_display_name TEXT NOT NULL,
    supports_vision BOOLEAN NOT NULL DEFAULT FALSE,
    is_chat_capable BOOLEAN NULL,
    status TEXT NOT NULL,
    message TEXT NOT NULL DEFAULT '',
    latency_ms INTEGER NULL,
    checked_at_utc TIMESTAMPTZ NULL,
    last_job_id TEXT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (provider_id, model_id)
);

CREATE INDEX IF NOT EXISTS idx_managed_ai_model_latency_status_checked
    ON managed_ai_model_latency_status(checked_at_utc DESC, provider_id, model_id);
";

    private const string InterviewQuestionBanksSql = @"
CREATE TABLE IF NOT EXISTS interview_question_bank_jobs (
    job_id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL UNIQUE,
    user_id TEXT NOT NULL,
    raw_question_inputs_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    questions_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    status TEXT NOT NULL,
    error TEXT NOT NULL DEFAULT '',
    interview_started_at_utc TIMESTAMPTZ NOT NULL,
    interview_ended_at_utc TIMESTAMPTZ NOT NULL,
    requested_at_utc TIMESTAMPTZ NOT NULL,
    started_at_utc TIMESTAMPTZ NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_interview_question_bank_jobs_status_requested
    ON interview_question_bank_jobs(status, requested_at_utc ASC);

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

DROP TRIGGER IF EXISTS trg_dashboard_projection_outbox_interview_question_banks ON dashboard_interview_question_banks;
CREATE TRIGGER trg_dashboard_projection_outbox_interview_question_banks
AFTER INSERT OR UPDATE OR DELETE ON dashboard_interview_question_banks
FOR EACH ROW EXECUTE FUNCTION enqueue_dashboard_projection_change();
";

    private const string DashboardInterviewQuestionBanksSql = @"
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
ALTER TABLE interview_question_bank_jobs
    ADD COLUMN IF NOT EXISTS interview_name TEXT NOT NULL DEFAULT '';
ALTER TABLE dashboard_interview_question_banks
    ADD COLUMN IF NOT EXISTS interview_name TEXT NOT NULL DEFAULT '';
";

    private const string DashboardInterviewQuestionBankNamesSql = @"
ALTER TABLE dashboard_interview_question_banks
    ADD COLUMN IF NOT EXISTS interview_name TEXT NOT NULL DEFAULT '';
";
}
