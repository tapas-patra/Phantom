export function parseUtcMillis(value) {
  if (!value) return 0;
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

export function formatDate(value) {
  if (!value) return "n/a";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "n/a";
  return new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(parsed);
}

export function toDateTimeLocal(value) {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

export function formatInr(value) {
  return new Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", maximumFractionDigits: 0 }).format(value || 0);
}

export function prettyJson(value) {
  if (!value) return "n/a";
  try {
    return JSON.stringify(typeof value === "string" ? JSON.parse(value) : value, null, 2);
  } catch {
    return String(value);
  }
}

export function countBy(items, keyFn) {
  return items.reduce((counts, item) => {
    const key = keyFn(item);
    counts[key] = (counts[key] || 0) + 1;
    return counts;
  }, {});
}

export function trimAdminTesterHistory(history) {
  return history.slice(-8);
}

export function formatManagedAiLatencyStatus(status) {
  return ({ ok: "Ready", queued: "Queued", running: "Running", completed: "Completed", timeout: "Timeout (>20s)", not_chat_capable: "Not chat-capable", failed: "Failed", untested: "Untested" })[status] || status || "Unknown";
}

export function getPaymentOpsState(order, now = Date.now()) {
  if (order.creditedAtUtc || order.status === "credited") return "credited";
  if (order.clientConfirmed || order.status === "client_confirmed") {
    const createdAt = order.createdAtUtc ? new Date(order.createdAtUtc).getTime() : 0;
    return createdAt && (now - createdAt) / 60000 >= 2 ? "stuck_waiting_webhook" : "waiting_webhook";
  }
  return "created";
}

export function describePaymentOpsState(order, now) {
  switch (getPaymentOpsState(order, now)) {
    case "credited": return "Wallet mutation applied after trusted backend confirmation.";
    case "waiting_webhook": return "Checkout succeeded in the browser and the backend is waiting for the webhook to credit the wallet.";
    case "stuck_waiting_webhook": return "Client confirmed but still not credited after 2+ minutes. Check delivery, webhook URL, secret, and backend logs.";
    default: return "Order created, but browser confirmation has not been recorded yet.";
  }
}
