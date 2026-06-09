const DASHBOARD_API_BASE =
  import.meta.env.VITE_PHANTOM_DASHBOARD_API_BASE_URL?.replace(/\/$/, "") ||
  "http://localhost:5067";

async function request(path) {
  const response = await fetch(`${DASHBOARD_API_BASE}${path}`);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }

  return response.json();
}

export async function fetchAccountSummary(email) {
  if (!email) {
    return null;
  }

  try {
    return await request(`/api/dashboard/account-summary?email=${encodeURIComponent(email)}`);
  } catch {
    return {
      userId: email,
      email,
      planLabel: email.startsWith("premium") ? "Premium" : email.startsWith("pro") ? "Pro BYO" : "Free",
      phoneVerified: true,
      proAvailableCredits: email.startsWith("free") ? 0 : 5,
      premiumAvailableCredits: email.startsWith("premium") ? 5 : 0,
      premiumNegativeCredits: 0,
      leaseExpiresAtUtc: new Date(Date.now() + 18 * 60 * 60 * 1000).toISOString(),
      activeDeviceCount: email.startsWith("premium") ? 3 : 1,
      lastActivityAtUtc: new Date().toISOString()
    };
  }
}

export async function fetchWalletHistory(userId) {
  if (!userId) {
    return [];
  }

  try {
    return await request(`/api/dashboard/wallet-history?userId=${encodeURIComponent(userId)}`);
  } catch {
    return [
      {
        ledgerEntryId: "demo-ledger-1",
        sessionId: "interview-2026-001",
        chargedCredits: 0.5,
        chargedBlocks: 2,
        addedPremiumDebt: 0,
        createdAtUtc: new Date().toISOString()
      },
      {
        ledgerEntryId: "demo-ledger-2",
        sessionId: "interview-2026-002",
        chargedCredits: 1,
        chargedBlocks: 4,
        addedPremiumDebt: 0,
        createdAtUtc: new Date(Date.now() - 86400000).toISOString()
      }
    ];
  }
}

export async function fetchDevices(userId) {
  if (!userId) {
    return [];
  }

  try {
    return await request(`/api/dashboard/devices?userId=${encodeURIComponent(userId)}`);
  } catch {
    return [
      {
        deviceInstallId: "device-alpha",
        deviceFingerprintHash: "fp-live-01",
        lastAuthenticatedAtUtc: new Date().toISOString(),
        authMethod: "password",
        isActive: true
      }
    ];
  }
}

export async function fetchDownloadEntitlement(userId) {
  if (!userId) {
    return null;
  }

  try {
    return await request(`/api/dashboard/download-entitlement?userId=${encodeURIComponent(userId)}`);
  } catch {
    return {
      canDownload: true,
      installerLabel: "Phantom Desktop for Windows",
      installerVersion: "0.9.0-preview",
      installerUrl: "#download",
      releaseChannel: "Hosted Preview"
    };
  }
}

export async function fetchSupportOverview(userId) {
  if (!userId) {
    return null;
  }

  try {
    return await request(`/api/dashboard/support/preview?userId=${encodeURIComponent(userId)}`);
  } catch {
    return {
      openLockSessionId: "session-alpha",
      lastUsageChargeCredits: 0.5,
      offlineLeaseHoursRemaining: 18,
      supportMessage: "Support tools become live when dashboard backend admin keys and payment events are connected."
    };
  }
}
