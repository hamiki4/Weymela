import { AdminSearch, AdminPager, useAdminSearch } from "./AdminSearch";
import { useState } from "react";
import { notifyAdminActionCountsChanged, post, useAction, useResource } from "../../api/client";
import type {
  OperationsPayoutWorkspace,
  PayoutWorkspace,
  PlatformSummary,
  QueueRow,
} from "../../api/types";
import { useSession } from "../../app/Session";
import {
  Badge,
  Button,
  Currency,
  DataTable,
  Dialog,
  Empty,
  Field,
  FundsGrid,
  MoneyInput,
  Notice,
  PageHeader,
  Resource,
  Section,
  Tabs,
} from "../../ui/components";
import { amount, date, dateTime } from "../../ui/format";

export function PlatformSettlement({
  available,
  accrued,
  settled,
  reload,
}: {
  available: number;
  accrued: number;
  settled: number;
  reload: () => void;
}) {
  const [value, setValue] = useState("");
  const [reference, setReference] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const action = useAction();
  const [success, setSuccess] = useState(false);
  return (
    <>
      <FundsGrid
        values={[
          ["Accrued Revenue", accrued],
          ["Settled Revenue", settled],
          ["Available Platform Revenue", available],
        ]}
      />
      <form
        className="contained-form section-kicker-space"
        onSubmit={(e) => {
          e.preventDefault();
          void action.run(async (key) => {
            await post(
              "/admin/platform/settlements",
              { amount: Number(value), reference },
              key,
            );
            setSuccess(true);
            setValue("");
            setReference("");
            setConfirmed(false);
            reload();
          });
        }}
      >
        <fieldset disabled={action.busy || available <= 0}>
          <Field label="Settlement Amount">
            <MoneyInput
              value={value}
              max={available}
              onChange={(e) => setValue(e.target.value)}
            />
          </Field>
          <Field label="Settlement reference">
            <input
              value={reference}
              onChange={(e) => setReference(e.target.value)}
              maxLength={200}
              required
            />
          </Field>
          <label className="check-line">
            <input
              type="checkbox"
              checked={confirmed}
              onChange={(e) => setConfirmed(e.target.checked)}
              required
            />
            I confirm this settlement has already been completed externally.
          </label>
          <p className="fine-print">
            Records the settlement against existing Platform revenue. No payment
            is sent. Partial settlement leaves the remainder available.
          </p>
          <Button
            type="submit"
            disabled={
              action.busy || !confirmed || Number(value) > available || !value
            }
          >
            Record Settlement
          </Button>
        </fieldset>
      </form>
      {available <= 0 && (
        <Notice>No Platform revenue is currently available to settle.</Notice>
      )}
      {action.error && <Notice error>{action.error}</Notice>}
      {success && (
        <Notice>
          Settlement recorded. Remaining Platform revenue carries forward.
        </Notice>
      )}
    </>
  );
}
function AddPayout({ reload }: { reload: () => void }) {
  const [open,setOpen] = useState(false);
  const [kind,setKind] = useState("Creator");
  const [search,setSearch] = useState("");
  const [page,setPage] = useState(1);
  const [chosen,setChosen] = useState<QueueRow | null>(null);
  const [value,setValue] = useState("");
  const recipients = useResource<QueueRow[]>(`/admin/payouts/recipients/${kind}?search=${encodeURIComponent(search)}&page=${page}&pageSize=20`,open);
  const action = useAction();
  return <><Button variant="secondary" onClick={() => { setOpen(true); setChosen(null); }}>+ Add Payout</Button>
    <Dialog title="Add Payout" open={open} onClose={() => { if (!action.busy) setOpen(false); }}>
      <Field label="Recipient"><select value={kind} onChange={event => { setKind(event.target.value); setChosen(null); setPage(1); }}><option value="Creator">Creators</option><option value="Customer">Customers</option></select></Field>
      <Field label="Search"><input type="search" value={search} onChange={event => { setSearch(event.target.value); setChosen(null); setPage(1); }} /></Field>
      <Resource resource={recipients}>{rows => <DataTable compact rows={rows} rowKey={row => row.subjectId} label="Payout recipients" columns={[
        { label: "Name", cell: row => <Button variant="quiet" onClick={() => { setChosen(row); setValue(String(row.eligibleAmount)); }}>{row.name}</Button> },
        { label: "Available", cell: row => amount(row.available), numeric: true },
        { label: "Eligible", cell: row => amount(row.eligibleAmount), numeric: true },
      ]} empty={<Empty title="No recipients" />} />}</Resource>
      <div className="actions"><Button variant="quiet" disabled={page===1} onClick={() => setPage(page-1)}>Previous</Button><Button variant="quiet" disabled={(recipients.data?.length ?? 0)<20} onClick={() => setPage(page+1)}>Next</Button></div>
      {chosen && <form onSubmit={event => { event.preventDefault(); void action.run(async key => { await post(`/admin/payouts/${kind}/${chosen.subjectId}/prepare`,{amount:Number(value)},key); setOpen(false); reload(); notifyAdminActionCountsChanged(); }); }}>
        <strong data-no-translate>{chosen.name}</strong><p>Available: {amount(chosen.available)} ETB · Eligible: {amount(chosen.eligibleAmount)} ETB</p><p>{chosen.method ?? "No payout destination"} · {chosen.provider} · {chosen.account}</p>
        <Field label="Amount"><MoneyInput value={value} min={0.01} max={chosen.eligibleAmount} onChange={event => setValue(event.target.value)} /></Field>
        <Button type="submit" disabled={action.busy || !chosen.account || !!chosen.payoutId || Number(value)<=0 || Number(value)>chosen.eligibleAmount}>Add to Queue</Button>
      </form>}
      {action.error && <Notice error>{action.error}</Notice>}
    </Dialog></>;
}

export function AdminPayouts() {
  const { user } = useSession();
  if (user?.role === "OperationsAdmin") return <OperationsPayouts />;
  const search = useAdminSearch("/admin/payouts");
  const resource = useResource<PayoutWorkspace>(search.path);
  const [tab, setTab] = useState("Creators");
  const [filter, setFilter] = useState("All");
  const [selected, select] = useState<{ kind: string; row: QueueRow } | null>(
    null,
  );
  const [reference, setReference] = useState("");
  const [payAmount, setPayAmount] = useState("");
  const [confirmed, confirm] = useState(false);
  const action = useAction();
  const [message, setMessage] = useState("");
  const choose = (r: QueueRow) => {
    select({ row: r, kind: tab === "Creators" ? "Creator" : "Customer" });
    setReference("");
    setPayAmount(String(r.payAmount));
    confirm(false);
  };
  return (
    <>
      <PageHeader
        title="Payouts"
        action={<AddPayout reload={resource.reload} />}
        compact
      />
      <AdminSearch value={search} label="Search recipient or reference" statuses={["Eligible", "Ready", "NeedsDestination", "Paid"]} types={["Telebirr", "Bank", "Mpesa"]} dates />
      <AdminPager value={search} count={Math.max(resource.data?.creators.length ?? 0, resource.data?.customers.length ?? 0, resource.data?.history.length ?? 0)} loading={resource.loading} />
      <Tabs
        label="Payout workspaces"
        value={tab}
        onChange={setTab}
        items={["Creators", "Customers", "Platform", "History"].map((s) => ({
          value: s,
          label: s,
        }))}
      />
      {message && <Notice>{message}</Notice>}
      <Resource resource={resource}>
        {(data) => (
          <>
            {["Creators", "Customers"].includes(tab) && (
              <Section title={`${tab} payout queue`} action={<Currency />}>
                <DataTable
                  rows={tab === "Creators" ? data.creators : data.customers}
                  rowKey={(r) => r.subjectId}
                  label={`${tab} payout queue`}
                  columns={[
                    {
                      label: "Name",
                      cell: (r) => r.name,
                    },
                    {
                      label: "Available",
                      cell: (r) => amount(r.available),
                      numeric: true,
                    },
                    {
                      label: "Method",
                      cell: (r) => r.method === "Mpesa" ? "M-PESA" : r.method ?? "Not configured",
                    },
                    {
                      label: "Full Account / Phone",
                      cell: (r) => r.account ?? "—",
                    },
                    {
                      label: "Amount",
                      cell: (r) => amount(r.payAmount),
                      numeric: true,
                    },
                    {
                      label: "Remaining",
                      cell: (r) => amount(Math.max(0, r.available - r.payAmount)),
                      numeric: true,
                    },
                    {
                      label: "Action",
                      cell: (r) => (
                        <Button variant="secondary" disabled={!r.account || r.status === "NeedsDestination"} onClick={() => choose(r)}>
                          Pay
                        </Button>
                      ),
                    },
                  ]}
                  card={(r) => (
                    <>
                      <div className="card-head">
                        <h3>{r.name}</h3>
                        <Badge status={r.status} />
                      </div>
                      <FundsGrid
                        values={[
                          ["Available", r.available],
                          ["Threshold", r.threshold],
                        ]}
                      />
                      <p className="fine-print">{r.method ?? "No payout destination"} · {r.provider ?? "—"} · {r.account ?? "—"}</p>
                      <p className="fine-print">
                        Eligible Since: {date(r.eligibleSinceUtc)}
                      </p>
                      <Button variant="secondary" disabled={!r.account || r.status === "NeedsDestination"} onClick={() => choose(r)}>
                        Pay
                      </Button>
                    </>
                  )}
                  empty={
                    <Empty
                      title="No eligible payouts right now"
                      message="Eligible balances appear here."
                      icon="wallet"
                    />
                  }
                />
              </Section>
            )}
            {tab === "Platform" && (
              <Section title="Platform settlement" action={<Currency />}>
                <PlatformSettlement
                  available={data.platformUnsettled}
                  accrued={data.platformAccrued}
                  settled={data.platformSettled}
                  reload={resource.reload}
                />
              </Section>
            )}
            {tab === "History" && (
              <Section
                title="Payout & settlement history"
                action={<Currency />}
              >
                <Field label="History type">
                  <select
                    className="compact-input"
                    value={filter}
                    onChange={(e) => setFilter(e.target.value)}
                  >
                    {["All", "Creator", "Customer", "Platform"].map((f) => (
                      <option key={f}>{f}</option>
                    ))}
                  </select>
                </Field>
                <DataTable
                  rows={data.history.filter(
                    (r) => filter === "All" || r.kind === filter,
                  )}
                  rowKey={(r) => r.id}
                  label="Admin payout history"
                  columns={[
                    { label: "Type", cell: (r) => r.kind },
                    { label: "Name", cell: (r) => r.name },
                    {
                      label: "Amount",
                      cell: (r) => amount(r.amount),
                      numeric: true,
                    },
                    {
                      label: "Status",
                      cell: (r) => <Badge status={r.status} />,
                    },
                    {
                      label: "Date",
                      cell: (r) => date(r.paidAtUtc ?? r.eligibleAtUtc),
                    },
                    {
                      label: "Reference",
                      cell: (r) => r.reference ?? "Awaiting confirmation",
                    },
                  ]}
                  card={(r) => (
                    <>
                      <div className="card-head">
                        <strong>
                          {r.name} · {r.kind}
                        </strong>
                        <Badge status={r.status} />
                      </div>
                      <p>
                        {amount(r.amount)} ·{" "}
                        {date(r.paidAtUtc ?? r.eligibleAtUtc)}
                      </p>
                      <p className="fine-print">
                        {r.reference ?? "Awaiting confirmation"}
                      </p>
                    </>
                  )}
                  empty={
                    <Empty
                      title="No history to show"
                      message="Recorded payouts and settlements appear here."
                      icon="document"
                    />
                  }
                />
              </Section>
            )}
          </>
        )}
      </Resource>
      <Dialog
        title={
          selected
            ? `Confirm payment to ${selected.row.name}`
            : "Confirm payment"
        }
        open={!!selected}
        onClose={() => {
          if (!action.busy) select(null);
        }}
      >
        <p>
          Available: <strong>{amount(selected?.row.available ?? 0)}</strong>
        </p>
        <p className="fine-print">
          This records an external payment; it does not send money. Only the
          confirmed amount is paid. The rest stays in the account.
        </p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void action.run(async (key) => {
              let payoutId = selected!.row.payoutId;
              if (!payoutId)
                payoutId = (
                  await post<{ id: string }>(
                    `/admin/payouts/${selected!.kind}/${selected!.row.subjectId}/prepare`,
                    { amount: Number(payAmount) },
                    `${key}:prepare`,
                  )
                ).id;
              await post(
                `/admin/payouts/${payoutId}/paid`,
                { reference },
                `${key}:paid`,
              );
              select(null);
              setMessage(
                "Payment confirmed. The remaining balance carries forward.",
              );
              resource.reload();
              notifyAdminActionCountsChanged();
            });
          }}
        >
          <fieldset disabled={action.busy}>
            <p><strong>{selected?.row.method}</strong> · {selected?.row.provider} · {selected?.row.account}</p>
            {selected?.row.method === "Mpesa" && <Notice>Verify the M-PESA wallet and recipient name with the provider before paying. Phone verification alone does not verify wallet ownership.</Notice>}
            <Field label="Amount to Pay">
              <MoneyInput value={payAmount} min={0.01} max={selected?.row.eligibleAmount} disabled={!!selected?.row.payoutId} onChange={(e) => setPayAmount(e.target.value)} />
            </Field>
            <p className="fine-print">Remaining: {amount(Math.max((selected?.row.available ?? 0) - Number(payAmount || 0), 0))}</p>
            <Field label="Payment reference">
              <input
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                maxLength={200}
                required
              />
            </Field>
            <label className="check-line">
              <input
                type="checkbox"
                required
                checked={confirmed}
                onChange={(e) => confirm(e.target.checked)}
              />
              {selected?.row.method === "Mpesa" ? "I verified the M-PESA recipient and completed this payment externally." : "I confirm this payment has been completed externally."}
            </label>
            {action.error && <Notice error>{action.error}</Notice>}
            <Button
              type="submit"
              disabled={action.busy || !confirmed || !reference || !payAmount || Number(payAmount) <= 0 || Number(payAmount) > (selected?.row.eligibleAmount ?? 0)}
            >
              {action.busy ? "Recording…" : "Confirm Paid"}
            </Button>
          </fieldset>
        </form>
      </Dialog>
    </>
  );
}

export function OperationsPayouts() {
  const search = useAdminSearch("/admin/payouts");
  const resource = useResource<OperationsPayoutWorkspace>(search.path);
  const [tab, setTab] = useState("Creators");
  const [selected, select] = useState<{ kind: string; row: QueueRow } | null>(null);
  const [reference, setReference] = useState("");
  const [payAmount, setPayAmount] = useState("");
  const [confirmed, confirm] = useState(false);
  const action = useAction();
  const [message, setMessage] = useState("");
  const choose = (row: QueueRow) => {
    select({ row, kind: tab === "Creators" ? "Creator" : "Customer" });
    setReference("");
    setPayAmount(String(row.payAmount));
    confirm(false);
  };
  return <>
    <PageHeader title="Payouts" compact action={<AddPayout reload={resource.reload} />} /><AdminSearch value={search} label="Search recipient or reference" statuses={["Eligible", "Ready", "NeedsDestination", "Paid"]} types={["Telebirr", "Bank", "Mpesa"]} dates /><AdminPager value={search} count={Math.max(resource.data?.creators.length ?? 0, resource.data?.customers.length ?? 0, resource.data?.history.length ?? 0)} loading={resource.loading} />
    <Tabs label="Payout workspaces" value={tab} onChange={setTab} items={["Creators", "Customers", "History"].map((value) => ({ value, label: value }))} />
    {message && <Notice>{message}</Notice>}
    <Resource resource={resource}>{(data) => <>
      {["Creators", "Customers"].includes(tab) && <Section title={`${tab} payout queue`} action={<Currency />}>
        <DataTable rows={tab === "Creators" ? data.creators : data.customers} rowKey={(r) => r.subjectId} label={`${tab} payout queue`} columns={[
          { label: "Name", cell: (r) => r.name },
          { label: "Available", cell: (r) => amount(r.available), numeric: true },
          { label: "Method", cell: (r) => r.method === "Mpesa" ? "M-PESA" : r.method ?? "Not configured" },
          { label: "Full Account / Phone", cell: (r) => r.account ?? "—" },
          { label: "Amount", cell: (r) => amount(r.payAmount), numeric: true },
          { label: "Remaining", cell: (r) => amount(Math.max(0,r.available-r.payAmount)), numeric: true },
          { label: "Action", cell: (r) => <Button variant="secondary" disabled={!r.account || r.status === "NeedsDestination"} onClick={() => choose(r)}>Pay</Button> },
        ]} card={(r) => <><div className="card-head"><h3>{r.name}</h3><Badge status={r.status} /></div><FundsGrid values={[["Available", r.available], ["Threshold", r.threshold]]} /><p>{r.method ?? "No payout destination"} · {r.provider ?? "—"} · {r.account ?? "—"}</p><Button variant="secondary" disabled={!r.account || r.status === "NeedsDestination"} onClick={() => choose(r)}>Pay</Button></>} empty={<Empty title="No eligible payouts right now" message="Eligible balances appear here." icon="wallet" />} />
      </Section>}
      {tab === "History" && <Section title="Payout history" action={<Currency />}><DataTable rows={data.history} rowKey={(r) => r.id} label="Operations payout history" columns={[
        { label: "Type", cell: (r) => r.kind }, { label: "Name", cell: (r) => r.name }, { label: "Amount", cell: (r) => amount(r.amount), numeric: true },
        { label: "Status", cell: (r) => <Badge status={r.status} /> }, { label: "Date", cell: (r) => date(r.paidAtUtc ?? r.eligibleAtUtc) },
        { label: "Reference", cell: (r) => r.reference ?? "Awaiting confirmation" },
      ]} card={(r) => <><div className="card-head"><strong>{r.name} · {r.kind}</strong><Badge status={r.status} /></div><p>{amount(r.amount)} · {date(r.paidAtUtc ?? r.eligibleAtUtc)}</p><p className="fine-print">{r.reference ?? "Awaiting confirmation"}</p></>} empty={<Empty title="No payout history" message="Recorded Creator and Customer payouts appear here." icon="document" />} /></Section>}
    </>}</Resource>
    <Dialog title={selected ? `Confirm payment to ${selected.row.name}` : "Confirm payment"} open={!!selected} onClose={() => { if (!action.busy) select(null); }}>
      <p>Available: <strong>{amount(selected?.row.available ?? 0)}</strong></p>
      <p>{selected?.row.method} · {selected?.row.provider} · {selected?.row.account}</p>
      {selected?.row.method === "Mpesa" && <Notice>Verify the M-PESA wallet and recipient name with the provider before paying. Phone verification alone does not verify wallet ownership.</Notice>}
      <p className="fine-print">This records an external payment; it does not send money.</p>
      <form onSubmit={(e) => { e.preventDefault(); void action.run(async (key) => { let payoutId = selected!.row.payoutId; if (!payoutId) payoutId = (await post<{ id: string }>(`/admin/payouts/${selected!.kind}/${selected!.row.subjectId}/prepare`, { amount: Number(payAmount) }, `${key}:prepare`)).id; await post(`/admin/payouts/${payoutId}/paid`, { reference }, `${key}:paid`); select(null); setMessage("Payment confirmed. The remaining balance carries forward."); resource.reload(); notifyAdminActionCountsChanged(); }); }}>
        <fieldset disabled={action.busy}><Field label="Amount to Pay"><MoneyInput value={payAmount} min={0.01} max={selected?.row.eligibleAmount} disabled={!!selected?.row.payoutId} onChange={(e) => setPayAmount(e.target.value)} /></Field><p className="fine-print">Remaining: {amount(Math.max((selected?.row.available ?? 0) - Number(payAmount || 0), 0))}</p><Field label="Payment reference"><input value={reference} onChange={(e) => setReference(e.target.value)} maxLength={200} required /></Field><label className="check-line"><input type="checkbox" required checked={confirmed} onChange={(e) => confirm(e.target.checked)} /> {selected?.row.method === "Mpesa" ? "I verified the M-PESA recipient and completed this payment externally." : "I confirm this payment has been completed externally."}</label>{action.error && <Notice error>{action.error}</Notice>}<Button type="submit" disabled={action.busy || !confirmed || !reference || !payAmount || Number(payAmount) <= 0 || Number(payAmount) > (selected?.row.eligibleAmount ?? 0)}>{action.busy ? "Recording…" : "Confirm Paid"}</Button></fieldset>
      </form>
    </Dialog>
  </>;
}
export function AdminPlatformRevenue() {
  const resource = useResource<PlatformSummary>("/admin/platform");
  return (
    <>
      <PageHeader
        eyebrow="Platform accounting"
        title="Platform Revenue"
        description="Accrued and settled revenue from the same authoritative financial journal."
      />
      <Resource resource={resource}>
        {(p) => (
          <>
            <Section title="Revenue overview" action={<Currency />}>
              <PlatformSettlement
                available={p.unsettled.amount}
                accrued={p.accrued.amount}
                settled={p.settled.amount}
                reload={resource.reload}
              />
            </Section>
            <Section title="Settlement history" action={<Currency />}>
              {p.history.length ? (
                p.history.map((s) => (
                  <div className="amount-row" key={s.id}>
                    <div>
                      <strong>{s.reference}</strong>
                      <small>{dateTime(s.settledAtUtc)}</small>
                    </div>
                    <strong>{amount(s.amount.amount)}</strong>
                  </div>
                ))
              ) : (
                <Empty
                  title="No settlements yet"
                  message="Recorded Platform settlements will appear here."
                  icon="wallet"
                />
              )}
            </Section>
          </>
        )}
      </Resource>
    </>
  );
}
