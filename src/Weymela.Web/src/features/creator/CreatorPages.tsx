import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  CreatorHome,
  CreatorPricing,
  Earnings,
  Opportunity,
  Payout,
} from "../../api/types";
import {
  ActionLink,
  Badge,
  Button,
  Currency,
  DataTable,
  Empty,
  Field,
  Metric,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import {
  amount,
  campaignType,
  count,
  date,
  isViewAndSale,
  isViewOnly,
} from "../../ui/format";
import { Icon } from "../../ui/Icon";

export function CreatorDashboard() {
  const resource = useResource<CreatorHome>("/creator/home");
  return (
    <Resource resource={resource}>
      {(data) => (
        <>
          <PageHeader
            eyebrow={`Welcome, ${data.creator.displayName}`}
            title="Make your creativity count."
            description="Find the right Campaigns, create something genuine and grow your earnings."
            action={
              <ActionLink to="/creator/discover">Discover Campaigns</ActionLink>
            }
          />
          <div className="metric-grid">
            <Metric
              label="Available Earnings"
              value={amount(data.earnings.availableEarnings)}
            note="Yours across every Campaign"
              emphasis
              icon="wallet"
            />
            <Metric
              label="Active Campaigns"
              value={count(data.activeCampaigns)}
              note="Your current collaborations"
              icon="campaign"
            />
          </div>
          <div className="quick-grid">
            <Link className="quick-card" to="/creator/requests">
              <Icon name="people" />
              <span className="quick-value">{data.requests}</span>
              <strong>Campaign Requests</strong>
            </Link>
            <Link className="quick-card" to="/creator/campaigns">
              <Icon name="campaign" />
              <strong>Active Campaigns</strong>
              <span>Content, views and your progress.</span>
            </Link>
            <Link className="quick-card" to="/creator/earnings">
              <Icon name="wallet" />
              <strong>Your Earnings</strong>
              <span>Every verified moment counts.</span>
            </Link>
            <Link className="quick-card pricing-home-card" to="/creator/pricing">
              <Icon name="spark" />
              <strong>Pricing</strong>
              <Icon name="arrow" />
            </Link>
          </div>
          <div className="two-column section-kicker-space">
            <Section title="Your next payout">
              <Eligibility data={data.earnings} />
              <ActionLink to="/creator/payouts" secondary>
                Payout details
              </ActionLink>
            </Section>
            <div className="overview-highlight">
              <p className="eyebrow">The right fit, already found</p>
              <h2>
                Campaigns made
                <br />
                for your kind of creativity.
              </h2>
              <p>
                Discovery matches your category, region and verified profile to
                funded Campaigns.
              </p>
              <ActionLink to="/creator/discover" secondary>
                Find your next Campaign
              </ActionLink>
            </div>
          </div>
        </>
      )}
    </Resource>
  );
}
function OpportunityCard({ row }: { row: Opportunity }) {
  return (
    <article className="campaign-card">
      <div className="campaign-art" aria-hidden="true">
        <span>{row.business.displayName.charAt(0)}</span>
        <Icon name="campaign" />
      </div>
      <p className="card-eyebrow">
        {row.business.displayName} · {row.business.region}
      </p>
      <div className="card-head">
        <h3>{row.title}</h3>
        {row.requestStatus && <Badge status={row.requestStatus} />}
      </div>
      <div className="tag-row">
        <Badge status={row.type} />
        {row.category && <span className="badge">{row.category}</span>}
      </div>
      <p>{row.requirements || row.description}</p>
      <div className="pricing-note">
        <strong>
          {amount(row.earnings.youEarn)} per {count(row.earnings.views)}{" "}
          verified views
        </strong>
        {isViewAndSale(row.type) && (
          <small>
            Plus {amount(row.earnings.saleCommissionPercent)}% per verified
            sale.
          </small>
        )}
      </div>
      <p className="fine-print">
        {date(row.startUtc)} – {date(row.endUtc)}
      </p>
      <ActionLink to={`/creator/discover/${row.id}`} secondary>
        {row.requestStatus ? "View Campaign" : "Join Campaign"}
      </ActionLink>
    </article>
  );
}
export function CreatorDiscovery() {
  const resource = useResource<Opportunity[]>("/creator/discover");
  return (
    <>
      <PageHeader
        eyebrow="Create with a purpose"
        title="Discover Campaigns"
        description="Funded Campaigns that match your verified profile. Your next collaboration starts here."
      />
      <Resource resource={resource}>
        {(rows) =>
          rows.length ? (
            <div className="card-stack campaign-grid">
              {rows.map((row) => (
                <OpportunityCard key={row.id} row={row} />
              ))}
            </div>
          ) : (
            <Section title="Made for your profile">
              <Empty
                title="No available Campaigns right now"
                message="New opportunities will appear when a funded Campaign matches your category, region and verified metrics."
              />
            </Section>
          )
        }
      </Resource>
    </>
  );
}
export function CreatorOpportunity() {
  const { id } = useParams();
  const resource = useResource<Opportunity>(`/creator/discover/${id}`);
  const action = useAction();
  const [message, setMessage] = useState("");
  const [concept, setConcept] = useState("");
  return (
    <>
      <Link className="back-link" to="/creator/discover">
        ← Discover Campaigns
      </Link>
      <Resource resource={resource}>
        {(p) => (
          <>
            <PageHeader
              eyebrow={p.business.displayName}
              title={p.title}
              description={campaignType(p.type)}
              action={p.requestStatus && <Badge status={p.requestStatus} />}
            />
            <div className="content-grid">
              <Section title="The Campaign">
                <p className="preserve-lines">{p.description}</p>
                <div className="pricing-note">
                  <strong>Requirements</strong>
                  <p className="preserve-lines">
                    {p.requirements || "Bring your own creative approach."}
                  </p>
                </div>
                <dl className="detail-list">
                  <div>
                    <dt>Duration</dt>
                    <dd>
                      {date(p.startUtc)} – {date(p.endUtc)}
                    </dd>
                  </div>
                  <div>
                    <dt>Region</dt>
                    <dd>{p.region || "All regions"}</dd>
                  </div>
                  <div>
                    <dt>Category</dt>
                    <dd>{p.category || "All categories"}</dd>
                  </div>
                  {!!p.minimumVerifiedFollowers && (
                    <div>
                      <dt>Verified followers</dt>
                      <dd>{count(p.minimumVerifiedFollowers)} minimum</dd>
                    </div>
                  )}
                </dl>
                <Notice>{p.eligibility}</Notice>
              </Section>
              <Section title="How You Earn" action={<Currency />}>
                <div className="price-feature">
                  <strong>{amount(p.earnings.youEarn)}</strong>
                  <span>per {count(p.earnings.views)} verified views</span>
                </div>
                {isViewAndSale(p.type) && (
                  <p>
                    Plus {amount(p.earnings.saleCommissionPercent)}% per
                    verified sale.
                  </p>
                )}
                <p className="fine-print">
                  These are the saved earning terms for this Campaign. Your
                  Creator Budget will be shown after Business approval.
                </p>
              </Section>
            </div>
            <Section
              title={p.requestStatus ? "Your request" : "Request to Join"}
              description="Share a short introduction and the idea you’d love to create."
            >
              {p.requestStatus ? (
                <Notice>
                  Your request is {p.requestStatus.toLowerCase()}.{" "}
                  <Link to="/creator/requests">View your requests</Link>
                </Notice>
              ) : (
                <form
                  className="contained-form"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void action.run(async (key) => {
                      await post(
                        `/creator/campaigns/${id}/join`,
                        { message, contentConcept: concept || null },
                        key,
                      );
                      resource.reload();
                    });
                  }}
                >
                  <fieldset disabled={action.busy}>
                    <Field label="Short message">
                      <textarea
                        value={message}
                        onChange={(e) => setMessage(e.target.value)}
                        maxLength={1000}
                        rows={3}
                      />
                    </Field>
                    <Field label="Content concept (optional)">
                      <textarea
                        value={concept}
                        onChange={(e) => setConcept(e.target.value)}
                        maxLength={2000}
                        rows={3}
                      />
                    </Field>
                    {action.error && <Notice error>{action.error}</Notice>}
                    <Button type="submit" disabled={action.busy}>
                      {action.busy ? "Sending request…" : "Request to Join"}
                    </Button>
                  </fieldset>
                </form>
              )}
            </Section>
          </>
        )}
      </Resource>
    </>
  );
}
export function CreatorHowYouEarn() {
  const resource = useResource<CreatorPricing>("/creator/pricing");
  return (
    <>
      <PageHeader
        eyebrow="Clear rewards, real creativity"
        title="Pricing"
        description="Current earning rates. Existing Promotions keep their saved earning terms."
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
                    <h3>{campaignType(r.type)}</h3>
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
            </div>
            <div className="threshold-note">
              <strong>
                Minimum to cash out: {amount(p.minimumToCashOut)}
              </strong>
              <p>
                Earnings from your Campaigns accumulate together. Any balance
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
          <small>Available Earnings</small>
          <strong>{amount(data.availableEarnings)}</strong>
        </div>
        <Icon name="wallet" />
      </div>
      <p>
        Minimum to cash out:{" "}
        <strong>{amount(data.minimumToCashOut)}</strong>
      </p>
      {data.amountNeeded > 0 ? (
        <p className="muted">{amount(data.amountNeeded)} more needed</p>
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
export function PayoutHistory({ rows }: { rows: Payout[] }) {
  return (
    <DataTable
      rows={rows}
      rowKey={(r) => r.id}
      label="Payout history"
      columns={[
        { label: "Amount", cell: (r) => amount(r.amount), numeric: true },
        { label: "Status", cell: (r) => <Badge status={r.status} /> },
        { label: "Date", cell: (r) => date(r.paidAtUtc ?? r.eligibleAtUtc) },
        {
          label: "Reference",
          cell: (r) => r.reference || "Awaiting payment confirmation",
        },
      ]}
      card={(r) => (
        <>
          <div className="card-head">
            <strong>{amount(r.amount)}</strong>
            <Badge status={r.status} />
          </div>
          <p>{date(r.paidAtUtc ?? r.eligibleAtUtc)}</p>
          <p className="fine-print">
            {r.reference || "Awaiting payment confirmation"}
          </p>
        </>
      )}
      empty={
        <Empty
          title="No payout history yet"
          message="Confirmed payouts will appear here. Your remaining earnings always carry forward."
          icon="wallet"
        />
      }
    />
  );
}
export function CreatorEarnings({ payout = false }: { payout?: boolean }) {
  const resource = useResource<Earnings>("/creator/earnings");
  const action = useAction();
  const [requested, setRequested] = useState(false);
  return (
    <>
      <PageHeader
        eyebrow="Your creativity, rewarded"
        title={payout ? "Your Payouts" : "Your Earnings"}
        description="One earnings balance across all your Campaigns. No weekly or monthly cash-out schedule."
      />
      <Resource resource={resource}>
        {(data) => (
          <>
            <div className="two-column">
              <Section title="Available Earnings">
                <Eligibility data={data} />
                {payout && (
                  <>
                    <Button
                      disabled={
                        action.busy ||
                        data.amountNeeded > 0 ||
                        data.payoutHistory.some((r) => r.status === "Eligible")
                      }
                      onClick={() =>
                        void action.run(async (key) => {
                          await post("/creator/payouts/request", {}, key);
                          setRequested(true);
                          resource.reload();
                        })
                      }
                    >
                      {data.payoutHistory.some((r) => r.status === "Eligible")
                        ? "Awaiting payment confirmation"
                        : "Request Payout"}
                    </Button>
                    {requested && (
                      <Notice>
                        Payout request recorded. Your balance changes only after
                        payment is confirmed.
                      </Notice>
                    )}
                    {action.error && <Notice error>{action.error}</Notice>}
                  </>
                )}
              </Section>
              <div className="overview-highlight">
                <p className="eyebrow">Every Campaign adds up</p>
                <h2>
                  One balance.
                  <br />
                  More possibilities.
                </h2>
                <p>
                  View Rewards and Sale Commissions build the same earnings
                  balance. A payout uses your threshold amount; the rest stays
                  with you.
                </p>
                <ActionLink to="/creator/pricing" secondary>
                  How You Earn
                </ActionLink>
              </div>
            </div>
            {!payout && (
              <Section title="Earning History" action={<Currency />}>
                <DataTable
                  rows={data.history}
                  rowKey={(r) => r.id}
                  label="Earning History"
                  columns={[
                    { label: "Campaign", cell: (r) => r.campaign },
                    { label: "Source", cell: (r) => r.source },
                    {
                      label: "Amount",
                      cell: (r) => amount(r.amount),
                      numeric: true,
                    },
                    { label: "Date", cell: (r) => date(r.atUtc) },
                  ]}
                  card={(r) => (
                    <>
                      <div className="card-head">
                        <strong>{r.campaign}</strong>
                        <strong>{amount(r.amount)}</strong>
                      </div>
                      <p className="fine-print">
                        {r.source} · {date(r.atUtc)}
                      </p>
                    </>
                  )}
                  empty={
                    <Empty
                      title="No earnings yet"
                      message="Verified views and eligible sales will grow your earnings here."
                      icon="wallet"
                    />
                  }
                />
              </Section>
            )}
            <Section title="Payout history" action={<Currency />}>
              <PayoutHistory rows={data.payoutHistory} />
            </Section>
          </>
        )}
      </Resource>
    </>
  );
}
export function CreatorRequests() {
  const resource = useResource<
    {
      id: string;
      campaignId: string;
      campaign: string;
      business: string;
      type: string;
      status: string;
      appliedAtUtc: string;
    }[]
  >("/creator/requests");
  return (
    <>
      <PageHeader
        eyebrow="Your next collaborations"
        title="Campaign Requests"
        description="Keep track of the Campaigns you’ve asked to join."
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Your requests">
            <DataTable
              rows={rows}
              rowKey={(r) => r.id}
              label="Campaign Requests"
              columns={[
                {
                  label: "Campaign",
                  cell: (r) => (
                    <>
                      <strong>{r.campaign}</strong>
                      <small>{r.business}</small>
                    </>
                  ),
                },
                { label: "Type", cell: (r) => campaignType(r.type) },
                { label: "Requested", cell: (r) => date(r.appliedAtUtc) },
                { label: "Status", cell: (r) => <Badge status={r.status} /> },
              ]}
              card={(r) => (
                <>
                  <div className="card-head">
                    <div>
                      <p className="subheading">{r.business}</p>
                      <h3>{r.campaign}</h3>
                    </div>
                    <Badge status={r.status} />
                  </div>
                  <p className="fine-print">
                    {campaignType(r.type)} · Requested {date(r.appliedAtUtc)}
                  </p>
                  {r.status === "Approved" && (
                    <ActionLink to="/creator/campaigns" secondary>
                      Your Campaigns
                    </ActionLink>
                  )}
                </>
              )}
              empty={
                <Empty
                  title="No Campaign requests yet"
                  message="Discover funded Campaigns that match your creativity."
                  action={
                    <ActionLink to="/creator/discover">
                      Discover Campaigns
                    </ActionLink>
                  }
                />
              }
            />
          </Section>
        )}
      </Resource>
    </>
  );
}
