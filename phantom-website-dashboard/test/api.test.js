import test from "node:test";
import assert from "node:assert/strict";
import { request } from "../src/lib/api.js";

test("safe reads retry one transient backend failure", async (context) => {
  const originalFetch = globalThis.fetch;
  context.after(() => { globalThis.fetch = originalFetch; });
  let calls = 0;
  globalThis.fetch = async () => {
    calls += 1;
    return calls === 1
      ? new Response(JSON.stringify({ error: "Temporarily unavailable" }), { status: 503, headers: { "content-type": "application/json" } })
      : new Response(JSON.stringify({ ok: true }), { status: 200, headers: { "content-type": "application/json" } });
  };

  assert.deepEqual(await request("https://example.test", "/health", { correlationId: "corr-retry" }), { ok: true });
  assert.equal(calls, 2);
});

test("unsafe requests are not retried and include browser verification headers", async (context) => {
  const originalFetch = globalThis.fetch;
  context.after(() => { globalThis.fetch = originalFetch; });
  let calls = 0;
  let requestHeaders;
  globalThis.fetch = async (_url, init) => {
    calls += 1;
    requestHeaders = init.headers;
    return new Response(JSON.stringify({ error: "Conflict" }), { status: 409, headers: { "content-type": "application/json" } });
  };

  await assert.rejects(
    request("https://example.test", "/mutation", { method: "POST", body: "{}", correlationId: "corr-mutation" }),
    /Conflict.*corr-mutation/
  );
  assert.equal(calls, 1);
  assert.equal(requestHeaders["X-Phantom-CSRF"], "1");
  assert.equal(requestHeaders["X-Phantom-Correlation-Id"], "corr-mutation");
});

test("caller cancellation remains cancellation instead of becoming a timeout", async (context) => {
  const originalFetch = globalThis.fetch;
  context.after(() => { globalThis.fetch = originalFetch; });
  globalThis.fetch = async (_url, init) => new Promise((_resolve, reject) => {
    init.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true });
  });
  const controller = new AbortController();
  const pending = request("https://example.test", "/slow", { signal: controller.signal, correlationId: "corr-abort" });
  controller.abort();
  await assert.rejects(pending, (error) => error.name === "AbortError");
});
