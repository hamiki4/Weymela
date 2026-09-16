export const amount = (value: number) =>
  new Intl.NumberFormat("en-ET", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(value);
export type MoneyDisplayContext = "compact" | "explicit";
export const money = (
  value: number,
  context: MoneyDisplayContext = "compact",
  currency = "ETB",
) =>
  context === "compact"
    ? amount(value)
    : new Intl.NumberFormat("en-ET", {
        style: "currency",
        currency,
        currencyDisplay: "code",
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
      }).format(value);
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
export const campaignType = (type: string) =>
  type === "ViewOnly" ? "View Only" : "View + Commission";
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
    ViewPlusCommission: "View + Commission",
  })[status] ?? status.replace(/([a-z])([A-Z])/g, "$1 $2");
export const safeExternal = (value: string | null | undefined) => {
  try {
    const url = new URL(value ?? "");
    return url.protocol === "https:" ? url.href : undefined;
  } catch {
    return undefined;
  }
};
