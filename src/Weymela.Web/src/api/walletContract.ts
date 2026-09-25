/** Reject a malformed authoritative wallet response; never fabricate money. */
export function validateWalletContract(payload: unknown): void {
  const wallet = payload && typeof payload === "object" && "wallet" in payload
    ? (payload as { wallet: unknown }).wallet : payload;
  if (!wallet || typeof wallet !== "object")
    throw new Error("Business balance is unavailable. Please try again.");
  const values = wallet as Record<string, unknown>;
  for (const field of ["totalBalance", "available", "reserved"]) {
    const value = values[field];
    if (typeof value !== "number" || !Number.isFinite(value) || value < 0)
      throw new Error("Business balance is unavailable. Please try again or contact support.");
  }
  const total = values.totalBalance as number;
  const available = values.available as number;
  const reserved = values.reserved as number;
  if (Math.abs(total - available - reserved) > 0.011)
    throw new Error("Business balance is inconsistent. Please contact support.");
  if (!Array.isArray(values.history) || values.history.some((item: unknown) =>
    !item || typeof item !== "object" || typeof (item as Record<string, unknown>).amount !== "number"
      || !Number.isFinite((item as { amount: number }).amount)))
    throw new Error("Business wallet history is unavailable. Please try again.");
}
