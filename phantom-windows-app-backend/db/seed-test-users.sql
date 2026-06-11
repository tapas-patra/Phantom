CREATE TABLE IF NOT EXISTS desktop_accounts (
    user_id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    access_tier TEXT NOT NULL DEFAULT 'free',
    password_hash TEXT NOT NULL,
    phone_verified BOOLEAN NOT NULL DEFAULT FALSE,
    pro_available_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
    premium_available_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
    premium_negative_credits NUMERIC(18,2) NOT NULL DEFAULT 0,
    lease_expires_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW() + INTERVAL '24 hours',
    offline_mode_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    last_validated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS access_tier TEXT NOT NULL DEFAULT 'free';
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS password_hash TEXT NOT NULL DEFAULT '';
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS phone_verified BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS pro_available_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS premium_available_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS premium_negative_credits NUMERIC(18,2) NOT NULL DEFAULT 0;
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS lease_expires_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW() + INTERVAL '24 hours';
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS offline_mode_enabled BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS last_validated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE desktop_accounts ADD COLUMN IF NOT EXISTS updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW();

INSERT INTO desktop_accounts (
    user_id,
    email,
    access_tier,
    password_hash,
    phone_verified,
    pro_available_credits,
    premium_available_credits,
    premium_negative_credits,
    lease_expires_at_utc,
    offline_mode_enabled,
    last_validated_at_utc,
    created_at_utc,
    updated_at_utc
)
VALUES
(
    'free.user@phantom.app',
    'free.user@phantom.app',
    'free',
    'pbkdf2-sha256$120000$3PianhKVoXGAWQ5kY/tjaQ==$HJCk0VlHUExBveHqU5ZynAFC8uZPrGofvjL7nPmxlPI=',
    TRUE,
    0.00,
    0.00,
    0.00,
    NOW() + INTERVAL '24 hours',
    FALSE,
    NOW(),
    NOW(),
    NOW()
),
(
    'pro.user@phantom.app',
    'pro.user@phantom.app',
    'pro_byo',
    'pbkdf2-sha256$120000$Kfp4L4HiRaxO1KVxJM96Nw==$ucoNS3p7L1BcIuwRlSE5VCUhu/C3H6jlaZQp5EcP3bI=',
    TRUE,
    5.00,
    0.00,
    0.00,
    NOW() + INTERVAL '24 hours',
    FALSE,
    NOW(),
    NOW(),
    NOW()
),
(
    'premium.user@phantom.app',
    'premium.user@phantom.app',
    'premium',
    'pbkdf2-sha256$120000$2wNflBQCx67mA3R1XURjDg==$Hkw07WGMPtOIXAzRpZTKnotSqYMEQbkv8eDgiyT3QC8=',
    TRUE,
    5.00,
    5.00,
    0.00,
    NOW() + INTERVAL '24 hours',
    FALSE,
    NOW(),
    NOW(),
    NOW()
)
ON CONFLICT (user_id) DO UPDATE SET
    email = EXCLUDED.email,
    access_tier = EXCLUDED.access_tier,
    password_hash = EXCLUDED.password_hash,
    phone_verified = EXCLUDED.phone_verified,
    pro_available_credits = EXCLUDED.pro_available_credits,
    premium_available_credits = EXCLUDED.premium_available_credits,
    last_validated_at_utc = NOW(),
    updated_at_utc = NOW();
