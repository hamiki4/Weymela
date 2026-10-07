import { useResource } from "../../api/client";
import type {
  CreatorPricing,
  Earnings,
} from "../../api/types";
import {
  ActionLink,
  Currency,
  DataTable,
  Empty,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { PayoutDestinationForm } from "../PayoutDestinationForm";
import {
  amount,
  count,
  dateTime,
  isViewOnly,
  promotionTypeLabel,
} from "../../ui/format";
import { Icon } from "../../ui/Icon";

export function CreatorHowYouEarn() {
  const resource = useResource<CreatorPricing>("/creator/pricing");
  return (
    <>
      <PageHeader
        eyebrow="Clear rewards, real creativity"
        title="How You Earn"
        description="Creator rewards are recorded from verified activity using the saved terms for each Promotion."
      />
      <Resource resource={resource}>
        {(p) => (
          <Section
            title="Your earning terms"
            description="Current earning rates. Existing Promotions keep their saved earning terms."
            action={<Currency />}
          >
            <div
              className="pricing-card-grid"
              role="region"
              aria-label="Creator earning options"
            >
              {p.rows.map((r) => (
                <article className="pricing-card" key={r.type}>
                  <div className="pricing-card-heading">
                    <span className="pricing-card-kicker">Promotion type</span>
                    <h3>{promotionTypeLabel(r.type)}</h3>
                  </div>
                  <dl className="pricing-card-details">
                    <div>
                      <dt>Verified views</dt>
                      <dd>{count(r.views)}</dd>
                    </div>
                    <div>
                      <dt>You earn</dt>
                      <dd>{amount(r.youEarn)}</dd>
                    </div>
                    <div>
                      <dt>Eligible sale</dt>
                      <dd>
                        {isViewOnly(r.type)
                          ? "Not included"
                          : `You earn ${amount(r.saleCommissionPercent)}% per eligible sale`}
                      </dd>
                    </div>
                  </dl>
                </article>
              ))}
              <article className="pricing-card"><div className="pricing-card-heading"><span className="pricing-card-kicker">Content creation</span><h3>UGC</h3></div><p>Earn the net amount shown on each UGC opportunity when your content is approved.</p></article>
            </div>
            <div className="threshold-note">
              <strong>
                Minimum to cash out: {amount(p.minimumToCashOut)}
              </strong>
              <p>
                Earnings from your Promotions accumulate together. Any balance
                left after a payout carries forward.
              </p>
            </div>
          </Section>
        )}
      </Resource>
    </>
  );
}
export function Eligibility({ data }: { data: Earnings }) {
  return (
    <>
      <div className="payout-summary">
        <div>
          <small>Available</small>
          <strong>{amount(data.availableEarnings)}</strong>
        </div>
        <Icon name="wallet" />
      </div>
      <p>
        Minimum to cash out:{" "}
        <strong>{amount(data.minimumToCashOut)}</strong>
      </p>
      {data.amountNeeded > 0 ? (
        <p className="muted">Amount remaining: {amount(data.amountNeeded)}</p>
      ) : (
        <Notice>Eligible for payout · {amount(data.eligibleAmount)}</Notice>
      )}
      <progress
        className="progress"
        value={Math.min(data.availableEarnings, data.minimumToCashOut)}
        max={data.minimumToCashOut}
        aria-label="Progress toward payout threshold"
      />
    </>
  );
}
export function CreatorEarnings() {
  const resource = useResource<Earnings>("/creator/earnings");
  return (
    <>
      <PageHeader title="Earnings" compact action={<ActionLink to="/creator/pricing" secondary>How You Earn</ActionLink>} />
      <Resource resource={resource}>
        {(data) => (
          <>
            <Section title="Available Earnings" className="creator-earnings-summary">
                <Eligibility data={data} />
            </Section>
            <Section title="Payout Destination"><PayoutDestinationForm endpoint="/creator/payout-destination" /></Section>
            <Section title="Earning History" action={<Currency />}>
                <DataTable
                  rows={data.history}
                  rowKey={(r) => r.id}
                  label="Earning History"
                  columns={[
                    { label: "Business", cell: (r) => r.business ?? r.campaign },
                    { label: "Earning Type", cell: (r) => r.sourceType === "UGC" ? "UGC" : r.source === "Sale Earnings" ? "Sale Commission" : r.source === "View Earnings" ? "Verified Views" : r.source },
                    {
                      label: "Amount",
                      cell: (r) => `+${amount(r.amount)} ETB`,
                      numeric: true,
                    },
                    { label: "Date", cell: (r) => dateTime(r.atUtc) },
                  ]}
                  card={(r) => (
                    <>
                      <div className="card-head">
                        <strong>{r.business ?? r.campaign}</strong>
                        <strong>+{amount(r.amount)} ETB</strong>
                      </div>
                      <p className="fine-print">
                        {r.sourceType === "UGC" ? "UGC" : r.source === "Sale Earnings" ? "Sale Commission" : r.source === "View Earnings" ? "Verified Views" : r.source} · {dateTime(r.atUtc)} · {r.campaign}
                      </p>
                    </>
                  )}
                  empty={
                    <Empty
                      title="No earnings yet."
                      icon="wallet"
                    />
                  }
                />
              </Section>
          </>
        )}
      </Resource>
    </>
  );
}
