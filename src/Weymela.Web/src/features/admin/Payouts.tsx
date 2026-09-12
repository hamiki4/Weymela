import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type {
  PayoutWorkspace,
  PlatformSummary,
  QueueRow,
} from "../../api/types";
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
          <Field label="Settlement Amount (ETB)">
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
export function AdminPayouts() {
  const resource = useResource<PayoutWorkspace>("/admin/payouts");
  const [tab, setTab] = useState("Creators");
  const [filter, setFilter] = useState("All");
  const [selected, select] = useState<{ kind: string; row: QueueRow } | null>(
    null,
  );
  const [reference, setReference] = useState("");
  const [confirmed, confirm] = useState(false);
  const action = useAction();
  const [message, setMessage] = useState("");
  const choose = (r: QueueRow) => {
    select({ row: r, kind: tab === "Creators" ? "Creator" : "Customer" });
    setReference("");
    confirm(false);
  };
  return (
    <>
      <PageHeader
        eyebrow="Payouts & settlement"
        title="Payouts"
        description="Threshold-based payouts. Confirm external payments here; remaining balances always carry forward."
      />
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
                      label: tab === "Creators" ? "Creator" : "Customer",
                      cell: (r) => r.name,
                    },
                    {
                      label: "Available",
                      cell: (r) => amount(r.available),
                      numeric: true,
                    },
                    {
                      label: "Threshold",
                      cell: (r) => amount(r.threshold),
                      numeric: true,
                    },
                    {
                      label: "Pay Amount",
                      cell: (r) => amount(r.payAmount),
                      numeric: true,
                    },
                    {
                      label: "Eligible Since",
                      cell: (r) => date(r.eligibleSinceUtc),
                    },
                    {
                      label: "Status",
                      cell: (r) => <Badge status={r.status} />,
                    },
                    {
                      label: "Action",
                      cell: (r) => (
                        <Button variant="secondary" onClick={() => choose(r)}>
                          Mark Paid
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
                          ["Pay Amount", r.payAmount],
                        ]}
                      />
                      <p className="fine-print">
                        Eligible Since: {date(r.eligibleSinceUtc)}
                      </p>
                      <Button variant="secondary" onClick={() => choose(r)}>
                        Mark Paid
                      </Button>
                    </>
                  )}
                  empty={
                    <Empty
                      title="No eligible payouts right now"
                      message="Accounts appear when their available balance reaches the effective payout threshold."
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
                        {amount(r.amount)} ETB ·{" "}
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
          Pay Amount:{" "}
          <strong>{amount(selected?.row.payAmount ?? 0)} ETB</strong>
        </p>
        <p className="fine-print">
          This records an external payment; it does not send money. Only the
          threshold amount is paid. The rest stays in the account.
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
                    {},
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
            });
          }}
        >
          <fieldset disabled={action.busy}>
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
              I confirm this payment has been completed externally.
            </label>
            {action.error && <Notice error>{action.error}</Notice>}
            <Button
              type="submit"
              disabled={action.busy || !confirmed || !reference}
            >
              {action.busy ? "Recording…" : "Confirm Paid"}
            </Button>
          </fieldset>
        </form>
      </Dialog>
    </>
  );
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
