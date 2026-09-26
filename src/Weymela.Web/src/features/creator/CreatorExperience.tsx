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
  UgcDetail,
  UgcRequest,
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

export function CreatorDashboard() {
  const home = useResource<CreatorHome>("/creator/home");
  const promotions = useResource<CreatorCampaign[]>("/creator/campaigns");
  return <>
    <Resource resource={home}>{(data) => <>
      <PageHeader eyebrow={`Creator · ${data.creator.displayName}`} title="Home" />
      <div className="metric-grid creator-metrics">
        <Link className="creator-dashboard-metric" to="/creator/promotions?filter=Requests"><Metric label="Pending Requests" value={count(data.requests)} icon="people" /></Link>
        <Link className="creator-dashboard-metric" to="/creator/promotions?filter=Active"><Metric label="Active Promotions" value={count(data.activeCampaigns)} icon="campaign" /></Link>
        <Link className="creator-dashboard-metric" to="/creator/earnings"><Metric label="Available Earnings" value={amount(data.earnings.availableEarnings)} icon="wallet" emphasis /></Link>
      </div>
      <section className="creator-discover-cta">
        <ActionLink to="/creator/discover">Discover Promotions</ActionLink>
      </section>
      <Section title="Recent earnings">
        {data.earnings.history.length || data.earnings.payoutHistory.length ? <div className="creator-activity-list">
          {data.earnings.history.slice(0, 3).map((item) => <article key={item.id}><span className="creator-activity-icon"><Icon name="spark" /></span><div><strong>Verified earning recorded</strong><p>{item.campaign} · {item.source}</p></div><small>{date(item.atUtc)} · {amount(item.amount)}</small></article>)}
          {data.earnings.payoutHistory.filter((item) => item.paidAtUtc).slice(0, 2).map((item) => <article key={item.id}><span className="creator-activity-icon"><Icon name="wallet" /></span><div><strong>Payout paid</strong><p>{item.status}</p></div><small>{date(item.paidAtUtc!)} · {amount(item.amount)}</small></article>)}
        </div> : <Empty title="No recent earnings" />}
      </Section>
    </>}</Resource>
    <Section title="Live Promotions" action={<ActionLink to="/creator/promotions" secondary>Promotions</ActionLink>}>
      <Resource resource={promotions}>{(rows) => {
        const live = rows.filter((row) => row.remainingDays != null && row.remainingDays > 0).slice(0, 3);
        return live.length ? <div className="creator-promotion-list">{live.map((row) => <Link className="creator-promotion-list-card" to={`/creator/promotions/${row.budgetId}`} key={row.budgetId}>
          <div><small>{row.business.displayName}</small><h3>{row.title}</h3><p>{campaignType(row.type)} · {row.remainingDays} days left</p></div>
          <div className="creator-progress"><span>{count(row.verifiedViews)} verified views</span><span>{amount(row.viewEarnings + row.saleCommissionEarnings)} earned</span></div>
        </Link>)}</div> : <Empty title="No live Promotions" />;
      }}</Resource>
    </Section>
  </>;
}

function PromotionOpportunityCard({ row }: { row: Opportunity }) {
  return <article className="creator-opportunity-card">
    <div className="creator-opportunity-heading"><div><p className="card-eyebrow">{row.business.displayName}</p><h3>{row.title}</h3></div>{row.requestStatus && <Badge status={row.requestStatus} />}</div>
    {row.slogan?.trim() && <p className="creator-opportunity-slogan">{row.slogan}</p>}
    {row.platforms?.length ? <div className="creator-platform-counts" aria-label="Social platform availability">{row.platforms.map((slot) => <span key={slot.platform} className={slot.available <= 0 ? "platform-full" : ""}><strong>{slot.platform}</strong> {slot.approved}/{slot.capacity}{slot.available <= 0 ? " · Full" : ""}</span>)}</div> : null}
    <p className="creator-opportunity-meta">{campaignType(row.type)} · {amount(row.earnings.youEarn)} per {count(row.earnings.views)} views{row.creatorCapacity ? ` · Creators ${row.approvedCreators ?? 0}/${row.creatorCapacity}` : ""}</p>
    {row.type.toLowerCase().includes("sale") && <p className="creator-opportunity-meta">{amount(row.earnings.saleCommissionPercent)}% per eligible sale</p>}
    {(row.location || row.business.region) && <p className="creator-opportunity-location"><Icon name="location" size={16} />{row.location || row.business.region}</p>}
    <ActionLink to={`/creator/discover/${row.id}`}>{row.requestStatus ? "View Promotion" : "Request to Join"}</ActionLink>
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
  const [tab, setTab] = useState<"Promotions" | "UGC">(() => searchParams.get("tab") === "UGC" ? "UGC" : "Promotions");
  const [search, setSearch] = useState("");
  const promotions = useResource<Opportunity[]>("/creator/discover");
  const ugc = useResource<UgcCard[]>("/creator/ugc");
  const filteredPromotions = useMemo(() => promotions.data?.filter((row) => `${row.business.displayName} ${row.title} ${row.slogan ?? ""}`.toLocaleLowerCase().includes(search.toLocaleLowerCase())) ?? [], [promotions.data, search]);
  const filteredUgc = useMemo(() => ugc.data?.filter((row) => `${row.business} ${row.title} ${row.slogan ?? ""}`.toLocaleLowerCase().includes(search.toLocaleLowerCase())) ?? [], [ugc.data, search]);
  return <>
    <PageHeader title="Discover" />
    <div className="creator-discover-controls"><div className="creator-tabs" role="tablist" aria-label="Opportunity type">{(["Promotions", "UGC"] as const).map((item) => <button key={item} role="tab" aria-selected={tab === item} className={tab === item ? "selected" : ""} onClick={() => setTab(item)}>{item}</button>)}</div><label className="creator-search"><Icon name="search" /><span className="sr-only">Search opportunities</span><input aria-label="Search opportunities" placeholder="Search opportunities" value={search} onChange={(e) => setSearch(e.target.value)} /></label></div>
    {tab === "Promotions" ? <Resource resource={promotions}>{() => filteredPromotions.length ? <div className="creator-opportunity-grid">{filteredPromotions.map((row) => <PromotionOpportunityCard key={row.id} row={row} />)}</div> : <Empty title="No available Promotions" />}</Resource>
      : <Resource resource={ugc}>{() => filteredUgc.length ? <div className="creator-opportunity-grid">{filteredUgc.map((row) => <UGCOpportunityCard key={row.id} row={row} onChanged={ugc.reload} />)}</div> : <Empty title="No available UGC opportunities" />}</Resource>}
  </>;
}

type PromotionGroup = "Active" | "Requests" | "History";
const GROUPS: PromotionGroup[] = ["Active", "Requests", "History"];
const terminalPromotionStatuses = new Set(["Ended", "Completed", "Cancelled", "Rejected"]);
const terminalRequestStatuses = new Set(["Rejected", "Declined", "Withdrawn"]);

function statusLabel(status: string) {
  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function promotionGroup(row: CreatorCampaign): PromotionGroup {
  return terminalPromotionStatuses.has(row.status) || row.contentReviewStatus === "Rejected"
    || (row.participationId !== null && row.remainingDays == null) ? "History" : "Active";
}

function promotionStatus(row: CreatorCampaign) {
  if (row.participationId && row.remainingDays == null && !terminalPromotionStatuses.has(row.status)) return "Ended";
  return row.status === "Active" ? "Live" : statusLabel(row.status);
}

type CreatorWorkItem =
  | { kind: "promotion"; row: CreatorCampaign; group: PromotionGroup; key: string }
  | { kind: "promotionRequest"; row: CreatorRequest; group: PromotionGroup; key: string }
  | { kind: "ugcAssignment"; row: UgcAssignment; group: PromotionGroup; key: string }
  | { kind: "ugcRequest"; row: UgcRequest; group: PromotionGroup; key: string };

function workItems(campaigns: CreatorCampaign[], requests: CreatorRequest[],
  assignments: UgcAssignment[], ugcRequests: UgcRequest[]): CreatorWorkItem[] {
  const campaignIds = new Set(campaigns.map((row) => row.id));
  const assignedUgcIds = new Set(assignments.map((row) => row.opportunityId));
  return [
    ...campaigns.map((row): CreatorWorkItem => ({ kind: "promotion", row, group: promotionGroup(row), key: `promotion-${row.budgetId}` })),
    ...requests.filter((row) => !campaignIds.has(row.campaignId)).map((row): CreatorWorkItem => ({
      kind: "promotionRequest", row, key: `promotion-request-${row.id}`,
      group: row.status === "Pending" ? "Requests" : terminalRequestStatuses.has(row.status) ? "History" : "Active",
    })),
    ...assignments.map((row): CreatorWorkItem => ({
      kind: "ugcAssignment", row, key: `ugc-assignment-${row.id}`,
      group: row.status === "Approved" || row.status === "Rejected" ? "History" : "Active",
    })),
    ...ugcRequests.filter((row) => !assignedUgcIds.has(row.opportunityId)).map((row): CreatorWorkItem => ({
      kind: "ugcRequest", row, key: `ugc-request-${row.id}`,
      group: row.status === "Pending" ? "Requests" : terminalRequestStatuses.has(row.status) ? "History" : "Active",
    })),
  ];
}

export function CreatorPromotions() {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedGroup = searchParams.get("filter");
  const filter: PromotionGroup = requestedGroup === "Requests" || requestedGroup === "Requested" ? "Requests"
    : requestedGroup === "History" || requestedGroup === "Ended" || requestedGroup === "DeclinedRejected" ? "History" : "Active";
  const promotions = useResource<CreatorCampaign[]>("/creator/campaigns");
  const requests = useResource<CreatorRequest[]>("/creator/requests");
  const assignments = useResource<UgcAssignment[]>("/creator/ugc/assignments");
  const ugcRequests = useResource<UgcRequest[]>("/creator/ugc/requests");
  const refresh = () => { promotions.reload(); requests.reload(); assignments.reload(); ugcRequests.reload(); };
  return <>
    <PageHeader title="My Promotions" />
    <div className="creator-tabs creator-promotion-filters" role="tablist" aria-label="Filter Promotions">{GROUPS.map((item) => <button key={item} role="tab" aria-selected={filter === item} className={filter === item ? "selected" : ""} onClick={() => setSearchParams(item === "Active" ? {} : { filter: item })}>{item}</button>)}</div>
    <Resource resource={promotions}>{(campaignRows) => <Resource resource={requests}>{(requestRows) =>
      <Resource resource={assignments}>{(assignmentRows) => <Resource resource={ugcRequests}>{(ugcRequestRows) => {
        const visible = workItems(campaignRows, requestRows, assignmentRows, ugcRequestRows).filter((item) => item.group === filter);
        return visible.length ? <div className="creator-promotion-list creator-work-list">
          {visible.map((item) => item.kind === "promotion" ? <PromotionParticipationCard key={item.key} row={item.row} onChanged={refresh} />
            : item.kind === "promotionRequest" ? <PromotionRequestCard key={item.key} row={item.row} />
              : <UgcWorkCard key={item.key} item={item} onSubmitted={refresh} />)}
        </div> : <div className="creator-work-empty"><h2>{filter === "Requests" ? "No pending requests" : filter === "History" ? "No past work" : "No active promotions"}</h2>
          <ActionLink to="/creator/discover">Discover Promotions</ActionLink></div>;
      }}</Resource>}</Resource>
    }</Resource>}</Resource>
  </>;
}

function PromotionRequestCard({ row }: { row: CreatorRequest }) {
  return <article className="creator-promotion-row creator-work-row">
    <div><small>{row.business}</small><h3>{row.campaign}</h3><p>{campaignType(row.type)}</p></div>
    <div className="creator-next-action"><Badge status={statusLabel(row.status)} />
      {row.status === "Pending" && <span>Waiting for Business decision</span>}
      {row.status === "Approved" && <span>Waiting for your Promotion workspace</span>}
    </div>
  </article>;
}

function UgcWorkCard({ item, onSubmitted }: { item: Extract<CreatorWorkItem, { kind: "ugcAssignment" | "ugcRequest" }>; onSubmitted: () => void }) {
  const assignment = item.kind === "ugcAssignment" ? item.row : null;
  const request = item.kind === "ugcRequest" ? item.row : null;
  const opportunityId = item.row.opportunityId;
  const detail = useResource<UgcDetail>(`/creator/ugc/${opportunityId}`);
  const action = useAction();
  const [url, setUrl] = useState(assignment?.submissionUrl ?? "");
  const status = item.row.status;
  const opportunity = detail.data?.opportunity;
  return <article className="creator-promotion-row creator-work-row">
    <div><small>{assignment?.business ?? opportunity?.business ?? "UGC"}</small>
      <h3>{assignment?.opportunity ?? opportunity?.title ?? "UGC opportunity"}</h3>
      <p>{opportunity?.customerOfferEnabled ? "UGC + Discount" : "UGC"}</p>
      {assignment && <p>Fixed Creator payment {amount(assignment.creatorPayment)} · Due {date(assignment.dueDateUtc)}</p>}
      {assignment?.instructions && <p>{assignment.instructions}</p>}
      {assignment && <p className="fine-print">{assignment.platformRequirements.length
        ? `Post on ${assignment.platformRequirements.map((requirement) => requirement.platform).join(", ")}`
        : "Deliver content to the Business"}</p>}
      {assignment?.feedback && <Notice>{assignment.feedback}</Notice>}
      {request?.rejectionReason && <Notice>{request.rejectionReason}</Notice>}
      {detail.error && <div className="creator-work-detail-error"><span>UGC details unavailable.</span><Button variant="secondary" onClick={detail.reload}>Retry</Button></div>}
    </div>
    <div className="creator-next-action"><Badge status={statusLabel(status)} />
      {status === "Pending" && <span>Waiting for Business decision</span>}
      {status === "Submitted" && <span>Waiting for Business review</span>}
      {request?.status === "Approved" && <span>Waiting for your UGC assignment</span>}
      {assignment?.revisionAcceptanceRequired && <span>Review updated requirements with the Business before submitting</span>}
      {assignment && !assignment.revisionAcceptanceRequired && (status === "InProgress" || status === "ChangesRequested") &&
        <form className="creator-work-submit" onSubmit={(event) => { event.preventDefault(); if (!url.trim()) return; void action.run(async (key) => {
          await post(`/creator/ugc/assignments/${assignment.id}/submit`, { submissionUrl: url.trim() }, key); onSubmitted();
        }); }}>
          <label>{assignment.platformRequirements.length ? "Social post link" : "Content delivery link"}
            <input type="url" value={url} onChange={(event) => setUrl(event.target.value)} required /></label>
          <Button type="submit" disabled={action.busy || !url.trim()}>{action.busy ? "Submitting…" : status === "ChangesRequested" ? "Update Content" : "Submit Content"}</Button>
        </form>}
      {action.error && <Notice error>{action.error}</Notice>}
    </div>
  </article>;
}

function PromotionParticipationCard({ row, onChanged }: { row: CreatorCampaign; onChanged: () => void }) {
  const action = useAction();
  const state = row.status;
  const canGoLive = state === "ReadyToGoLive" && row.contentReviewStatus === "Approved" && !row.participationId;
  return <article className="creator-promotion-row creator-work-row">
    <div><small>{row.business.displayName}</small><h3>{row.title}</h3><p>{campaignType(row.type)}</p>
      {row.participationId && row.remainingDays != null ? <p className="creator-live-days">{row.remainingDays} days left</p> : null}
      {row.participationId && <p>{count(row.verifiedViews)} verified views · {amount(row.viewEarnings + row.saleCommissionEarnings)} earned</p>}
      {row.contentReviewStatus && <p className="fine-print">Content · {row.contentReviewStatus.replace(/([A-Z])/g, " $1").trim()}{row.contentRevisionNumber ? ` · Revision ${row.contentRevisionNumber}` : ""}</p>}
      {row.contentFeedback && <Notice>{row.contentFeedback}</Notice>}
    </div>
    <div className="creator-next-action">
      <Badge status={promotionStatus(row)} />
      {state === "UnderReview" && <span>Waiting for Business review</span>}
      {state === "Paused" && <span>Your Promotion is paused</span>}
      {state === "FundingRequired" && <span>Waiting for Business funding</span>}
      {state === "Approved" || state === "ChangesRequested" ? <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>{state === "Approved" ? "Add Content" : "Update Content"}</ActionLink> : null}
      {canGoLive && <Button disabled={action.busy} onClick={() => void action.run(async (key) => { await post(`/creator/creator-budgets/${row.budgetId}/go-live`, {}, key); onChanged(); })}>{action.busy ? "Going live…" : "Go Live"}</Button>}
      {row.participationId && <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>View progress</ActionLink>}
      {action.error && <Notice error>{action.error}</Notice>}
    </div>
  </article>;
}
