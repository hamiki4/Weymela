import { useEffect, useState } from "react";
import { post, privateAsset, useAction, useResource } from "../../api/client";
import type { AdminReport, AdminUgcFinance, AdminWallets } from "../../api/types";
import { Badge, Button, DataTable, Dialog, Empty, Field, MoneyInput, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { amount, count, date } from "../../ui/format";

type Deposit = { id: string; businessId: string; business: string; amount: number; status: string; hasReceipt: boolean; submittedAtUtc: string; version: number; destinationNameSnapshot: string | null; destinationAccountSnapshot: string | null };
const br = (value: number) => `${amount(value)} Br`;

export function AdminWalletsPage() {
  const wallets = useResource<AdminWallets>("/admin/wallets");
  const action = useAction();
  const [fund, setFund] = useState<{ id: string; name: string } | null>(null);
  const [fundAmount, setFundAmount] = useState("");
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState("");
  return <div className="admin-page"><PageHeader title="Wallets" compact />{message && <Notice>{message}</Notice>}
    <DepositReviewSection onReviewed={wallets.reload} />
    <Section title="Business wallets"><Resource resource={wallets}>{data => <DataTable rows={data.businesses} rowKey={row => row.businessId} label="Business wallets" columns={[
      { label: "Business", cell: row => <strong>{row.business}</strong> },
      { label: "Total", cell: row => br(row.totalBalance), numeric: true },
      { label: "Available", cell: row => br(row.available), numeric: true },
      { label: "Reserved", cell: row => br(row.reserved), numeric: true },
      { label: "Pending Deposit", cell: row => br(row.pendingDeposit), numeric: true },
      { label: "View Only", cell: row => count(row.viewOnlyCount) },
      { label: "View + Sale", cell: row => count(row.viewSaleCount) },
      { label: "Last Deposit Via", cell: row => row.lastDepositVia ?? "—" },
      { label: "Status", cell: row => <Badge status={row.status} /> },
      { label: "Actions", cell: row => <Button variant="secondary" onClick={() => setFund({ id: row.businessId, name: row.business })}>Promotional Credit</Button> },
    ]} card={row => <div className="admin-mobile-row"><div className="admin-mobile-row-meta"><strong>{row.business}</strong><Badge status={row.status} /></div><dl className="admin-mobile-facts"><div><dt>Total</dt><dd>{br(row.totalBalance)}</dd></div><div><dt>Available</dt><dd>{br(row.available)}</dd></div><div><dt>Reserved</dt><dd>{br(row.reserved)}</dd></div><div><dt>Pending deposit</dt><dd>{br(row.pendingDeposit)}</dd></div><div><dt>View Only</dt><dd>{count(row.viewOnlyCount)}</dd></div><div><dt>View + Sale</dt><dd>{count(row.viewSaleCount)}</dd></div><div><dt>Last Deposit Via</dt><dd>{row.lastDepositVia ?? "—"}</dd></div></dl><div className="actions"><Button variant="secondary" onClick={() => setFund({ id: row.businessId, name: row.business })}>Promotional Credit</Button></div></div>} empty={<Empty title="No Business wallets" message="Business wallets appear after account activation." />} />}</Resource></Section>
    <Section title="Active View / Sale promotions"><Resource resource={wallets}>{data => <DataTable rows={data.promotions} rowKey={row => row.id} label="Active View and Sale promotions" columns={[
      { label: "Business", cell: row => row.business }, { label: "Promotion", cell: row => <strong>{row.title}</strong> }, { label: "Type", cell: row => row.type },
      { label: "Budget", cell: row => br(row.budget), numeric: true }, { label: "Used", cell: row => br(row.used), numeric: true }, { label: "Remaining", cell: row => br(row.remaining), numeric: true },
      { label: "Views / Sales", cell: row => row.type === "View Only" ? `${count(row.verifiedViews)} views` : `${count(row.verifiedViews)} views · ${count(row.verifiedSales)} sales` }, { label: "Status", cell: row => <Badge status={row.status} /> },
    ]} card={row => <div className="admin-mobile-row"><div className="admin-mobile-row-meta"><strong>{row.title}</strong><Badge status={row.status} /></div><small>{row.business} · {row.type}</small><dl className="admin-mobile-facts"><div><dt>Budget</dt><dd>{br(row.budget)}</dd></div><div><dt>Used</dt><dd>{br(row.used)}</dd></div><div><dt>Remaining</dt><dd>{br(row.remaining)}</dd></div><div><dt>Verified views</dt><dd>{count(row.verifiedViews)}</dd></div>{row.type !== "View Only" && <div><dt>Sales</dt><dd>{count(row.verifiedSales)}</dd></div>}</dl></div>} empty={<Empty title="No active promotions" message="Active View and Sale promotions appear here." />} />}</Resource></Section>
    <Dialog title={fund ? `Promotional credit · ${fund.name}` : "Promotional credit"} open={!!fund} onClose={() => { if (!action.busy) setFund(null); }}>
      {fund && <form onSubmit={event => { event.preventDefault(); void action.run(async key => { await post(`/admin/accounts/businesses/${fund.id}/promotional-funding`, { amount: Number(fundAmount), reason: reason.trim() }, key); setMessage(`Promotional funding added for ${fund.name}.`); setFund(null); setFundAmount(""); setReason(""); wallets.reload(); }); }}><Field label="Amount (ETB)"><MoneyInput value={fundAmount} onChange={event => setFundAmount(event.target.value)} /></Field><Field label="Reason"><textarea required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></Field>{action.error && <Notice error>{action.error}</Notice>}<Button type="submit" disabled={action.busy || !fundAmount || !reason.trim()}>{action.busy ? "Adding…" : "Add Promotional Funds"}</Button></form>}
    </Dialog>
  </div>;
}

export function OperationsWalletsPage() {
  return <div className="admin-page"><PageHeader title="Wallets" compact /><DepositReviewSection /></div>;
}

function ReceiptReviewImage({ id }: { id: string }) {
  const [url, setUrl] = useState("");
  const [contentType, setContentType] = useState("");
  const [error, setError] = useState(false);
  useEffect(() => {
    const abort = new AbortController(); let objectUrl = "";
    void privateAsset(`/admin/deposit-requests/${id}/receipt`, abort.signal)
      .then(value => { objectUrl = value.url; if (!abort.signal.aborted) { setUrl(value.url); setContentType(value.contentType); } })
      .catch(() => { if (!abort.signal.aborted) setError(true); });
    return () => { abort.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [id]);
  if (error) return <Notice error>The receipt is unavailable.</Notice>;
  if (!url) return <p>Loading receipt…</p>;
  return contentType === "application/pdf"
    ? <object className="admin-receipt-image" data={url} type="application/pdf" aria-label="Payment receipt for verification"><a href={url} target="_blank" rel="noreferrer">Open receipt PDF</a></object>
    : <img className="admin-receipt-image" src={url} alt="Payment receipt for verification" />;
}

function DepositReviewSection({ onReviewed }: { onReviewed?: () => void }) {
  const deposits = useResource<Deposit[]>("/admin/deposit-requests");
  const action = useAction();
  const [selected, setSelected] = useState<string | null>(null);
  const [reference, setReference] = useState("");
  const [message, setMessage] = useState("");
  const pending = deposits.data?.filter(row => row.status === "Pending") ?? [];
  const rows = deposits.data ?? [];
  const current = rows.find(row => row.id === selected);
  const review = (approve: boolean) => {
    if (!current || !reference.trim()) return;
    void action.run(async key => {
      await post(`/admin/deposit-requests/${current.id}/review`, { approve, expectedVersion: current.version, confirmationReference: reference.trim() }, key);
      setMessage(approve ? "Deposit approved. The wallet balance has been credited." : "Deposit rejected. The wallet balance has not changed.");
      setSelected(null); setReference(""); deposits.reload(); onReviewed?.();
    });
  };
  return <Section title={`Deposit review · ${pending.length} pending`}>
    {message && <Notice>{message}</Notice>}
    <Resource resource={deposits}>{() => rows.length ? <DataTable rows={rows} rowKey={row => row.id} label="Deposits" columns={[
      { label: "Business", cell: row => <strong>{row.business}</strong> }, { label: "Amount", cell: row => br(row.amount), numeric: true },
      { label: "Deposited To", cell: row => row.destinationNameSnapshot ?? "Legacy" }, { label: "Weymela Destination", cell: row => row.destinationAccountSnapshot ?? "—" },
      { label: "Receipt", cell: row => row.hasReceipt ? <Button variant="secondary" onClick={() => setSelected(row.id)}>View Receipt</Button> : "—" },
      { label: "Submitted", cell: row => date(row.submittedAtUtc) }, { label: "Status", cell: row => <Badge status={row.status} /> },
      { label: "Action", cell: row => <Button variant="secondary" onClick={() => setSelected(row.id)}>{row.status === "Pending" ? "Review" : "View Receipt"}</Button> },
    ]} card={row => <div className="admin-mobile-row"><div className="admin-mobile-row-meta"><strong>{row.business}</strong><Badge status={row.status} /></div><small>{row.destinationNameSnapshot ?? "Legacy"} · {row.destinationAccountSnapshot ?? "—"}</small><dl className="admin-mobile-facts"><div><dt>Amount</dt><dd>{br(row.amount)}</dd></div><div><dt>Submitted</dt><dd>{date(row.submittedAtUtc)}</dd></div></dl><Button variant="secondary" onClick={() => setSelected(row.id)}>{row.status === "Pending" ? "Review" : "View Receipt"}</Button></div>} empty={null} /> : <Empty title="No deposits" message="Submitted deposits will appear here." />}</Resource>
    <Dialog title={current ? `Review deposit · ${current.business}` : "Review deposit"} open={!!current} onClose={() => { if (!action.busy) { setSelected(null); setReference(""); } }}>
      {current && <div className="admin-deposit-review"><dl className="admin-mobile-facts"><div><dt>Business</dt><dd>{current.business}</dd></div><div><dt>Amount</dt><dd>{br(current.amount)}</dd></div><div><dt>Deposited To</dt><dd>{current.destinationNameSnapshot ?? "Legacy request"}</dd></div><div><dt>Weymela Destination</dt><dd>{current.destinationAccountSnapshot ?? "—"}</dd></div><div><dt>Submitted</dt><dd>{date(current.submittedAtUtc)}</dd></div><div><dt>Status</dt><dd>Pending</dd></div></dl>
        {current.hasReceipt ? <ReceiptReviewImage id={current.id} /> : <Notice error>No receipt is attached to this earlier request.</Notice>}
        {current.status === "Pending" && <><Field label="Confirmation reference or reason code"><input required maxLength={120} pattern="[A-Za-z0-9][A-Za-z0-9._-]*" value={reference} onChange={event => setReference(event.target.value)} /></Field>
          {action.error && <Notice error>{action.error}</Notice>}
          <div className="actions"><Button disabled={action.busy || !reference.trim()} onClick={() => review(true)}>Approve</Button><Button variant="secondary" disabled={action.busy || !reference.trim()} onClick={() => review(false)}>Reject</Button></div></>}
      </div>}
    </Dialog>
  </Section>;
}

export function AdminUgcPage() {
  const resource = useResource<AdminUgcFinance[]>("/admin/ugc/finance");
  const [search, setSearch] = useState("");
  return <div className="admin-page"><PageHeader title="UGC" compact /><Section title="UGC funding and activity"><div className="admin-ugc-search"><Field label="Search"><input placeholder="Search business or promotion..." value={search} onChange={event => setSearch(event.target.value)} /></Field></div><Resource resource={resource}>{rows => { const visible = rows.filter(row => `${row.business} ${row.title}`.toLowerCase().includes(search.trim().toLowerCase())); return visible.length ? <div className="admin-ugc-list" role="list" aria-label="UGC funding">{visible.map(row => <article key={row.id} role="listitem" className="admin-ugc-row"><div className="admin-ugc-heading"><div><strong>{row.title}</strong><small>{row.business} · {row.type}</small></div><Badge status={row.status} /></div><dl className="admin-mobile-facts"><div><dt>Budget</dt><dd>{br(row.budget)}</dd></div><div><dt>Fixed Creator pay</dt><dd>{br(row.creatorPayment)}</dd></div><div><dt>Used</dt><dd>{br(row.creatorUsed + row.offerUsed)}</dd></div><div><dt>Remaining funded</dt><dd>{br(row.remaining)}</dd></div>{row.customerDiscountPercent !== null && <><div><dt>Customer cashback</dt><dd>{amount(row.customerDiscountPercent)}%</dd></div><div><dt>Cashback funded</dt><dd>{br(row.discountUsed)}</dd></div><div><dt>Qualifying sales</dt><dd>{count(row.qualifyingSales)}</dd></div></>}</dl></article>)}</div> : <Empty title="No matching UGC promotions" message="Business UGC promotions appear here." />; }}</Resource></Section></div>;
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
  return <div className="admin-page"><PageHeader title="Reports" compact /><div className="admin-report-controls"><div className="tabs" role="group" aria-label="Report period">{(["Today", "Week", "Month", "Year", "Custom"] as Range[]).map(value => <button key={value} type="button" aria-pressed={range === value} onClick={() => choose(value)}>{value}</button>)}</div>{range === "Custom" && <form className="actions" onSubmit={event => { event.preventDefault(); if (custom.from <= custom.to) setApplied(custom); }}><Field label="From"><input type="date" required value={custom.from} onChange={event => setCustom({ ...custom, from: event.target.value })} /></Field><Field label="To"><input type="date" required value={custom.to} min={custom.from} onChange={event => setCustom({ ...custom, to: event.target.value })} /></Field><Button type="submit">Apply</Button></form>}</div>
    <Resource resource={resource}>{report => <><p className="admin-period-label">Activity {report.fromUtc.slice(0, 10)} – {new Date(new Date(report.toExclusiveUtc).getTime() - 86400000).toISOString().slice(0, 10)} · UTC</p>
      <Section title="Platform summary"><div className="admin-report-grid">{[
        ["Business deposits", report.activity.businessDeposits], ["Weymela promotional funding", report.activity.promotionalFunding],
        ["Promotion spend", report.activity.promotionSpend], ["UGC spend", report.activity.ugcCreatorSpend + report.activity.ugcOfferSpend], ["Fixed UGC Creator payments", report.activity.ugcCreatorPayments],
        ["Creator earnings", report.activity.creatorEarnings], ["Customer cashback", report.activity.customerCashback],
        ["Customer discounts", report.activity.customerDiscounts], ["Platform revenue", report.activity.platformRevenue],
        ["Creator payouts", report.activity.creatorPayouts], ["Customer payouts", report.activity.customerPayouts],
      ].map(([label, value]) => <div key={label}><span>{label}</span><strong>{br(value as number)}</strong></div>)}</div></Section>
      <Section title="Promotion summary"><DataTable rows={report.promotions} rowKey={row => row.type} label="Promotion summary" columns={[{ label: "Type", cell: row => <strong>{row.type}</strong> }, { label: "Allocated during period", cell: row => br(row.allocated), numeric: true }, { label: "Used during period", cell: row => br(row.used), numeric: true }, { label: "Current remaining", cell: row => br(row.currentRemaining), numeric: true }, { label: "Verified views", cell: row => count(row.verifiedViews) }, { label: "Verified sales", cell: row => count(row.verifiedSales) }]} card={row => <div className="admin-mobile-row"><strong>{row.type}</strong><dl className="admin-mobile-facts"><div><dt>Allocated</dt><dd>{br(row.allocated)}</dd></div><div><dt>Used</dt><dd>{br(row.used)}</dd></div><div><dt>Current remaining</dt><dd>{br(row.currentRemaining)}</dd></div><div><dt>Views</dt><dd>{count(row.verifiedViews)}</dd></div><div><dt>Sales</dt><dd>{count(row.verifiedSales)}</dd></div></dl></div>} empty={<Empty title="No promotion activity" message="No promotion activity in this period." />} /></Section>
      <Section title="UGC summary"><DataTable rows={report.ugc} rowKey={row => row.type} label="UGC summary" columns={[{ label: "Type", cell: row => <strong>{row.type}</strong> }, { label: "Allocated during period", cell: row => br(row.allocated), numeric: true }, { label: "Used during period", cell: row => br(row.used), numeric: true }, { label: "Current remaining", cell: row => br(row.currentRemaining), numeric: true }, { label: "Creator payments", cell: row => br(row.creatorPayments), numeric: true }, { label: "Customer benefit", cell: row => row.type === "UGC Only" ? "—" : br(row.customerDiscounts), numeric: true }, { label: "Qualifying sales", cell: row => row.type === "UGC Only" ? "—" : count(row.verifiedSales) }]} card={row => <div className="admin-mobile-row"><strong>{row.type}</strong><dl className="admin-mobile-facts"><div><dt>Allocated</dt><dd>{br(row.allocated)}</dd></div><div><dt>Used</dt><dd>{br(row.used)}</dd></div><div><dt>Current remaining</dt><dd>{br(row.currentRemaining)}</dd></div><div><dt>Creator payments</dt><dd>{br(row.creatorPayments)}</dd></div>{row.type !== "UGC Only" && <><div><dt>Customer benefit</dt><dd>{br(row.customerDiscounts)}</dd></div><div><dt>Qualifying sales</dt><dd>{count(row.verifiedSales)}</dd></div></>}</dl></div>} empty={<Empty title="No UGC activity" message="No UGC activity in this period." />} /><p className="admin-period-label">UGC spend is funded consumption across Creator deliveries and Customer offers. Creator payments and Customer benefits are shown separately.</p></Section>
      <Section title="Purchase transactions"><DataTable rows={report.purchases ?? []} rowKey={row => row.id} label="Purchase transactions" columns={[
        { label: "Date", cell: row => date(row.occurredAtUtc) },
        { label: "Business", cell: row => row.business },
        { label: "Source", cell: row => `${row.source} · ${row.sourceType === "UGC_PLUS_SALE" ? "UGC + Sale" : "View + Sale"}` },
        { label: "Purchase", cell: row => br(row.purchaseAmount), numeric: true },
        { label: "Business charge", cell: row => br(row.businessCharge), numeric: true },
        { label: "Customer benefit", cell: row => br(row.customerBenefit), numeric: true },
        { label: "Creator sale earning", cell: row => br(row.creatorSaleEarning), numeric: true },
        { label: "Platform share", cell: row => br(row.platformShare), numeric: true },
      ]} card={row => <div className="admin-mobile-row"><div className="admin-mobile-row-meta"><strong>{row.source}</strong><Badge status={row.status} /></div><small>{row.business} · {row.sourceType === "UGC_PLUS_SALE" ? "UGC + Sale" : "View + Sale"} · {date(row.occurredAtUtc)}</small><dl className="admin-mobile-facts"><div><dt>Purchase</dt><dd>{br(row.purchaseAmount)}</dd></div><div><dt>Business charge</dt><dd>{br(row.businessCharge)}</dd></div><div><dt>Customer benefit</dt><dd>{br(row.customerBenefit)}</dd></div><div><dt>Creator sale earning</dt><dd>{br(row.creatorSaleEarning)}</dd></div><div><dt>Platform share</dt><dd>{br(row.platformShare)}</dd></div></dl></div>} empty={<Empty title="No purchases in this period" message="Recorded View + Sale and UGC + Sale purchases appear here." />} /></Section>
      <Section title="Accounts"><div className="admin-report-grid">{[["Customers", report.accounts.customers], ["Creators", report.accounts.creators], ["Businesses", report.accounts.businesses], ["New active accounts", report.accounts.newAccountsInPeriod]].map(([label, value]) => <div key={label}><span>{label}</span><strong>{count(value as number)}</strong></div>)}</div><small>Counts include currently active accounts. New accounts are active identities first recorded during the selected period.</small></Section>
      <Section title="Current balances"><div className="admin-report-grid"><div><span>Business wallet balance</span><strong>{br(report.currentBusinessWalletBalance)}</strong></div><div><span>Unsettled Platform revenue</span><strong>{br(report.currentPlatformUnsettled)}</strong></div></div><small>Current balances are shown separately from activity during the selected period.</small></Section>
    </>}</Resource>
  </div>;
}
