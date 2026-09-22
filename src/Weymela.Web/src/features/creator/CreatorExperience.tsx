import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  CreatorCampaign,
  CreatorHome,
  CreatorRequest,
  Opportunity,
  UgcAssignment,
  UgcCard,
} from "../../api/types";
import {
  ActionLink,
  Badge,
  Button,
  Empty,
  Metric,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, campaignType, count, date } from "../../ui/format";
import { Icon } from "../../ui/Icon";
import { CreatorAssignmentCard } from "../business/UgcPages";

export function CreatorDashboard() {
  const home = useResource<CreatorHome>("/creator/home");
  const promotions = useResource<CreatorCampaign[]>("/creator/campaigns");
  return <>
    <Resource resource={home}>{(data) => <>
      <PageHeader eyebrow={`Creator · ${data.creator.displayName}`} title="Dashboard" description="Your opportunities, live Promotions and earnings at a glance." />
      <div className="metric-grid creator-metrics">
        <Link className="creator-dashboard-metric" to="/creator/promotions?filter=Requested"><Metric label="Pending Requests" value={count(data.requests)} icon="people" /></Link>
        <Link className="creator-dashboard-metric" to="/creator/promotions?filter=Live"><Metric label="Active Promotions" value={count(data.activeCampaigns)} icon="campaign" /></Link>
        <Link className="creator-dashboard-metric" to="/creator/earnings"><Metric label="Available Earnings" value={amount(data.earnings.availableEarnings)} icon="wallet" emphasis /></Link>
        <Link className="creator-dashboard-metric" to="/creator/earnings"><Metric label="Minimum to Cash Out" value={amount(data.earnings.minimumToCashOut)} icon="money" /></Link>
      </div>
      <section className="creator-discover-cta">
        <div><p className="eyebrow">Your next opportunity</p><h2>Find Your Next Opportunity</h2><p>Discover funded Promotions and UGC opportunities from Businesses.</p></div>
        <ActionLink to="/creator/discover">Discover</ActionLink>
      </section>
      <Section title="Recent Activity" description="Recorded Creator earnings and payouts from your account.">
        {data.earnings.history.length || data.earnings.payoutHistory.length ? <div className="creator-activity-list">
          {data.earnings.history.slice(0, 3).map((item) => <article key={item.id}><span className="creator-activity-icon"><Icon name="spark" /></span><div><strong>Verified earning recorded</strong><p>{item.campaign} · {item.source}</p></div><small>{date(item.atUtc)} · {amount(item.amount)}</small></article>)}
          {data.earnings.payoutHistory.filter((item) => item.paidAtUtc).slice(0, 2).map((item) => <article key={item.id}><span className="creator-activity-icon"><Icon name="wallet" /></span><div><strong>Payout paid</strong><p>{item.status}</p></div><small>{date(item.paidAtUtc!)} · {amount(item.amount)}</small></article>)}
        </div> : <Empty title="No recent activity" message="Approved content, live Promotions and recorded earnings will appear here when available." />}
      </Section>
    </>}</Resource>
    <Section title="Recent Active Promotions" action={<ActionLink to="/creator/promotions" secondary>My Promotions</ActionLink>}>
      <Resource resource={promotions}>{(rows) => {
        const live = rows.filter((row) => row.remainingDays != null && row.remainingDays > 0).slice(0, 3);
        return live.length ? <div className="creator-promotion-list">{live.map((row) => <Link className="creator-promotion-list-card" to={`/creator/promotions/${row.budgetId}`} key={row.budgetId}>
          <div><small>{row.business.displayName}</small><h3>{row.title}</h3><p>{campaignType(row.type)} · {row.remainingDays} days left</p></div>
          <div className="creator-progress"><span>{count(row.verifiedViews)} verified views</span><span>{amount(row.viewEarnings + row.saleCommissionEarnings)} earned</span></div>
        </Link>)}</div> : <Empty title="No live Promotions yet" message="When an approved Promotion goes live, its progress will appear here." />;
      }}</Resource>
    </Section>
  </>;
}

function PromotionOpportunityCard({ row }: { row: Opportunity }) {
  return <article className="creator-opportunity-card">
    <div className="creator-opportunity-heading"><div><p className="card-eyebrow">{row.business.displayName}{row.location ? ` · ${row.location}` : row.business.region ? ` · ${row.business.region}` : ""}</p><h3>{row.title}</h3></div>{row.requestStatus && <Badge status={row.requestStatus} />}</div>
    {row.slogan?.trim() && <p className="creator-opportunity-slogan">{row.slogan}</p>}
    <p>{campaignType(row.type)}</p>
    {row.platforms?.length ? <div className="creator-platform-counts">{row.platforms.map((slot) => <span key={slot.platform}><strong>{slot.platform}</strong> {slot.approved}/{slot.capacity}</span>)}</div> : null}
    {(row.requirements || row.description) && <p className="preserve-lines">{row.requirements || row.description}</p>}
    <div className="creator-opportunity-reward"><strong>{amount(row.earnings.youEarn)}</strong><span>per {count(row.earnings.views)} verified views</span>{row.type.toLowerCase().includes("sale") && <span> · {amount(row.earnings.saleCommissionPercent)}% per eligible sale</span>}</div>
    <p className="fine-print">{row.promotionLiveDurationDays} days after you go live</p>
    <ActionLink to={`/creator/discover/${row.id}`}>{row.requestStatus ? "View Promotion" : "Review Promotion"}</ActionLink>
  </article>;
}

function UGCOpportunityCard({ row, onChanged }: { row: UgcCard; onChanged: () => void }) {
  const action = useAction();
  return <article className="creator-opportunity-card">
    <div className="creator-opportunity-heading"><div><p className="card-eyebrow">{row.business}{row.location ? ` · ${row.location}` : ""}</p><h3>{row.title}</h3></div><Badge status={row.requestStatus ?? row.status} /></div>
    {row.slogan?.trim() && <p className="creator-opportunity-slogan">{row.slogan}</p>}
    <p>{row.contentType} · {row.platformRequirements.length ? row.platformRequirements.map((x) => x.platform).join(", ") : "Deliver to Business"}</p>
    <div className="creator-opportunity-reward"><strong>{amount(row.creatorPayment)}</strong><span>Creator payment</span><span>Due {date(row.dueDateUtc)}</span></div>
    {row.platformRequirements.map((platform) => <p className="fine-print" key={platform.platform}>{platform.platform} · {platform.format}{platform.minimumAudience ? ` · ${count(platform.minimumAudience)} minimum audience` : ""}</p>)}
    {!row.requestStatus && <Button disabled={action.busy} onClick={() => void action.run(async (key) => { await post(`/creator/ugc/${row.id}/request`, undefined, key); onChanged(); })}>{action.busy ? "Requesting…" : "Request to Join"}</Button>}
    {action.error && <Notice error>{action.error}</Notice>}
  </article>;
}

export function CreatorDiscover() {
  const [searchParams] = useSearchParams();
  const [tab, setTab] = useState<"All" | "Promotions" | "UGC">(() => searchParams.get("tab") === "UGC" ? "UGC" : "All");
  const [search, setSearch] = useState("");
  const promotions = useResource<Opportunity[]>("/creator/discover");
  const ugc = useResource<UgcCard[]>("/creator/ugc");
  const filteredPromotions = useMemo(() => promotions.data?.filter((row) => `${row.business.displayName} ${row.title} ${row.slogan ?? ""}`.toLocaleLowerCase().includes(search.toLocaleLowerCase())) ?? [], [promotions.data, search]);
  const filteredUgc = useMemo(() => ugc.data?.filter((row) => `${row.business} ${row.title} ${row.slogan ?? ""}`.toLocaleLowerCase().includes(search.toLocaleLowerCase())) ?? [], [ugc.data, search]);
  return <>
    <PageHeader eyebrow="Business-created opportunities" title="Discover" description="Explore available funded Promotions and UGC opportunities from Businesses." />
    <div className="creator-discover-controls"><div className="creator-tabs" role="tablist" aria-label="Opportunity type">{(["All", "Promotions", "UGC"] as const).map((item) => <button key={item} role="tab" aria-selected={tab === item} className={tab === item ? "selected" : ""} onClick={() => setTab(item)}>{item}</button>)}</div><label className="creator-search"><Icon name="search" /><span className="sr-only">Search opportunities</span><input aria-label="Search opportunities" placeholder="Search opportunities" value={search} onChange={(e) => setSearch(e.target.value)} /></label></div>
    <Section title="Available Opportunities">
      {(tab === "All" || tab === "Promotions") && <div className="creator-opportunity-section"><h3>Promotions</h3><Resource resource={promotions}>{() => filteredPromotions.length ? <div className="creator-opportunity-grid">{filteredPromotions.map((row) => <PromotionOpportunityCard key={row.id} row={row} />)}</div> : <Empty title="No available Promotions" message="Eligible funded Promotions will appear here when they match your Creator profile." />}</Resource></div>}
      {(tab === "All" || tab === "UGC") && <div className="creator-opportunity-section"><h3>UGC</h3><Resource resource={ugc}>{() => filteredUgc.length ? <div className="creator-opportunity-grid">{filteredUgc.map((row) => <UGCOpportunityCard key={row.id} row={row} onChanged={ugc.reload} />)}</div> : <Empty title="No available UGC opportunities" message="Business-created UGC opportunities will appear here when available." />}</Resource></div>}
    </Section>
  </>;
}

type PromotionFilter = "All" | "Requested" | "Approved" | "UnderReview" | "ChangesRequested" | "ReadyToGoLive" | "Live" | "Ended" | "DeclinedRejected";
const FILTERS: { value: PromotionFilter; label: string }[] = [
  { value: "All", label: "All" }, { value: "Requested", label: "Requested" }, { value: "Approved", label: "Approved" },
  { value: "UnderReview", label: "Under Review" }, { value: "ChangesRequested", label: "Changes Requested" },
  { value: "ReadyToGoLive", label: "Ready to Go Live" }, { value: "Live", label: "Live" }, { value: "Ended", label: "Ended" },
  { value: "DeclinedRejected", label: "Declined / Rejected" },
];

function promotionFilter(row: CreatorCampaign): PromotionFilter {
  if (row.remainingDays != null && row.remainingDays > 0) return "Live";
  if (row.participationId || row.status === "Ended" || row.status === "Completed") return "Ended";
  if (row.status === "UnderReview") return "UnderReview";
  if (row.status === "ChangesRequested") return "ChangesRequested";
  if (row.status === "ReadyToGoLive") return "ReadyToGoLive";
  if (["Cancelled", "Rejected"].includes(row.status) || row.contentReviewStatus === "Rejected") return "DeclinedRejected";
  return "Approved";
}

export function CreatorPromotions() {
  const [filter, setFilter] = useState<PromotionFilter>("All");
  const promotions = useResource<CreatorCampaign[]>("/creator/campaigns");
  const requests = useResource<CreatorRequest[]>("/creator/requests");
  const assignments = useResource<UgcAssignment[]>("/creator/ugc/assignments");
  const refresh = () => { promotions.reload(); requests.reload(); assignments.reload(); };
  return <>
    <PageHeader eyebrow="Your Creator workspace" title="My Promotions" description="Track each Business decision and your next valid action." />
    <div className="creator-tabs creator-promotion-filters" role="tablist" aria-label="Filter My Promotions">{FILTERS.map((item) => <button key={item.value} role="tab" aria-selected={filter === item.value} className={filter === item.value ? "selected" : ""} onClick={() => setFilter(item.value)}>{item.label}</button>)}</div>
    <Resource resource={promotions}>{(rows) => <Resource resource={requests}>{(requestRows) => {
      const cards = rows.filter((row) => filter === "All" || promotionFilter(row) === filter);
      const requestCards = requestRows.filter((row) => row.status === "Pending" && (filter === "All" || filter === "Requested") || ["Rejected", "Declined"].includes(row.status) && (filter === "All" || filter === "DeclinedRejected"));
      return <Section title="Promotion participation">
        {cards.length || requestCards.length ? <div className="creator-promotion-list">
          {requestCards.map((row) => <article key={row.id}><div><small>{row.business}</small><h3>{row.campaign}</h3><p>{campaignType(row.type)}</p></div><div className="creator-next-action"><Badge status={row.status === "Pending" ? "Request Pending" : "Request Declined"} /><span>{row.status === "Pending" ? "Waiting for Business response." : "This request is closed. You can discover other opportunities."}</span></div></article>)}
          {cards.map((row) => <PromotionParticipationCard key={row.budgetId} row={row} onChanged={refresh} />)}
        </div> : <Empty title="No Promotion participation yet" message="Request to join a specific available Promotion. Approved requests will appear here." action={<ActionLink to="/creator/discover">Discover opportunities</ActionLink>} />}
      </Section>;
    }}</Resource>}</Resource>
    <Section title="UGC work" description="UGC assignments keep their existing Business review and payment workflow.">
      <Resource resource={assignments}>{(rows) => rows.length ? <div className="creator-opportunity-grid">{rows.map((row) => <CreatorAssignmentCard key={row.id} assignment={row} onSubmitted={refresh} />)}</div> : <Empty title="No UGC assignments yet" message="Accepted UGC work will appear here." />}</Resource>
    </Section>
  </>;
}

function PromotionParticipationCard({ row, onChanged }: { row: CreatorCampaign; onChanged: () => void }) {
  const action = useAction();
  const state = promotionFilter(row);
  const canGoLive = state === "ReadyToGoLive" && row.contentReviewStatus === "Approved" && !row.participationId;
  return <article className="creator-promotion-row">
    <div><small>{row.business.displayName}</small><h3>{row.title}</h3><p>{campaignType(row.type)}</p>
      {state === "Live" ? <p className="creator-live-days">{row.remainingDays} days left</p> : null}
      {(state === "Live" || state === "Ended") && <p>{count(row.verifiedViews)} verified views · {amount(row.viewEarnings + row.saleCommissionEarnings)} earned</p>}
      {row.contentReviewStatus && <p className="fine-print">Content · {row.contentReviewStatus.replace(/([A-Z])/g, " $1").trim()}{row.contentRevisionNumber ? ` · Revision ${row.contentRevisionNumber}` : ""}</p>}
      {row.contentFeedback && <Notice>{row.contentFeedback}</Notice>}
    </div>
    <div className="creator-next-action">
      <Badge status={state === "ReadyToGoLive" ? "Content Approved" : state === "UnderReview" ? "Content Under Review" : state === "ChangesRequested" ? "Changes Requested" : state === "Live" ? "LIVE" : state} />
      {state === "Requested" && <span>Waiting for Business response.</span>}
      {state === "Approved" && <span>Approved — review the Promotion requirements and submit your content.</span>}
      {state === "UnderReview" && <span>Waiting for Business review.</span>}
      {state === "Live" && <span>Your participation is live.</span>}
      {state === "Ended" && <span>This Creator live window has ended.</span>}
      {state === "DeclinedRejected" && <span>No further action is available for this request or content.</span>}
      {state === "Approved" || state === "ChangesRequested" ? <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>{state === "Approved" ? "Review Requirements & Add Content" : "Resubmit Content"}</ActionLink> : null}
      {canGoLive && <Button disabled={action.busy} onClick={() => void action.run(async (key) => { await post(`/creator/creator-budgets/${row.budgetId}/go-live`, {}, key); onChanged(); })}>{action.busy ? "Going live…" : "Go Live"}</Button>}
      {(state === "Live" || state === "Ended") && <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>View progress</ActionLink>}
      {action.error && <Notice error>{action.error}</Notice>}
    </div>
  </article>;
}
