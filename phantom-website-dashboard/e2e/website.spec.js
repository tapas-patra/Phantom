import { test, expect } from "@playwright/test";

test("public site is navigable and does not overflow", async ({ page }) => {
  await mockApi(page, { user: null, admin: null });
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1, name: /Think clearly when the room gets loud/i })).toBeVisible();
  await expect(page.getByRole("link", { name: "Start your free trial" })).toBeVisible();
  const dimensions = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  await page.getByRole("link", { name: "Pricing", exact: true }).first().click();
  await expect(page).toHaveURL(/\/pricing$/);
  await expect(page.getByRole("heading", { level: 1 })).toContainText(/Choose who manages the AI/i);
});

test("keyboard users can skip directly to main content", async ({ page }) => {
  await mockApi(page, { user: null, admin: null });
  await page.goto("/");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to main content" })).toBeFocused();
});

test("protected dashboard redirects when no session exists", async ({ page }) => {
  await mockApi(page, { user: null, admin: null });
  await page.goto("/dashboard");
  await expect(page).toHaveURL(/\/login$/);
});

test("user dashboard and focused knowledge editor render from backend contracts", async ({ page }) => {
  await mockApi(page, { user: userSession(), admin: null });
  await page.goto("/dashboard");
  await expect(page.getByText("user@example.com", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "Your Phantom workspace is ready." })).toBeVisible();
  await page.getByRole("link", { name: "Knowledge Base" }).click();
  await expect(page.getByRole("heading", { name: /Turn your experience into context/i })).toBeVisible();
  await expect(page.getByRole("button", { name: "Profile", exact: true })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Documents" }).click();
  await expect(page.getByText("Process Pasted Content")).toBeVisible();
  await expect(page.getByLabel("Full name")).toHaveCount(0);
});

test("admin overview and audit route render without exposing bearer tokens", async ({ page }) => {
  await mockApi(page, { user: null, admin: adminSession() });
  await page.goto("/admin");
  await expect(page.getByRole("heading", { name: "Monitor the systems that keep Phantom available." })).toBeVisible();
  await page.getByRole("link", { name: "Audit" }).click();
  await expect(page.getByRole("heading", { name: /Review security-sensitive changes/i })).toBeVisible();
  await expect(page.getByText("No admin changes have been recorded yet.")).toBeVisible();
  const storedSession = await page.evaluate(() => window.localStorage.getItem("phantom.website.admin-session"));
  expect(storedSession ?? "").not.toContain("accessToken");
});

test("admin password login requires the emailed one-time code", async ({ page }) => {
  await mockApi(page, { user: null, admin: null, adminOtp: true });
  await page.goto("/admin/login");
  await page.getByLabel("Admin email").fill("admin@example.com");
  await page.getByLabel("Password").fill("correct-horse-battery-staple");
  await page.getByRole("button", { name: "Continue with Email Verification" }).click();
  await expect(page.getByText(/A 6-digit verification code was sent/i)).toBeVisible();
  await page.getByLabel("Email verification code").fill("482913");
  await page.getByRole("button", { name: "Verify and Open Dashboard" }).click();
  await expect(page).toHaveURL(/\/admin$/);
  await expect(page.getByRole("heading", { name: "Monitor the systems that keep Phantom available." })).toBeVisible();
});

async function mockApi(page, { user, admin, adminOtp = false }) {
  let currentAdmin = admin;
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const method = route.request().method();
    const json = (value, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(value) });
    if (path.endsWith("/api/desktop/auth/me")) return user ? json(user) : json({ error: "Unauthorized" }, 401);
    if (path.endsWith("/api/admin/auth/me")) return currentAdmin ? json(currentAdmin) : json({ error: "Unauthorized" }, 401);
    if (adminOtp && method === "POST" && path.endsWith("/api/admin/auth/login")) {
      return json({ challengeId: "admin-otp-test", maskedEmail: "a***n@example.com", expiresAtUtc: "2026-09-04T10:10:00Z" });
    }
    if (adminOtp && method === "POST" && path.endsWith("/api/admin/auth/verify-otp")) {
      const request = route.request().postDataJSON();
      if (request.challengeId !== "admin-otp-test" || request.otpCode !== "482913") return json({ error: "Invalid code" }, 400);
      currentAdmin = adminSession();
      return json(currentAdmin);
    }
    if (path.endsWith("/api/dashboard/account-summary")) return json(accountSummary());
    if (path.endsWith("/api/dashboard/download-entitlement")) return json({ canDownload: true, installerLabel: "Phantom Desktop", installerVersion: "1.0.0", releaseChannel: "stable" });
    if (path.endsWith("/api/dashboard/support/preview")) return json({ supportMessage: "No active support event", lastUsageChargeCredits: 0, offlineLeaseHoursRemaining: 24 });
    if (path.endsWith("/api/desktop/kb")) return json(knowledgeBase());
    if (path.endsWith("/api/dashboard/devices")) return json(emptyPage());
    if (path.endsWith("/api/dashboard/wallet-history")) return json(emptyPage());
    if (path.endsWith("/api/dashboard/wallet-purchases")) return json(emptyPage());
    if (path.endsWith("/api/dashboard/admin/overview")) return json({ accountCount: 0, activeSessionCount: 0, ledgerEntryCount: 0, activeLockCount: 0, managedCredentialCount: 0, paymentOrderCount: 0 });
    if (path.endsWith("/api/dashboard/admin/managed-ai/credentials")) return json({ credentials: [], managedProviders: [], catalogs: { providers: [] } });
    if (path.endsWith("/api/admin/managed-ai/latency/status")) return json({ providers: [] });
    if (path.endsWith("/api/admin/integrations/gmail/oauth/status")) return json({ statusLabel: "Configured", statusMessage: "Sender ready", isConfigured: true, hasRefreshToken: true, hasValidRefreshToken: true, fromEmail: "admin@example.com" });
    if (path.endsWith("/api/admin/audit")) return json(emptyPage());
    if (path.endsWith("/api/desktop/auth/registration-settings")) return json({ phoneVerificationRequired: false });
    return json({ error: `No mock for ${path}` }, 404);
  });
}

function userSession() {
  return { userId: "user-1", email: "user@example.com", authMethod: "password", deviceInstallId: "web-test", authenticatedAtUtc: "2026-09-04T10:00:00Z", expiresAtUtc: "2026-09-05T10:00:00Z", isAuthenticated: true };
}

function adminSession() {
  return { adminId: "admin-1", email: "admin@example.com", displayName: "Phantom Admin", role: "super_admin", authMethod: "admin:password+email_otp", authenticatedAtUtc: "2026-09-04T10:00:00Z", expiresAtUtc: "2026-09-05T10:00:00Z", isAuthenticated: true };
}

function accountSummary() {
  return { userId: "user-1", email: "user@example.com", planLabel: "Premium", accessTier: "premium", emailVerified: true, phoneVerified: true, canUseDesktopPowerFeatures: true, proAvailableCredits: 12, premiumAvailableCredits: 20, premiumNegativeCredits: 0, activeDeviceCount: 1, leaseExpiresAtUtc: "2026-09-05T10:00:00Z", lastActivityAtUtc: "2026-09-04T10:00:00Z" };
}

function knowledgeBase() {
  return { knowledgeBaseId: "kb-1", name: "Interview context", description: "Production test context", status: "ready", documentCount: 0, chunkCount: 0, canUseInInterview: true, documents: [], profile: {}, experienceCards: [], projectCards: [] };
}

function emptyPage() {
  return { items: [], page: 1, pageSize: 20, totalCount: 0, hasNextPage: false };
}
