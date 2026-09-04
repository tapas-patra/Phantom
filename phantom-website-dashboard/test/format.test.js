import test from "node:test";
import assert from "node:assert/strict";
import {
  countBy,
  describePaymentOpsState,
  formatDate,
  getPaymentOpsState,
  parseUtcMillis,
  prettyJson,
  trimAdminTesterHistory
} from "../src/lib/format.js";

test("date helpers fail closed for invalid values", () => {
  assert.equal(parseUtcMillis("not-a-date"), 0);
  assert.equal(formatDate("not-a-date"), "n/a");
  assert.equal(formatDate(null), "n/a");
});

test("payment operations distinguish credited, waiting, and stuck orders", () => {
  const now = Date.parse("2026-09-04T12:05:00Z");
  assert.equal(getPaymentOpsState({ status: "credited" }, now), "credited");
  assert.equal(getPaymentOpsState({ status: "client_confirmed", createdAtUtc: "2026-09-04T12:04:30Z" }, now), "waiting_webhook");
  const stuck = { status: "client_confirmed", createdAtUtc: "2026-09-04T12:00:00Z" };
  assert.equal(getPaymentOpsState(stuck, now), "stuck_waiting_webhook");
  assert.match(describePaymentOpsState(stuck, now), /still not credited/i);
});

test("collection and safe display helpers preserve predictable output", () => {
  assert.deepEqual(countBy([{ state: "open" }, { state: "open" }, { state: "closed" }], (item) => item.state), { open: 2, closed: 1 });
  assert.equal(trimAdminTesterHistory([1, 2, 3, 4, 5, 6, 7, 8, 9]).length, 8);
  assert.equal(prettyJson('{"ok":true}'), '{\n  "ok": true\n}');
  assert.equal(prettyJson("plain text"), "plain text");
});
