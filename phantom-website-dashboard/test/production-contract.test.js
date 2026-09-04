import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

test("frontend preserves the production browser security contract", async () => {
  const [api, app, html, vercel] = await Promise.all([
    readFile(new URL("../src/lib/api.js", import.meta.url), "utf8"),
    readFile(new URL("../src/App.jsx", import.meta.url), "utf8"),
    readFile(new URL("../index.html", import.meta.url), "utf8"),
    readFile(new URL("../vercel.json", import.meta.url), "utf8")
  ]);

  assert.match(api, /credentials:\s*"include"/);
  assert.match(api, /"X-Phantom-CSRF":\s*"1"/);
  assert.doesNotMatch(app, /github\.com\/.+releases\/download/);
  assert.doesNotMatch(api, /VITE_PHANTOM_DASHBOARD_ADMIN_API_KEY/);
  assert.match(html, /Content-Security-Policy/);
  assert.match(html, /frame-src[^;]+checkout\.razorpay\.com/);
  assert.match(vercel, /Strict-Transport-Security/);
  assert.match(vercel, /X-Frame-Options/);
});

test("public metadata and authenticated no-index controls are present", async () => {
  const [app, html, robots, sitemap] = await Promise.all([
    readFile(new URL("../src/App.jsx", import.meta.url), "utf8"),
    readFile(new URL("../index.html", import.meta.url), "utf8"),
    readFile(new URL("../public/robots.txt", import.meta.url), "utf8"),
    readFile(new URL("../public/sitemap.xml", import.meta.url), "utf8")
  ]);
  assert.match(app, /noindex/);
  assert.match(html, /og:image/);
  assert.match(robots, /Sitemap:/);
  assert.match(sitemap, /<loc>https:\/\/phantom-interview\.vercel\.app\//);
});
