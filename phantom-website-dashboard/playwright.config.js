import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? "github" : "line",
  use: {
    baseURL: "http://127.0.0.1:4174",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure"
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"], browserName: "chromium", channel: process.env.CI ? undefined : "chrome" } },
    { name: "mobile", use: { ...devices["iPhone 13"], browserName: "chromium", channel: process.env.CI ? undefined : "chrome" } }
  ],
  webServer: {
    command: "npm run dev -- --host 127.0.0.1 --port 4174",
    url: "http://127.0.0.1:4174",
    reuseExistingServer: !process.env.CI,
    env: {
      ...process.env,
      VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL: "/api/windows",
      VITE_PHANTOM_DASHBOARD_API_BASE_URL: "/api/dashboard-backend"
    }
  }
});
