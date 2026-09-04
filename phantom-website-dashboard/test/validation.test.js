import test from "node:test";
import assert from "node:assert/strict";
import {
  maskIdentifier,
  parseRequiredInteger,
  parseRequiredNonNegativeNumber
} from "../src/lib/validation.js";

test("credit validation accepts bounded numbers without coercing invalid input to zero", () => {
  assert.equal(parseRequiredNonNegativeNumber("12.5", "Credits", 100), 12.5);
  assert.throws(() => parseRequiredNonNegativeNumber("not-a-number", "Credits", 100), /Credits must be a number/);
  assert.throws(() => parseRequiredNonNegativeNumber("-1", "Credits", 100), /Credits must be a number/);
  assert.throws(() => parseRequiredNonNegativeNumber("101", "Credits", 100), /Credits must be a number/);
});

test("integer validation rejects fractions and out-of-range values", () => {
  assert.equal(parseRequiredInteger("32", "Batch size", 1, 256), 32);
  assert.throws(() => parseRequiredInteger("1.5", "Batch size", 1, 256), /whole number/);
  assert.throws(() => parseRequiredInteger("0", "Batch size", 1, 256), /whole number/);
});

test("sensitive identifiers are shortened for dashboard display", () => {
  assert.equal(maskIdentifier("browser-install-12345678"), "••••12345678");
  assert.equal(maskIdentifier("short"), "short");
  assert.equal(maskIdentifier(""), "Unavailable");
});
