export function parseRequiredNonNegativeNumber(value, label, maximum = 100000) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0 || parsed > maximum) {
    throw new Error(`${label} must be a number between 0 and ${maximum.toLocaleString()}.`);
  }
  return parsed;
}

export function parseRequiredInteger(value, label, minimum, maximum) {
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum || parsed > maximum) {
    throw new Error(`${label} must be a whole number between ${minimum} and ${maximum.toLocaleString()}.`);
  }
  return parsed;
}

export function maskIdentifier(value) {
  if (!value) return "Unavailable";
  return value.length <= 8 ? value : `••••${value.slice(-8)}`;
}
