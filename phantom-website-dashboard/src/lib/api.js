const DASHBOARD_API_BASE =
  import.meta.env.VITE_PHANTOM_DASHBOARD_API_BASE_URL?.replace(/\/$/, "") ||
  "http://localhost:5067";

const WINDOWS_BACKEND_API_BASE =
  import.meta.env.VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL?.replace(/\/$/, "") ||
  "http://localhost:5057";

const BROWSER_DEVICE_STORAGE_KEY = "phantom.website.device-profile";

async function request(baseUrl, path, init) {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers || {})
    }
  });

  if (!response.ok) {
    let message = `Request failed: ${response.status}`;
    try {
      const payload = await response.json();
      if (payload?.error) {
        message = payload.error;
      } else if (payload?.detail) {
        message = payload.detail;
      }
    } catch {
      // Ignore parse failures.
    }

    throw new Error(message);
  }

  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes("application/json")) {
    return null;
  }

  return response.json();
}

function getBrowserDeviceProfile() {
  const existing = window.localStorage.getItem(BROWSER_DEVICE_STORAGE_KEY);
  if (existing) {
    return JSON.parse(existing);
  }

  const installId = `web-${crypto.randomUUID()}`;
  const deviceProfile = {
    appVersion: "phantom-website-dashboard",
    installId,
    deviceLabel: "Browser Dashboard",
    deviceFingerprintHash: `browser-${installId}`,
    secretFingerprintHint: "browser"
  };

  window.localStorage.setItem(BROWSER_DEVICE_STORAGE_KEY, JSON.stringify(deviceProfile));
  return deviceProfile;
}

export async function loginAccount(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/login", {
    method: "POST",
    body: JSON.stringify({
      ...getBrowserDeviceProfile(),
      ...payload
    })
  });
}

export async function logoutAccount(refreshToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/logout", {
    method: "POST",
    body: JSON.stringify({ refreshToken })
  });
}

export async function registerAccount(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/register", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function resendVerificationEmail(email) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/verify-email/request", {
    method: "POST",
    body: JSON.stringify({ email })
  });
}

export async function fetchAccountSummary(email) {
  if (!email) {
    return null;
  }

  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/account-summary?email=${encodeURIComponent(email)}`
  );
}

export async function fetchWalletHistory(userId) {
  if (!userId) {
    return [];
  }

  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/wallet-history?userId=${encodeURIComponent(userId)}`
  );
}

export async function fetchDevices(userId) {
  if (!userId) {
    return [];
  }

  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/devices?userId=${encodeURIComponent(userId)}`
  );
}

export async function fetchDownloadEntitlement(userId) {
  if (!userId) {
    return null;
  }

  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/download-entitlement?userId=${encodeURIComponent(userId)}`
  );
}

export async function fetchSupportOverview(userId) {
  if (!userId) {
    return null;
  }

  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/support/preview?userId=${encodeURIComponent(userId)}`
  );
}

export async function fetchAdminOverview(adminApiKey) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/overview", {
    headers: {
      "X-Phantom-Admin-Key": adminApiKey
    }
  });
}

export async function fetchManagedAiAdminInventory(adminApiKey) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/credentials", {
    headers: {
      "X-Phantom-Admin-Key": adminApiKey
    }
  });
}

export async function upsertManagedAiCredential(adminApiKey, payload) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/credentials", {
    method: "POST",
    headers: {
      "X-Phantom-Admin-Key": adminApiKey
    },
    body: JSON.stringify(payload)
  });
}

export async function triggerManagedAiCatalogRefresh(adminApiKey) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/catalog/refresh", {
    method: "POST",
    headers: {
      "X-Phantom-Admin-Key": adminApiKey
    }
  });
}

export async function deleteManagedAiCredential(adminApiKey, credentialId) {
  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/admin/managed-ai/credentials/${encodeURIComponent(credentialId)}`,
    {
      method: "DELETE",
      headers: {
        "X-Phantom-Admin-Key": adminApiKey
      }
    }
  );
}
