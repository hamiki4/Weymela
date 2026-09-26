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
            description="Discover Business-created opportunities, create something genuine and grow your earnings."
            action={
              <ActionLink to="/creator/discover">Discover</ActionLink>
            }
          />
          <div className="metric-grid">
            <Metric
              label="Available Earnings"
              value={amount(data.earnings.availableEarnings)}
            note="Yours across every Promotion"
              emphasis
              icon="wallet"
            />
            <Metric
              label="Active Promotions"
              value={count(data.activeCampaigns)}
              note="Your current collaborations"
              icon="campaign"
            />
          </div>
          <div className="quick-grid">
            <Link className="quick-card" to="/creator/requests">
              <Icon name="people" />
              <span className="quick-value">{data.requests}</span>
              <strong>Promotion Requests</strong>
            </Link>
            <Link className="quick-card" to="/creator/campaigns">
              <Icon name="campaign" />
              <strong>Active Promotions</strong>
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
                Promotions made
                <br />
                for your kind of creativity.
              </h2>
              <p>
                Discovery matches your category, region and verified profile to
                funded Promotions.
              </p>
              <ActionLink to="/creator/discover" secondary>
                Find your next opportunity
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
        {row.requestStatus ? "View Promotion" : "Request to Join"}
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
        title="Discover"
        description="Funded Promotions that match your verified profile. Your next collaboration starts here."
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
                title="No available Promotions right now"
                message="New opportunities will appear when a funded Promotion matches your category, region and verified metrics."
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
  const [socialProfileId, setSocialProfileId] = useState("");
  return (
    <>
      <Link className="back-link" to="/creator/discover">
        ← Discover
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
              <Section title="Promotion details">
                {p.description?.trim() && <p className="preserve-lines">{p.description}</p>}
                {p.slogan?.trim() && <p className="creator-opportunity-slogan">{p.slogan}</p>}
                {p.requirements?.trim() && <div className="pricing-note">
                  <strong>Requirements</strong>
                  <p className="preserve-lines">{p.requirements}</p>
                </div>}
                <dl className="detail-list">
                  <div>
                    <dt>Duration</dt>
                    <dd>
                      {p.promotionLiveDurationDays} days after you go live
                    </dd>
                  </div>
                  {p.region && <div>
                    <dt>Region</dt>
                    <dd>{p.region}</dd>
                  </div>}
                  {p.category && <div>
                    <dt>Category</dt>
                    <dd>{p.category}</dd>
                  </div>}
                  {!!p.minimumVerifiedFollowers && (
                    <div>
                      <dt>Verified followers</dt>
                      <dd>{count(p.minimumVerifiedFollowers)} minimum</dd>
                    </div>
                  )}
                </dl>
                {!!p.platforms?.length && <div className="creator-platform-counts" aria-label="Social platform availability">{p.platforms.map((slot) => <span key={slot.platform} className={slot.available <= 0 ? "platform-full" : ""}><strong>{slot.platform}</strong> {slot.approved}/{slot.capacity}{slot.available <= 0 ? " · Full" : ""}</span>)}</div>}
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
              </Section>
            </div>
            <Section
              title={p.requestStatus ? "Your request" : "Request to Join"}
            >
              {p.requestStatus ? (
                <Notice>Request {p.requestStatus.toLowerCase()}. {p.requestStatus === "Pending" && "Waiting for approval."} <Link to="/creator/promotions">My Promotions</Link></Notice>
              ) : (
                <form
                  className="contained-form"
                  onSubmit={(e) => {
                    e.preventDefault();
                    const selectedProfile = p.eligibleSocialProfiles?.find((profile) => profile.id === socialProfileId);
                    if (p.platforms?.length && !selectedProfile) return;
                    void action.run(async (key) => {
                      await post(
                        `/creator/promotions/${id}/request`,
                        { message, contentConcept: concept || null, ...(selectedProfile ? { platform: selectedProfile.platform, creatorSocialProfileId: selectedProfile.id } : {}) },
                        key,
                      );
                      resource.reload();
                    });
                  }}
                >
                  <fieldset disabled={action.busy}>
                    {!!p.platforms?.length && <div className="creator-platform-selector" role="radiogroup" aria-label="Choose platform">
                      <strong>Choose platform</strong>
                      {p.platforms.map((slot) => {
                        const profiles = p.eligibleSocialProfiles?.filter((profile) => profile.platform === slot.platform) ?? [];
                        return <div className="creator-platform-option" key={slot.platform}>
                          <strong>{slot.platform}</strong><small>{slot.available > 0 ? `${slot.available} available` : "Full"}</small>
                          {profiles.map((profile) => <label key={profile.id}><input type="radio" name="creator-platform" value={profile.id} checked={socialProfileId === profile.id} onChange={() => setSocialProfileId(profile.id)} disabled={slot.available <= 0} />{profile.profileUrl}</label>)}
                          {profiles.length === 0 && <small>{slot.available > 0 ? "Connect a social profile to request" : "Unavailable"}</small>}
                        </div>;
                      })}
                    </div>}
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
                    <Button type="submit" disabled={action.busy || (!!p.platforms?.length && !p.eligibleSocialProfiles?.some((profile) => profile.id === socialProfileId && p.platforms?.some((slot) => slot.platform === profile.platform && slot.available > 0)))}>
                      {action.busy ? "Sending request…" : "Submit Request"}
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
export function CreatorEarnings() {
  const resource = useResource<Earnings>("/creator/earnings");
  const action = useAction();
  const [requested, setRequested] = useState(false);
  return (
    <>
      <PageHeader
        title="Earnings"
      />
      <Resource resource={resource}>
        {(data) => (
          <>
            <Section title="Available Earnings" className="creator-earnings-summary">
                <Eligibility data={data} />
                <Button
                  disabled={action.busy || data.amountNeeded > 0 || data.payoutHistory.some((r) => r.status === "Eligible")}
                  onClick={() => void action.run(async (key) => {
                    await post("/creator/payouts/request", {}, key);
                    setRequested(true);
                    resource.reload();
                  })}
                >
                  {data.payoutHistory.some((r) => r.status === "Eligible") ? "Awaiting payment confirmation" : "Request Payout"}
                </Button>
                {requested && <Notice>Payout request recorded. Your balance changes only after payment is confirmed.</Notice>}
                {action.error && <Notice error>{action.error}</Notice>}
            </Section>
            <Section title="Earning History" action={<Currency />}>
                <DataTable
                  rows={data.history}
                  rowKey={(r) => r.id}
                  label="Earning History"
                  columns={[
                    { label: "Promotion or source", cell: (r) => r.campaign },
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
            <Section title="Payout history" action={<Currency />}>
              <PayoutHistory rows={data.payoutHistory} />
            </Section>
            <details className="creator-how-you-earn"><summary>How You Earn</summary><CreatorHowYouEarn /></details>
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
        title="My Promotions"
        description="Keep track of the Promotions you’ve requested to join."
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Your requests">
            <DataTable
              rows={rows}
              rowKey={(r) => r.id}
              label="Promotion Requests"
              columns={[
                {
                  label: "Promotion",
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
                    <ActionLink to="/creator/promotions" secondary>
                      My Promotions
                    </ActionLink>
                  )}
                </>
              )}
              empty={
                <Empty
                  title="No Promotion requests yet"
                  message="Discover funded Promotions that match your creativity."
                  action={
                    <ActionLink to="/creator/discover">
                      Discover opportunities
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
