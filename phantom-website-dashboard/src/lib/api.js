const HOSTED_DASHBOARD_API_BASE = "/api/dashboard-backend";
const HOSTED_WINDOWS_BACKEND_API_BASE = "/api/windows";
const viteEnvironment = import.meta.env || {};
const DASHBOARD_API_BASE =
  viteEnvironment.VITE_PHANTOM_DASHBOARD_API_BASE_URL?.replace(/\/$/, "") ||
  HOSTED_DASHBOARD_API_BASE;

const WINDOWS_BACKEND_API_BASE =
  viteEnvironment.VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL?.replace(/\/$/, "") ||
  HOSTED_WINDOWS_BACKEND_API_BASE;

const BROWSER_DEVICE_STORAGE_KEY = "phantom.website.device-profile";

const DEFAULT_REQUEST_TIMEOUT_MS = 15_000;

export async function request(baseUrl, path, init = {}) {
  const method = (init.method || "GET").toUpperCase();
  const retries = ["GET", "HEAD"].includes(method) ? Math.max(0, init.retries ?? 1) : 0;
  const correlationId = init.correlationId || crypto.randomUUID();

  for (let attempt = 0; ; attempt += 1) {
    try {
      return await requestOnce(baseUrl, path, { ...init, correlationId });
    } catch (error) {
      if (init.signal?.aborted || attempt >= retries || !error?.retryable) throw error;
      await new Promise((resolve) => globalThis.setTimeout(resolve, 250 * (attempt + 1)));
    }
  }
}

async function requestOnce(baseUrl, path, init) {
  const isFormData = typeof FormData !== "undefined" && init?.body instanceof FormData;
  const correlationId = init.correlationId;
  const controller = new AbortController();
  const method = (init.method || "GET").toUpperCase();
  const isUnsafeMethod = !["GET", "HEAD", "OPTIONS"].includes(method);
  const timeoutId = globalThis.setTimeout(() => controller.abort("timeout"), init.timeoutMs || DEFAULT_REQUEST_TIMEOUT_MS);
  const externalSignal = init.signal;
  const abortFromCaller = () => controller.abort(externalSignal?.reason || "cancelled");
  if (externalSignal?.aborted) abortFromCaller();
  else externalSignal?.addEventListener("abort", abortFromCaller, { once: true });
  const { timeoutMs: _timeoutMs, correlationId: _correlationId, retries: _retries, signal: _signal, ...fetchInit } = init;
  let response;

  try {
    response = await fetch(`${baseUrl}${path}`, {
      credentials: "include",
      ...fetchInit,
      signal: controller.signal,
      headers: {
        ...(isFormData ? {} : { "Content-Type": "application/json" }),
        "X-Phantom-Correlation-Id": correlationId,
        "X-Phantom-Operation-Id": crypto.randomUUID(),
        ...(isUnsafeMethod ? { "X-Phantom-CSRF": "1" } : {}),
        ...(init.headers || {})
      }
    });
  } catch (error) {
    if (error?.name === "AbortError") {
      if (externalSignal?.aborted) throw error;
      const timeoutError = new Error("The request timed out. Please try again.");
      timeoutError.code = "request_timeout";
      timeoutError.retryable = true;
      throw timeoutError;
    }
    const networkError = new Error("Could not reach Phantom. Check your connection and try again.");
    networkError.code = "network_unavailable";
    networkError.retryable = true;
    networkError.cause = error;
    throw networkError;
  } finally {
    globalThis.clearTimeout(timeoutId);
    externalSignal?.removeEventListener("abort", abortFromCaller);
  }

  if (!response.ok) {
    const acceptedCorrelationId = response.headers.get("X-Phantom-Correlation-Id") || correlationId;
    const contentType = response.headers.get("content-type") || "";
    const responseBody = await response.text();
    let serverError = "";
    if (responseBody) {
      if (contentType.includes("application/json")) {
        try {
          const parsed = JSON.parse(responseBody);
          serverError = parsed.message || parsed.error || parsed.detail || "";
        } catch {
          serverError = "";
        }
      } else if (responseBody.length < 240) {
        serverError = responseBody;
      }
    }
    const message = serverError || `Request failed (${response.status}).`;
    const error = new Error(`${message} Reference: ${acceptedCorrelationId}`);
    error.status = response.status;
    error.correlationId = acceptedCorrelationId;
    error.retryable = response.status === 408 || response.status === 429 || response.status >= 500;
    throw error;
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
    try {
      const parsed = JSON.parse(existing);
      if (parsed?.installId && parsed?.deviceFingerprintHash) return parsed;
    } catch {
      window.localStorage.removeItem(BROWSER_DEVICE_STORAGE_KEY);
    }
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

export function isCurrentBrowserDevice(deviceInstallId) {
  return Boolean(deviceInstallId) && getBrowserDeviceProfile().installId === deviceInstallId;
}

function authHeaders() {
  // Browser authentication is cookie-only. The HttpOnly session cookie is not
  // exposed to JavaScript and is forwarded by the same-origin API proxies.
  return {};
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

export async function verifyAdminOtp(challengeId, otpCode) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/auth/verify-otp", {
    method: "POST",
    body: JSON.stringify({ challengeId, otpCode })
  });
}

export async function createSignedDownloadLink(accessToken, platform) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/downloads/signed-url", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify({ platform })
  });
}

export async function revokeDeviceSession(accessToken, deviceInstallId, deviceFingerprintHash) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/sessions/revoke-device", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify({ deviceInstallId, deviceFingerprintHash })
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

export async function fetchRegistrationSettings() {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/registration-settings");
}

export async function updateRegistrationSettings(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/registration-settings", {
    method: "POST",
    headers: authHeaders(accessToken),
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

export async function requestUserPasswordReset(email) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/forgot-password", {
    method: "POST",
    body: JSON.stringify({ email })
  });
}

export async function resetUserPassword(token, newPassword) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/auth/reset-password", {
    method: "POST",
    body: JSON.stringify({ token, newPassword })
  });
}

export async function fetchAccountSummary(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/account-summary", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchWalletHistory(accessToken, page = 1, pageSize = 12) {
  return request(DASHBOARD_API_BASE, `/api/dashboard/wallet-history?page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`, {
    headers: authHeaders(accessToken)
  });
}

export async function fetchWalletPurchases(accessToken, page = 1, pageSize = 12) {
  return request(DASHBOARD_API_BASE, `/api/dashboard/wallet-purchases?page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`, {
    headers: authHeaders(accessToken)
  });
}

export async function fetchDevices(accessToken, page = 1, pageSize = 10) {
  return request(DASHBOARD_API_BASE, `/api/dashboard/devices?page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`, {
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

export async function fetchInterviewQuestionBanks(accessToken, page = 1, pageSize = 10) {
  return request(
    DASHBOARD_API_BASE,
    `/api/dashboard/interview-question-banks?page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`,
    { headers: authHeaders(accessToken) }
  );
}

export async function updateInterviewQuestionBank(accessToken, sessionId, payload) {
  return request(
    WINDOWS_BACKEND_API_BASE,
    `/api/desktop/interview-question-banks/${encodeURIComponent(sessionId)}`,
    {
      method: "PUT",
      headers: authHeaders(accessToken),
      body: JSON.stringify(payload)
    }
  );
}

export async function fetchUserSupportTickets(accessToken, page = 1, pageSize = 10) {
  return request(
    WINDOWS_BACKEND_API_BASE,
    `/api/desktop/support/tickets?page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`,
    {
      headers: authHeaders(accessToken)
    }
  );
}

export async function createUserSupportTicket(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/support/tickets", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function submitPublicFeedback(payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/public/feedback", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export async function fetchPublicReviews(limit = 6) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/public/reviews?limit=${encodeURIComponent(limit)}`);
}

export async function fetchAdminOverview(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/overview", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminFeedback(accessToken, { status = "all", page = 1, pageSize = 20 } = {}) {
  const params = new URLSearchParams({ status, page: String(page), pageSize: String(pageSize) });
  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/feedback?${params.toString()}`, {
    headers: authHeaders(accessToken)
  });
}

export async function updateAdminFeedback(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/feedback/update", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function fetchAdminAudit(accessToken, { page = 1, pageSize = 25 } = {}) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/audit?page=${encodeURIComponent(page)}&pageSize=${encodeURIComponent(pageSize)}`, {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminUsers(accessToken, { page = 1, pageSize = 20, query = "" } = {}) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    query
  });

  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/accounts?${params.toString()}`, {
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

export async function fetchAdminUserLedger(accessToken, userId, { page = 1, pageSize = 10 } = {}) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });

  return request(
    WINDOWS_BACKEND_API_BASE,
    `/api/admin/accounts/${encodeURIComponent(userId)}/ledger?${params.toString()}`,
    {
      headers: authHeaders(accessToken)
    }
  );
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

export async function setAdminManualLock(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/accounts/manual-lock", {
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

export async function fetchManagedSpeechAdminInventory(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-speech/credentials", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminPaymentOrders(accessToken, { page = 1, pageSize = 20, query = "", status = "all" } = {}) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    query,
    status
  });

  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/payments/orders?${params.toString()}`, {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminPaymentWebhooks(accessToken, { page = 1, pageSize = 20 } = {}) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });

  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/payments/webhooks?${params.toString()}`, {
    headers: authHeaders(accessToken)
  });
}

export async function fetchAdminSupportTickets(accessToken, { page = 1, pageSize = 20, query = "", status = "all" } = {}) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    query,
    status
  });

  return request(WINDOWS_BACKEND_API_BASE, `/api/admin/support/tickets?${params.toString()}`, {
    headers: authHeaders(accessToken)
  });
}

export async function updateAdminSupportTicket(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/support/tickets/update", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
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

export async function upsertManagedSpeechCredential(accessToken, payload) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-speech/credentials", {
    method: "POST", headers: authHeaders(accessToken), body: JSON.stringify(payload)
  });
}

export async function triggerManagedSpeechCatalogRefresh(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-speech/catalog/refresh", {
    method: "POST", headers: authHeaders(accessToken)
  });
}

export async function updateManagedSpeechRuntimeSelection(accessToken, payload) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-speech/selection", {
    method: "POST", headers: authHeaders(accessToken), body: JSON.stringify(payload)
  });
}

export async function deleteManagedSpeechCredential(accessToken, credentialId) {
  return request(DASHBOARD_API_BASE, `/api/dashboard/admin/managed-speech/credentials/${encodeURIComponent(credentialId)}`, {
    method: "DELETE", headers: authHeaders(accessToken)
  });
}

export async function triggerManagedAiCatalogRefresh(accessToken) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/catalog/refresh", {
    method: "POST",
    headers: authHeaders(accessToken)
  });
}

export async function updateManagedAiRuntimeSelection(accessToken, payload) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/managed-ai/selection", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function updateKnowledgeBaseEmbeddingConfig(accessToken, payload) {
  return request(DASHBOARD_API_BASE, "/api/dashboard/admin/kb/embedding-config", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
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

export async function updateManagedAiModelFlags(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/managed-ai/catalog/model-flags", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function upsertManagedAiCatalogModel(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/managed-ai/catalog/models", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function updateManagedAiModelVision(accessToken, payload) {
  return updateManagedAiModelFlags(accessToken, {
    providerId: payload.providerId,
    modelId: payload.modelId,
    supportsVision: payload.supportsVision
  });
}

export async function sendManagedAiAdminTest(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/managed-ai/test", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function triggerManagedAiLatencyCheck(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/managed-ai/latency/check", {
    method: "POST",
    headers: authHeaders(accessToken)
  });
}

export async function fetchManagedAiLatencyStatus(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/admin/managed-ai/latency/status", {
    headers: authHeaders(accessToken)
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

export async function uploadHostedKnowledgeBaseDocuments(accessToken, files, section) {
  const formData = new FormData();
  formData.append("section", section || "general_reference");
  Array.from(files || []).forEach((file) => {
    formData.append("files", file);
  });

  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/documents", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: formData
  });
}

export async function fetchHostedKnowledgeBaseDocument(accessToken, documentId) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/documents/${encodeURIComponent(documentId)}`, {
    headers: authHeaders(accessToken)
  });
}

export async function pasteHostedKnowledgeBaseDocument(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/paste", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function fetchHostedKnowledgeBaseProfile(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/profile", {
    headers: authHeaders(accessToken)
  });
}

export async function updateHostedKnowledgeBaseProfile(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/profile", {
    method: "PUT",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function createHostedKnowledgeBaseExperience(accessToken, payload) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/experiences", {
    method: "POST",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function updateHostedKnowledgeBaseExperience(accessToken, experienceCardId, payload) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/experiences/${encodeURIComponent(experienceCardId)}`, {
    method: "PUT",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function deleteHostedKnowledgeBaseExperience(accessToken, experienceCardId) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/experiences/${encodeURIComponent(experienceCardId)}`, {
    method: "DELETE",
    headers: authHeaders(accessToken)
  });
}

export async function fetchHostedKnowledgeBaseProjects(accessToken) {
  return request(WINDOWS_BACKEND_API_BASE, "/api/desktop/kb/projects", {
    headers: authHeaders(accessToken)
  });
}

export async function fetchHostedKnowledgeBaseProject(accessToken, projectCardId) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/projects/${encodeURIComponent(projectCardId)}`, {
    headers: authHeaders(accessToken)
  });
}

export async function updateHostedKnowledgeBaseProject(accessToken, projectCardId, payload) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/projects/${encodeURIComponent(projectCardId)}`, {
    method: "PUT",
    headers: authHeaders(accessToken),
    body: JSON.stringify(payload)
  });
}

export async function markHostedKnowledgeBaseProjectRecent(accessToken, projectCardId) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/projects/${encodeURIComponent(projectCardId)}/recent`, {
    method: "POST",
    headers: authHeaders(accessToken)
  });
}

export async function deleteHostedKnowledgeBaseDocument(accessToken, documentId) {
  return request(WINDOWS_BACKEND_API_BASE, `/api/desktop/kb/documents/${encodeURIComponent(documentId)}`, {
    method: "DELETE",
    headers: authHeaders(accessToken)
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
