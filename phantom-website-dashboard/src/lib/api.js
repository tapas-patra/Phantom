const DASHBOARD_API_BASE =
  import.meta.env.VITE_PHANTOM_DASHBOARD_API_BASE_URL?.replace(/\/$/, "") ||
  "http://localhost:5067";

const WINDOWS_BACKEND_API_BASE =
  import.meta.env.VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL?.replace(/\/$/, "") ||
  "http://localhost:5057";

const BROWSER_DEVICE_STORAGE_KEY = "phantom.website.device-profile";

async function request(baseUrl, path, init) {
  const isFormData = typeof FormData !== "undefined" && init?.body instanceof FormData;
  const response = await fetch(`${baseUrl}${path}`, {
    credentials: "include",
    ...init,
    headers: {
      ...(isFormData ? {} : { "Content-Type": "application/json" }),
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

function authHeaders(accessToken) {
  return accessToken
    ? {
        Authorization: `Bearer ${accessToken}`
      }
    : {};
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

export async function refreshAccountSession(refreshToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/refresh", {
    method: "POST",
    body: JSON.stringify({
      refreshToken,
      ...getBrowserDeviceProfile()
    })
  });
}

export async function fetchCurrentUserSession() {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/me");
}

export async function logoutAccount(refreshToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/logout", {
    method: "POST",
    body: JSON.stringify({ refreshToken })
  });
}

export async function loginAdmin(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/login", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function refreshAdminSession(refreshToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/refresh", {
    method: "POST",
    body: JSON.stringify({ refreshToken })
  });
}

export async function fetchCurrentAdminSession() {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/me");
}

export async function logoutAdmin(refreshToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/logout", {
    method: "POST",
    body: JSON.stringify({ refreshToken })
  });
}

export async function requestAdminPasswordReset(email) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/forgot-password", {
    method: "POST",
    body: JSON.stringify({ email })
  });
}

export async function resetAdminPassword(token, newPassword) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/reset-password", {
    method: "POST",
    body: JSON.stringify({ token, newPassword })
  });
}

export async function registerAccount(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/register", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function sendPhoneOtp(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/phone/send-otp", {
    method: "POST",
    body: JSON.stringify({
      ...getBrowserDeviceProfile(),
      ...payload
    })
  });
}

export async function verifyPhoneOtp(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/phone/verify-otp", {
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

export async function fetchAccountSummary(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/account-summary", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchWalletHistory(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/wallet-history", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchWalletPurchases(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/wallet-purchases", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchDevices(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/devices", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchDownloadEntitlement(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/download-entitlement", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchSupportOverview(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/support/preview", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminOverview(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/overview", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminUsers(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/accounts", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminUser(accessToken, userId) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/accounts/${encodeURIComponent(userId)}`, {
    headers: authHeaders(accessToken)
  });
}

export async function updateAdminUser(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/accounts/update", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function grantAdminCredits(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/credits/grant", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function waiveAdminPremiumDebt(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/balance/waive-negative-premium", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function clearAdminLock(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/locks/clear", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function fetchManagedAiAdminInventory(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/credentials", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminPaymentOrders(accessToken, limit = 100) {
  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/admin/payments/orders?limit=${encodeURIComponent(limit)}`,
    {
      headers: authHeaders(accessToken)
    }
  );
}

export async function fetchAdminPaymentWebhooks(accessToken, limit = 100) {
  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/admin/payments/webhooks?limit=${encodeURIComponent(limit)}`,
    {
      headers: authHeaders(accessToken)
    }
  );
}

export async function fetchGmailOAuthStatus(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/integrations/gmail/oauth/status", {
    headers: authHeaders(accessToken)
  });
}

export async function startGmailOAuth(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/integrations/gmail/oauth/start", {
    method: "POST",
    headers: authHeaders(accessToken)
  });
}

export async function upsertManagedAiCredential(accessToken, payload) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/credentials", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function triggerManagedAiCatalogRefresh(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/catalog/refresh", {
    method: "POST",
    headers: authHeaders(accessToken)
  });
}

export async function deleteManagedAiCredential(accessToken, credentialId) {
  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/admin/managed-ai/credentials/${encodeURIComponent(credentialId)}`,
    {
      method: "DELETE",
      headers: authHeaders(accessToken)
    }
  );
}

export async function updateManagedAiModelVision(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/managed-ai/catalog/vision", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function fetchHostedKnowledgeBase(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb", {
    headers: authHeaders(accessToken)
  });
}

export async function createHostedKnowledgeBase(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function uploadHostedKnowledgeBaseDocuments(accessToken, files) {
  const formData = new FormData();
  Array.from(files || []).forEach((file) => {
    formData.append("files", file);
  });

  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/documents", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: formData
  });
}

export async function fetchPaymentCatalog(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/payments/catalog", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchPaymentOrders(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/payments/orders", {
    headers: authHeaders(accessToken)
  });
}

export async function createPaymentCheckout(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/payments/checkout", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function confirmPaymentCheckout(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/payments/client-confirm", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}
