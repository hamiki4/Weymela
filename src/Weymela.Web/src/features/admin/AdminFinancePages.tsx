import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type { AdminReport, AdminUgcFinance, AdminWallets } from "../../api/types";
import { Badge, Button, DataTable, Dialog, Empty, Field, MoneyInput, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { amount, count, date } from "../../ui/format";

type Deposit = { id: string; businessId: string; amount: number; status: string; provider: string; externalReference: string; proofReference: string | null; submittedAtUtc: string; version: number };
const br = (value: number) => `${amount(value)} Br`;

export function AdminWalletsPage() {
  const wallets = useResource<AdminWallets>("/admin/wallets");
  const deposits = useResource<Deposit[]>("/admin/deposit-requests");
  const action = useAction();
  const [fund, setFund] = useState<{ id: string; name: string } | null>(null);
  const [reviewBusiness, setReviewBusiness] = useState<{ id: string; name: string } | null>(null);
  const [fundAmount, setFundAmount] = useState("");
  const [reason, setReason] = useState("");
  const [references, setReferences] = useState<Record<string, string>>({});
  const [message, setMessage] = useState("");
  const pending = deposits.data?.filter(row => row.status === "Pending" && row.businessId === reviewBusiness?.id) ?? [];
  return <div className="admin-page"><PageHeader title="Wallets" />{message && <Notice>{message}</Notice>}
    <Section title="Business wallets"><Resource resource={wallets}>{data => <DataTable rows={data.businesses} rowKey={row => row.businessId} label="Business wallets" columns={[
      { label: "Business", cell: row => <strong>{row.business}</strong> },
      { label: "Total", cell: row => br(row.totalBalance), numeric: true },
      { label: "Available", cell: row => br(row.available), numeric: true },
      { label: "Reserved", cell: row => br(row.reserved), numeric: true },
      { label: "Pending Deposit", cell: row => br(row.pendingDeposit), numeric: true },
      { label: "View Only", cell: row => count(row.viewOnlyCount) },
      { label: "View + Sale", cell: row => count(row.viewSaleCount) },
      { label: "Status", cell: row => <Badge status={row.status} /> },
      { label: "Actions", cell: row => <div className="actions">{row.pendingDeposit > 0 && <Button variant="secondary" onClick={() => setReviewBusiness({ id: row.businessId, name: row.business })}>Review Deposit</Button>}<Button variant="secondary" onClick={() => setFund({ id: row.businessId, name: row.business })}>Add Funds</Button></div> },
    ]} card={row => <div className="admin-mobile-row"><div className="admin-mobile-row-meta"><strong>{row.business}</strong><Badge status={row.status} /></div><dl className="admin-mobile-facts"><div><dt>Total</dt><dd>{br(row.totalBalance)}</dd></div><div><dt>Available</dt><dd>{br(row.available)}</dd></div><div><dt>Reserved</dt><dd>{br(row.reserved)}</dd></div><div><dt>Pending deposit</dt><dd>{br(row.pendingDeposit)}</dd></div><div><dt>View Only</dt><dd>{count(row.viewOnlyCount)}</dd></div><div><dt>View + Sale</dt><dd>{count(row.viewSaleCount)}</dd></div></dl><div className="actions">{row.pendingDeposit > 0 && <Button variant="secondary" onClick={() => setReviewBusiness({ id: row.businessId, name: row.business })}>Review Deposit</Button>}<Button variant="secondary" onClick={() => setFund({ id: row.businessId, name: row.business })}>Add Funds</Button></div></div>} empty={<Empty title="No Business wallets" message="Business wallets appear after account activation." />} />}</Resource></Section>
    <Section title="Active View / Sale promotions"><Resource resource={wallets}>{data => <DataTable rows={data.promotions} rowKey={row => row.id} label="Active View and Sale promotions" columns={[
      { label: "Business", cell: row => row.business }, { label: "Promotion", cell: row => <strong>{row.title}</strong> }, { label: "Type", cell: row => row.type },
      { label: "Budget", cell: row => br(row.budget), numeric: true }, { label: "Used", cell: row => br(row.used), numeric: true }, { label: "Remaining", cell: row => br(row.remaining), numeric: true },
      { label: "Views / Sales", cell: row => row.type === "View Only" ? `${count(row.verifiedViews)} views` : `${count(row.verifiedViews)} views · ${count(row.verifiedSales)} sales` }, { label: "Status", cell: row => <Badge status={row.status} /> },
    ]} card={row => <div className="admin-mobile-row"><div className="admin-mobile-row-meta"><strong>{row.title}</strong><Badge status={row.status} /></div><small>{row.business} · {row.type}</small><dl className="admin-mobile-facts"><div><dt>Budget</dt><dd>{br(row.budget)}</dd></div><div><dt>Used</dt><dd>{br(row.used)}</dd></div><div><dt>Remaining</dt><dd>{br(row.remaining)}</dd></div><div><dt>Verified views</dt><dd>{count(row.verifiedViews)}</dd></div>{row.type !== "View Only" && <div><dt>Sales</dt><dd>{count(row.verifiedSales)}</dd></div>}</dl></div>} empty={<Empty title="No active promotions" message="Active View and Sale promotions appear here." />} />}</Resource></Section>
    <Dialog title={fund ? `Add funds · ${fund.name}` : "Add funds"} open={!!fund} onClose={() => { if (!action.busy) setFund(null); }}>
      {fund && <form onSubmit={event => { event.preventDefault(); void action.run(async key => { await post(`/admin/accounts/businesses/${fund.id}/promotional-funding`, { amount: Number(fundAmount), reason: reason.trim() }, key); setMessage(`Promotional funding added for ${fund.name}.`); setFund(null); setFundAmount(""); setReason(""); wallets.reload(); }); }}><Field label="Amount (ETB)"><MoneyInput value={fundAmount} onChange={event => setFundAmount(event.target.value)} /></Field><Field label="Reason"><textarea required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></Field>{action.error && <Notice error>{action.error}</Notice>}<Button type="submit" disabled={action.busy || !fundAmount || !reason.trim()}>{action.busy ? "Adding…" : "Add Promotional Funds"}</Button></form>}
    </Dialog>
    <Dialog title={reviewBusiness ? `Review deposits · ${reviewBusiness.name}` : "Review deposits"} open={!!reviewBusiness} onClose={() => { if (!action.busy) setReviewBusiness(null); }}>
      <Resource resource={deposits}>{() => pending.length ? <div className="admin-deposit-list">{pending.map(row => <form key={row.id} onSubmit={event => event.preventDefault()}><strong>{br(row.amount)}</strong><small>{date(row.submittedAtUtc)} · {row.provider}</small><small>External reference: {row.externalReference}</small>{row.proofReference && <small>Proof: {row.proofReference}</small>}<Field label="Confirmation reference"><input required value={references[row.id] ?? ""} onChange={event => setReferences({ ...references, [row.id]: event.target.value })} /></Field><div className="actions"><Button disabled={action.busy || !(references[row.id] ?? "").trim()} onClick={() => void action.run(async key => { await post(`/admin/deposit-requests/${row.id}/review`, { approve: true, expectedVersion: row.version, confirmationReference: references[row.id].trim() }, key); setMessage("Deposit approved and credited through the existing review workflow."); setReferences(previous => { const next = { ...previous }; delete next[row.id]; return next; }); deposits.reload(); wallets.reload(); })}>Approve</Button><Button variant="secondary" disabled={action.busy || !(references[row.id] ?? "").trim()} onClick={() => void action.run(async key => { await post(`/admin/deposit-requests/${row.id}/review`, { approve: false, expectedVersion: row.version, confirmationReference: references[row.id].trim() }, key); setMessage("Deposit rejected."); setReferences(previous => { const next = { ...previous }; delete next[row.id]; return next; }); deposits.reload(); wallets.reload(); })}>Reject</Button></div></form>)}</div> : <Empty title="No pending deposits" message="There are no deposits awaiting review for this Business." />}</Resource>
      {action.error && <Notice error>{action.error}</Notice>}
    </Dialog>
  </div>;
}

export function AdminUgcPage() {
  const resource = useResource<AdminUgcFinance[]>("/admin/ugc/finance");
  return <div className="admin-page"><PageHeader title="UGC" /><Section title="UGC funding and activity"><Resource resource={resource}>{rows => rows.length ? <div className="admin-ugc-list" role="list" aria-label="UGC funding">{rows.map(row => <article key={row.id} role="listitem" className="admin-ugc-row"><div className="admin-ugc-heading"><div><strong>{row.title}</strong><small>{row.business} · {row.type}</small></div><Badge status={row.status} /></div><dl className="admin-mobile-facts"><div><dt>Budget</dt><dd>{br(row.budget)}</dd></div><div><dt>Fixed Creator pay</dt><dd>{br(row.creatorPayment)}</dd></div><div><dt>Used</dt><dd>{br(row.creatorUsed + row.offerUsed)}</dd></div><div><dt>Remaining funded</dt><dd>{br(row.remaining)}</dd></div>{row.customerDiscountPercent !== null && <><div><dt>Customer discount</dt><dd>{amount(row.customerDiscountPercent)}%</dd></div><div><dt>Discount consumed</dt><dd>{br(row.discountUsed)}</dd></div><div><dt>Qualifying sales</dt><dd>{count(row.qualifyingSales)}</dd></div></>}</dl></article>)}</div> : <Empty title="No UGC promotions" message="Business UGC promotions appear here." />}</Resource></Section></div>;
}

type Range = "Today" | "Week" | "Month" | "Year" | "Custom";
function iso(dateValue: Date) { return `${dateValue.getUTCFullYear()}-${String(dateValue.getUTCMonth() + 1).padStart(2, "0")}-${String(dateValue.getUTCDate()).padStart(2, "0")}`; }
function dates(range: Range, today: Date) {
  const end = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate()));
  const start = new Date(end);
  if (range === "Week") start.setUTCDate(start.getUTCDate() - ((start.getUTCDay() + 6) % 7));
  if (range === "Month") start.setUTCDate(1);
  if (range === "Year") { start.setUTCMonth(0); start.setUTCDate(1); }
  return { from: iso(start), to: iso(end) };
}
export function AdminReportsPage() {
  const [range, setRange] = useState<Range>("Month");
  const [custom, setCustom] = useState(() => dates("Month", new Date()));
  const [applied, setApplied] = useState(() => dates("Month", new Date()));
  const resource = useResource<AdminReport>(`/admin/reports?from=${applied.from}&to=${applied.to}`);
  const choose = (value: Range) => { setRange(value); if (value !== "Custom") setApplied(dates(value, new Date())); };
  return <div className="admin-page"><PageHeader title="Reports" /><div className="admin-report-controls"><div className="tabs" role="group" aria-label="Report period">{(["Today", "Week", "Month", "Year", "Custom"] as Range[]).map(value => <button key={value} type="button" aria-pressed={range === value} onClick={() => choose(value)}>{value}</button>)}</div>{range === "Custom" && <form className="actions" onSubmit={event => { event.preventDefault(); if (custom.from <= custom.to) setApplied(custom); }}><Field label="From"><input type="date" required value={custom.from} onChange={event => setCustom({ ...custom, from: event.target.value })} /></Field><Field label="To"><input type="date" required value={custom.to} min={custom.from} onChange={event => setCustom({ ...custom, to: event.target.value })} /></Field><Button type="submit">Apply</Button></form>}</div>
    <Resource resource={resource}>{report => <><p className="admin-period-label">Activity {report.fromUtc.slice(0, 10)} – {new Date(new Date(report.toExclusiveUtc).getTime() - 86400000).toISOString().slice(0, 10)} · UTC</p>
      <Section title="Platform summary"><div className="admin-report-grid">{[
        ["Business deposits", report.activity.businessDeposits], ["Weymela promotional funding", report.activity.promotionalFunding],
        ["Promotion spend", report.activity.promotionSpend], ["UGC spend", report.activity.ugcCreatorSpend + report.activity.ugcOfferSpend], ["Fixed UGC Creator payments", report.activity.ugcCreatorPayments],
        ["Creator earnings", report.activity.creatorEarnings], ["Customer cashback", report.activity.customerCashback],
        ["Customer discounts", report.activity.customerDiscounts], ["Platform revenue", report.activity.platformRevenue],
        ["Creator payouts", report.activity.creatorPayouts], ["Customer payouts", report.activity.customerPayouts],
      ].map(([label, value]) => <div key={label}><span>{label}</span><strong>{br(value as number)}</strong></div>)}</div></Section>
      <Section title="Promotion summary"><DataTable rows={report.promotions} rowKey={row => row.type} label="Promotion summary" columns={[{ label: "Type", cell: row => <strong>{row.type}</strong> }, { label: "Allocated during period", cell: row => br(row.allocated), numeric: true }, { label: "Used during period", cell: row => br(row.used), numeric: true }, { label: "Current remaining", cell: row => br(row.currentRemaining), numeric: true }, { label: "Verified views", cell: row => count(row.verifiedViews) }, { label: "Verified sales", cell: row => count(row.verifiedSales) }]} card={row => <div className="admin-mobile-row"><strong>{row.type}</strong><dl className="admin-mobile-facts"><div><dt>Allocated</dt><dd>{br(row.allocated)}</dd></div><div><dt>Used</dt><dd>{br(row.used)}</dd></div><div><dt>Current remaining</dt><dd>{br(row.currentRemaining)}</dd></div><div><dt>Views</dt><dd>{count(row.verifiedViews)}</dd></div><div><dt>Sales</dt><dd>{count(row.verifiedSales)}</dd></div></dl></div>} empty={<Empty title="No promotion activity" message="No promotion activity in this period." />} /></Section>
      <Section title="UGC summary"><DataTable rows={report.ugc} rowKey={row => row.type} label="UGC summary" columns={[{ label: "Type", cell: row => <strong>{row.type}</strong> }, { label: "Allocated during period", cell: row => br(row.allocated), numeric: true }, { label: "Used during period", cell: row => br(row.used), numeric: true }, { label: "Current remaining", cell: row => br(row.currentRemaining), numeric: true }, { label: "Creator payments", cell: row => br(row.creatorPayments), numeric: true }, { label: "Customer discounts", cell: row => row.type === "UGC Only" ? "—" : br(row.customerDiscounts), numeric: true }, { label: "Qualifying sales", cell: row => row.type === "UGC Only" ? "—" : count(row.verifiedSales) }]} card={row => <div className="admin-mobile-row"><strong>{row.type}</strong><dl className="admin-mobile-facts"><div><dt>Allocated</dt><dd>{br(row.allocated)}</dd></div><div><dt>Used</dt><dd>{br(row.used)}</dd></div><div><dt>Current remaining</dt><dd>{br(row.currentRemaining)}</dd></div><div><dt>Creator payments</dt><dd>{br(row.creatorPayments)}</dd></div>{row.type !== "UGC Only" && <><div><dt>Customer discounts</dt><dd>{br(row.customerDiscounts)}</dd></div><div><dt>Qualifying sales</dt><dd>{count(row.verifiedSales)}</dd></div></>}</dl></div>} empty={<Empty title="No UGC activity" message="No UGC activity in this period." />} /><p className="admin-period-label">UGC spend is funded consumption across Creator deliveries and Customer offers. Creator payments and Customer discounts are components shown separately.</p></Section>
      <Section title="Accounts"><div className="admin-report-grid">{[["Customers", report.accounts.customers], ["Creators", report.accounts.creators], ["Businesses", report.accounts.businesses], ["New active accounts", report.accounts.newAccountsInPeriod]].map(([label, value]) => <div key={label}><span>{label}</span><strong>{count(value as number)}</strong></div>)}</div><small>Counts include currently active accounts. New accounts are active identities first recorded during the selected period.</small></Section>
      <Section title="Current balances"><div className="admin-report-grid"><div><span>Business wallet balance</span><strong>{br(report.currentBusinessWalletBalance)}</strong></div><div><span>Unsettled Platform revenue</span><strong>{br(report.currentPlatformUnsettled)}</strong></div></div><small>Current balances are shown separately from activity during the selected period.</small></Section>
    </>}</Resource>
  </div>;
}
