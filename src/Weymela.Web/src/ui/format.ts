export const amount = (value: number) =>
  typeof value !== "number" || !Number.isFinite(value) ? "Unavailable" : new Intl.NumberFormat("en-ET", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(value);
export type MoneyDisplayContext = "compact" | "explicit";
export const money = (
  value: number,
  _context: MoneyDisplayContext = "compact",
  _currency?: string,
) => amount(value);
export const count = (value: number) =>
  new Intl.NumberFormat("en-ET").format(value);
export const date = (value: string | null) =>
  value
    ? new Intl.DateTimeFormat("en", {
        day: "numeric",
        month: "short",
        year: "numeric",
      }).format(new Date(value))
    : "—";
export const dateTime = (value: string) =>
  new Intl.DateTimeFormat("en", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
export const daysLeft = (end: string) =>
  Math.max(0, Math.ceil((new Date(end).getTime() - Date.now()) / 86400000));
export const promotionTypeCode = (type: string) =>
  type === "ViewOnly" || type === "View Only"
    ? "ViewOnly"
    : type === "ViewPlusCommission" || type === "View & Sale"
      ? "ViewPlusCommission"
      : null;
export const isViewOnly = (type: string) =>
  promotionTypeCode(type) === "ViewOnly";
export const isViewAndSale = (type: string) =>
  promotionTypeCode(type) === "ViewPlusCommission";
export const campaignType = (type: string) => {
  const code = promotionTypeCode(type);
  return code === "ViewOnly"
    ? "View Only"
    : code === "ViewPlusCommission"
      ? "View & Sale"
      : type;
};
export const statusLabel = (status: string) =>
  ({
    AwaitingContent: "Ready for content",
    FundingRequired: "Budget needed",
    BudgetExhausted: "Budget used",
    Eligible: "Eligible for payout",
    Ready: "Ready to pay",
    Pending: "Request sent",
    Approved: "Approved",
    Rejected: "Not approved",
    ViewOnly: "View Only",
    ViewPlusCommission: "View & Sale",
  })[status] ?? status.replace(/([a-z])([A-Z])/g, "$1 $2");
export const safeExternal = (value: string | null | undefined) => {
  try {
    const url = new URL(value ?? "");
    return url.protocol === "https:" ? url.href : undefined;
  } catch {
    return undefined;
  }
};
