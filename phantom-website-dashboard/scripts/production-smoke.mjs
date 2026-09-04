import assert from "node:assert/strict";

const website = requiredUrl("PHANTOM_SMOKE_WEBSITE_URL");
const windowsBackend = requiredUrl("PHANTOM_SMOKE_WINDOWS_BACKEND_URL");
const dashboardBackend = requiredUrl("PHANTOM_SMOKE_DASHBOARD_BACKEND_URL");

const websiteResponse = await checkedFetch(`${website}/`);
assert.equal(websiteResponse.status, 200, "Website root must return 200");
assert.match(await websiteResponse.text(), /<div id="root"><\/div>/, "Website root must contain the React mount point");
for (const header of ["strict-transport-security", "content-security-policy", "x-content-type-options", "x-frame-options", "referrer-policy"]) {
  assert.ok(websiteResponse.headers.get(header), `Website must send ${header}`);
}

for (const route of ["pricing", "download", "login", "register", "privacy", "terms"]) {
  const response = await checkedFetch(`${website}/${route}`);
  assert.equal(response.status, 200, `/${route} must return the SPA shell`);
}

for (const [label, baseUrl] of [["authority", windowsBackend], ["dashboard", dashboardBackend]]) {
  const response = await checkedFetch(`${baseUrl}/health/ready`);
  assert.equal(response.status, 200, `${label} readiness check must return 200`);
  const payload = await response.json();
  assert.equal(payload.status, "ready", `${label} readiness payload must report ready`);
}

await expectUnauthorized(`${windowsBackend}/api/desktop/auth/me`);
await expectUnauthorized(`${windowsBackend}/api/admin/auth/me`);
await expectUnauthorized(`${windowsBackend}/api/admin/audit`);
await expectUnauthorized(`${dashboardBackend}/api/dashboard/account-summary`);

console.log("Production smoke checks passed: website routes/headers, backend readiness, and protected-route boundaries.");

async function expectUnauthorized(url) {
  const response = await checkedFetch(url);
  assert.ok([401, 403].includes(response.status), `${url} must reject an unauthenticated request`);
}

async function checkedFetch(url) {
  try {
    return await fetch(url, { redirect: "manual", signal: AbortSignal.timeout(15_000) });
  } catch (error) {
    throw new Error(`Could not reach ${url}: ${error.message}`, { cause: error });
  }
}

function requiredUrl(name) {
  const value = process.env[name]?.replace(/\/$/, "");
  if (!value) throw new Error(`${name} is required.`);
  const url = new URL(value);
  if (url.protocol !== "https:") throw new Error(`${name} must use HTTPS.`);
  return url.toString().replace(/\/$/, "");
}
