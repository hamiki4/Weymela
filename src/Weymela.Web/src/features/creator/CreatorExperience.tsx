import { useMemo, useState, type ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { post, postForm, useAction, useResource } from "../../api/client";
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
  Currency,
  Empty,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, count, date, isViewAndSale, isViewOnly, promotionStatusLabel, promotionTypeLabel } from "../../ui/format";
import { Icon } from "../../ui/Icon";
import { PlatformRequirements } from "./CreatorPlatformIcon";
import { UgcJoinControls } from "./UgcJoinControls";

function HomePreviewResource<T>({ resource, children }: {
  resource: { data: T | null; loading: boolean; error: Error | null };
  children: (data: T) => ReactNode;
}) {
  if (resource.data === null) return resource.error
    ? <Empty title="Opportunities are temporarily unavailable." />
    : <p className="fine-print" aria-live="polite">Loading opportunities…</p>;
  return <>{children(resource.data)}</>;
}

export function CreatorDashboard() {
  const home = useResource<CreatorHome>("/creator/home");
  const promotions = useResource<CreatorCampaign[]>("/creator/campaigns");
  const opportunities = useResource<Opportunity[]>("/creator/discover");
  const ugc = useResource<UgcCard[]>("/creator/ugc");
  const activePromotion = promotions.data?.find(
    (row) => row.remainingDays != null && row.remainingDays > 0,
  );
  const featuredPromotion = opportunities.data?.[0];
  const featuredUgc = !featuredPromotion ? ugc.data?.[0] : undefined;

  return <>
    <Resource resource={home}>{(data) => <div className="creator-home">
      <PageHeader
        title="Home"
        description={data.creator.displayName}
        action={<ActionLink to="/creator/discover" icon="search">Discover Promotions</ActionLink>}
      />

      <Section title="Your work" className="creator-home-section">
        <div className="creator-home-work-list">
          <Link
            className="creator-home-summary-row"
            to="/creator/promotions?filter=Pending"
            aria-label={`Pending Requests, ${count(data.requests)}, Waiting for Business`}
          >
            <span className="creator-home-summary-icon"><Icon name="people" size={18} /></span>
            <span className="creator-home-summary-copy">
              <strong>Pending Requests</strong>
              <small>Waiting for Business</small>
            </span>
            <strong className="creator-home-summary-count">{count(data.requests)}</strong>
            <Icon name="arrow" size={17} />
          </Link>
          <Link
            className="creator-home-summary-row"
            to="/creator/promotions?filter=Active"
            aria-label={`Live Promotions, ${count(data.activeCampaigns)}, Continue your Promotion`}
          >
            <span className="creator-home-summary-icon"><Icon name="campaign" size={18} /></span>
            <span className="creator-home-summary-copy">
              <strong>Live Promotions</strong>
              <small>Continue your Promotion</small>
            </span>
            <strong className="creator-home-summary-count">{count(data.activeCampaigns)}</strong>
            <Icon name="arrow" size={17} />
          </Link>
        </div>
      </Section>

      {activePromotion && <Link
        className="creator-home-active-preview"
        to={`/creator/promotions/${activePromotion.budgetId}`}
        aria-label={`View Promotion ${activePromotion.title}`}
      >
        <span className="creator-home-active-copy">
          <small>Continue working</small>
          <strong>{activePromotion.title}</strong>
          <span>{activePromotion.business.displayName}</span>
        </span>
        <span className="creator-home-active-meta">
          <Badge status="Active" label="Active" />
          <small>{activePromotion.remainingDays} days left</small>
        </span>
        <span className="creator-home-active-action">View Promotion</span>
      </Link>}

      <Section
        title="Promotions for you"
        className="creator-home-section creator-home-discover-preview"
        action={<Link className="creator-home-section-link" to="/creator/discover">See all</Link>}
      >
        {featuredPromotion || featuredUgc ? (
          <CreatorHomeOpportunityPreview promotion={featuredPromotion} ugc={featuredUgc} />
        ) : (opportunities.data === null && !opportunities.error) || (ugc.data === null && !ugc.error) ? (
          <p className="creator-home-inline-state" aria-live="polite">Loading promotions…</p>
        ) : opportunities.error || ugc.error ? (
          <p className="creator-home-inline-state">Promotions are temporarily unavailable.</p>
        ) : (
          <p className="creator-home-inline-state">No new promotions right now.</p>
        )}
      </Section>

      <Section title="Earnings" className="creator-home-section">
        <Link
          className="creator-home-earnings-row"
          to="/creator/earnings"
          aria-label={`Available Earnings, ${amount(data.earnings.availableEarnings)} ETB`}
        >
          <span>
            <small>Available Earnings</small>
            <strong>{amount(data.earnings.availableEarnings)} ETB</strong>
          </span>
          <Icon name="arrow" size={17} />
        </Link>
      </Section>

      <Link className="creator-home-route-row" to="/creator/promotions">
        <span>My Promotions</span>
        <Icon name="arrow" size={17} />
      </Link>
    </div>}</Resource>
  </>;
}

function CreatorHomeOpportunityPreview({ promotion, ugc }: { promotion?: Opportunity; ugc?: UgcCard }) {
  const href = promotion ? `/creator/discover/${promotion.id}` : `/creator/ugc/${ugc?.id}`;
  const business = promotion?.business.displayName ?? ugc?.business ?? "Promotion";
  const title = promotion?.title ?? ugc?.title ?? "Promotion";
  const type = promotion ? promotionTypeLabel(promotion.type) : ugc?.customerOfferEnabled ? "UGC + Sales" : "UGC";

  return <article className="creator-home-opportunity-preview">
    <div className="creator-home-opportunity-copy">
      <p className="card-eyebrow">{business}</p>
      <span className="creator-opportunity-type">{type}</span>
      <h3>{title}</h3>
      {promotion ? <p className="creator-opportunity-payment">
        <strong>{amount(promotion.earnings.youEarn)} ETB</strong>
        <span>per {count(promotion.earnings.views)} verified views</span>
      </p> : <p className="creator-opportunity-payment">
        <strong>Creator payment {amount(ugc?.creatorPayment ?? 0)} ETB</strong>
        <span>Due {date(ugc?.dueDateUtc ?? "")}</span>
      </p>}
    </div>
    <ActionLink to={href} secondary>View details</ActionLink>
  </article>;
}

function creatorStatusTone(status: string) {
  const normalized = status.toLowerCase();
  if (normalized === "live") return "live";
  if (normalized === "completed" || normalized === "ended" || normalized === "cancelled") return "completed";
  if (normalized === "rejected" || normalized === "declined" || normalized === "changesrequested") return "attention";
  if (normalized === "inprogress" || normalized === "approved" || normalized === "readytogolive") return "progress";
  return "review";
}

function CreatorStatus({ status, label }: { status: string; label?: string }) {
  return <span className={`creator-status creator-status-${creatorStatusTone(status)}`} data-status={status}>
    {label ?? promotionStatusLabel(status)}
  </span>;
}

function PromotionOpportunityCard({ row }: { row: Opportunity }) {
  const full = !!row.platforms?.length && row.platforms.every((slot) => slot.available <= 0);
  const typeLabel = promotionTypeLabel(row.type);
  const platforms = row.platforms?.map((slot) => slot.platform).join(" · ");

  return <article className="creator-opportunity-card">
    <div className="creator-opportunity-heading">
      <div>
        <p className="card-eyebrow">{row.business.displayName}</p>
        <span className="creator-opportunity-type">{typeLabel}</span>
        <h3>{row.title}</h3>
      </div>
      {row.requestStatus && <CreatorStatus status={row.requestStatus} />}
    </div>

    <p className="creator-opportunity-payment">
      <strong>{amount(row.earnings.youEarn)} ETB</strong>
      <span>per {count(row.earnings.views)} verified views</span>
    </p>

    {!isViewOnly(row.type) &&
      <p className="creator-opportunity-payment creator-opportunity-commission">
        + {amount(row.earnings.saleCommissionPercent)}% from verified sales
      </p>
    }

    {platforms &&
      <p className="creator-opportunity-meta">{platforms}</p>
    }

    {(row.location || row.business.region) &&
      <p className="creator-opportunity-location">
        <Icon name="location" size={16} />
        {row.location || row.business.region}
      </p>
    }

    <p className="creator-opportunity-meta">Ends {date(row.endUtc)}</p>

    {full && !row.requestStatus ? (
      <span className="creator-opportunity-unavailable">No spots available</span>
    ) : (
      <ActionLink to={`/creator/discover/${row.id}`}>
        {row.requestStatus ? "View Promotion" : "Request to Join"}
      </ActionLink>
    )}
  </article>;
}

function UGCOpportunityCard({ row, onChanged }: { row: UgcCard; onChanged: () => void }) {
  const typeLabel = row.customerOfferEnabled ? "UGC + Sales" : "UGC";
  const platforms = (row.platformRequirements ?? [])
    .map((requirement) => requirement.platform)
    .join(" · ");

  return <article className="creator-opportunity-card">
    <div className="creator-opportunity-heading">
      <div>
        <p className="card-eyebrow">{row.business}</p>
        <span className="creator-opportunity-type">{typeLabel}</span>
        <h3>{row.title}</h3>
      </div>
      {row.requestStatus && <CreatorStatus status={row.requestStatus} />}
    </div>

    <p className="creator-opportunity-payment">
      <strong>Creator payment {amount(row.creatorPayment)} ETB</strong>
    </p>

    {row.customerOfferEnabled && row.customerDiscountPercent != null &&
      <p className="creator-opportunity-meta">
        Customer gets {amount(row.customerDiscountPercent)}% off
      </p>
    }

    <p className="creator-opportunity-meta">
      Due {date(row.dueDateUtc)}
    </p>

    {row.productProvided && !row.creatorMustPurchase &&
      <p className="creator-opportunity-meta">Product provided by Business</p>}
    {row.creatorMustPurchase && !row.productProvided &&
      <p className="creator-opportunity-meta">Creator purchases product</p>}

    {platforms &&
      <p className="creator-opportunity-meta">{platforms}</p>
    }

    {row.location &&
      <p className="creator-opportunity-location">
        <Icon name="location" size={16} />
        {row.location}
      </p>
    }

    {row.requestStatus ? (
      <ActionLink to={`/creator/ugc/${row.id}`}>View Promotion</ActionLink>
    ) : (
      <UgcJoinControls item={row} onChanged={onChanged} />
    )}
  </article>;
}

export function CreatorDiscover() {
  const [search, setSearch] = useState("");
  const promotions = useResource<Opportunity[]>("/creator/discover");
  const ugc = useResource<UgcCard[]>("/creator/ugc");

  const filteredPromotions = useMemo(
    () =>
      promotions.data?.filter((row) =>
        `${row.business.displayName} ${row.title} ${row.slogan ?? ""}`
          .toLocaleLowerCase()
          .includes(search.toLocaleLowerCase()),
      ) ?? [],
    [promotions.data, search],
  );

  const filteredUgc = useMemo(
    () =>
      ugc.data?.filter((row) =>
        `${row.business} ${row.title} ${row.slogan ?? ""}`
          .toLocaleLowerCase()
          .includes(search.toLocaleLowerCase()),
      ) ?? [],
    [ugc.data, search],
  );

  return (
    <>
      <PageHeader title="Discover" compact />

      <div className="creator-discover-controls">
        <label className="creator-search">
          <Icon name="search" />
          <span className="sr-only">Search opportunities</span>
          <input
            aria-label="Search opportunities"
            placeholder="Search opportunities"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
      </div>

      <Section title="Opportunities">
        <HomePreviewResource resource={promotions}>
          {() => (
            <HomePreviewResource resource={ugc}>
              {() => {
                const total =
                  filteredPromotions.length + filteredUgc.length;

                return total ? (
                  <div className="creator-opportunity-grid">
                    {filteredPromotions.map((row) => (
                      <PromotionOpportunityCard key={`promotion-${row.id}`} row={row} />
                    ))}
                    {filteredUgc.map((row) => (
                      <UGCOpportunityCard
                        key={`ugc-${row.id}`}
                        row={row}
                        onChanged={ugc.reload}
                      />
                    ))}
                  </div>
                ) : (
                  <Empty title="No available opportunities" />
                );
              }}
            </HomePreviewResource>
          )}
        </HomePreviewResource>
      </Section>
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
  return <>
    <Link className="back-link" to="/creator/discover">
      ← Discover
    </Link>
    <Resource resource={resource}>
      {(p) => (
        <>
          <PageHeader
            eyebrow={p.business.displayName}
            title={p.title}
            description={promotionTypeLabel(p.type)}
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
                  <dd>{p.promotionLiveDurationDays} days after you go live</dd>
                </div>
                {p.region && <div><dt>Region</dt><dd>{p.region}</dd></div>}
              </dl>
              {!!p.platforms?.length && <PlatformRequirements slots={p.platforms} />}
            </Section>
            <Section title="How You Earn" action={<Currency />}>
              <div className="price-feature">
                <strong>{amount(p.earnings.youEarn)}</strong>
                <span>per {count(p.earnings.views)} verified views</span>
              </div>
              {isViewAndSale(p.type) && <p>Plus {amount(p.earnings.saleCommissionPercent)}% per verified sale.</p>}
            </Section>
          </div>
          <Section title={p.requestStatus ? "Your request" : "Request to Join"}>
            {p.requestStatus ? (
              <Notice>Request {p.requestStatus.toLowerCase()}. {p.requestStatus === "Pending" && "Waiting for approval."} <Link to="/creator/promotions">My Promotions</Link></Notice>
            ) : (
              <form
                className="contained-form"
                onSubmit={(event) => {
                  event.preventDefault();
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
                        <strong>{slot.platform}</strong><small>{slot.available > 0 ? "Available" : "Full"}</small>
                        {profiles.map((profile) => <label key={profile.id}><input type="radio" name="creator-platform" value={profile.id} checked={socialProfileId === profile.id} onChange={() => setSocialProfileId(profile.id)} disabled={slot.available <= 0} />{profile.profileUrl}</label>)}
                        {profiles.length === 0 && <small>{slot.available > 0 ? "Connect a social profile to request" : "Unavailable"}</small>}
                      </div>;
                    })}
                  </div>}
                  <Field label="Short message"><textarea value={message} onChange={(event) => setMessage(event.target.value)} maxLength={1000} rows={3} /></Field>
                  <Field label="Content concept (optional)"><textarea value={concept} onChange={(event) => setConcept(event.target.value)} maxLength={2000} rows={3} /></Field>
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
  </>;
}

export function CreatorUgcOpportunity() {
  const { id } = useParams();
  const resource = useResource<UgcDetail>(`/creator/ugc/${id}`);
  return <>
    <Link className="back-link" to="/creator/discover">← Discover</Link>
    <Resource resource={resource}>{(detail) => {
      const opportunity = detail.opportunity;
      const arrangement = opportunity.productProvided && !opportunity.creatorMustPurchase
        ? "Product provided by Business"
        : opportunity.creatorMustPurchase && !opportunity.productProvided
          ? "Creator purchases product"
          : "Product arrangement unavailable";
      const requirements = opportunity.platformRequirements ?? [];
      return <>
        <PageHeader eyebrow={opportunity.business} title={opportunity.title} description={opportunity.customerOfferEnabled ? "UGC + Sales" : "UGC"}
          action={opportunity.requestStatus && <Badge status={opportunity.requestStatus} />} />
        <div className="content-grid">
          <Section title="Promotion details">
            {opportunity.slogan?.trim() && <p className="creator-opportunity-slogan">{opportunity.slogan}</p>}
            <dl className="detail-list">
              <div><dt>Content</dt><dd>{opportunity.contentType}</dd></div>
              <div><dt>Creator payment</dt><dd>{amount(opportunity.creatorPayment)} ETB</dd></div>
              <div><dt>Content due</dt><dd>{date(opportunity.dueDateUtc)}</dd></div>
              {opportunity.applicationClosesAtUtc && <div><dt>Application closes</dt><dd>{date(opportunity.applicationClosesAtUtc)}</dd></div>}
              {opportunity.location && <div><dt>Location</dt><dd>{opportunity.location}</dd></div>}
              <div><dt>Product arrangement</dt><dd>{arrangement}</dd></div>
            </dl>
            {detail.instructions?.trim() && <div className="pricing-note"><strong>Instructions</strong><p className="preserve-lines">{detail.instructions}</p></div>}
            {requirements.length ? <PlatformRequirements slots={requirements.map((requirement) => ({
              platform: requirement.platform,
              minimumAudience: requirement.minimumAudience,
              capacity: opportunity.platformCapacities?.find((slot) => slot.platform === requirement.platform)?.capacity ?? 1,
            }))} /> : null}
          </Section>
          <Section title={opportunity.requestStatus ? "Your request" : "Request to Join"}>
            {opportunity.requestStatus
              ? <Notice>Request {opportunity.requestStatus.toLowerCase()}. {opportunity.requestStatus === "Pending" && "Waiting for approval."} <Link to="/creator/promotions">My Promotions</Link></Notice>
              : <UgcJoinControls item={opportunity} onChanged={resource.reload} />}
          </Section>
        </div>
      </>;
    }}</Resource>
  </>;
}

type PromotionGroup = "Pending" | "Active" | "Completed";
const GROUPS: PromotionGroup[] = ["Pending", "Active", "Completed"];
const terminalPromotionStatuses = new Set(["Ended", "Completed", "Cancelled", "Rejected"]);
const terminalRequestStatuses = new Set(["Rejected", "Declined", "Withdrawn"]);

function promotionGroup(row: CreatorCampaign): PromotionGroup {
  return terminalPromotionStatuses.has(row.status) || row.contentReviewStatus === "Rejected"
    || (row.participationId !== null && row.remainingDays == null) ? "Completed" : "Active";
}

function promotionStatus(row: CreatorCampaign) {
  if (row.participationId && row.remainingDays == null && !terminalPromotionStatuses.has(row.status)) return "Completed";
  return row.wentLiveAtUtc ? "Live" : promotionStatusLabel(row.status);
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
      group: row.status === "Pending" ? "Pending" : terminalRequestStatuses.has(row.status) ? "Completed" : "Active",
    })),
    ...assignments.map((row): CreatorWorkItem => ({
      kind: "ugcAssignment", row, key: `ugc-assignment-${row.id}`,
      group: row.status === "Rejected" || (row.status === "Approved"
        && (!row.selectedPlatform || row.publication?.wentLiveAtUtc)) ? "Completed" : "Active",
    })),
    ...ugcRequests.filter((row) => !assignedUgcIds.has(row.opportunityId)).map((row): CreatorWorkItem => ({
      kind: "ugcRequest", row, key: `ugc-request-${row.id}`,
      group: row.status === "Pending" ? "Pending" : terminalRequestStatuses.has(row.status) ? "Completed" : "Active",
    })),
  ];
}

export function CreatorPromotions() {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedGroup = searchParams.get("filter");
  const filter: PromotionGroup =
    requestedGroup === "Pending" || requestedGroup === "Requests" || requestedGroup === "Requested"
      ? "Pending"
      : requestedGroup === "Completed" || requestedGroup === "History" || requestedGroup === "Ended" || requestedGroup === "DeclinedRejected"
        ? "Completed"
        : "Active";
  const promotions = useResource<CreatorCampaign[]>("/creator/campaigns");
  const requests = useResource<CreatorRequest[]>("/creator/requests");
  const assignments = useResource<UgcAssignment[]>("/creator/ugc/assignments");
  const ugcRequests = useResource<UgcRequest[]>("/creator/ugc/requests");
  const refresh = () => { promotions.reload(); requests.reload(); assignments.reload(); ugcRequests.reload(); };
  return <>
    <PageHeader title="My Promotions" compact />
    <div className="creator-tabs creator-promotion-filters" role="tablist" aria-label="Filter Promotions">{GROUPS.map((item) => <button key={item} role="tab" aria-selected={filter === item} className={filter === item ? "selected" : ""} onClick={() => setSearchParams({ filter: item })}>{item}</button>)}</div>
    <Resource resource={promotions}>{(campaignRows) => <Resource resource={requests}>{(requestRows) =>
      <Resource resource={assignments}>{(assignmentRows) => <Resource resource={ugcRequests}>{(ugcRequestRows) => {
        const visible = workItems(campaignRows, requestRows, assignmentRows, ugcRequestRows).filter((item) => item.group === filter);
        return visible.length ? <div className="creator-promotion-list creator-work-list">
          {visible.map((item) => item.kind === "promotion" ? <PromotionParticipationCard key={item.key} row={item.row} onChanged={refresh} />
            : item.kind === "promotionRequest" ? <PromotionRequestCard key={item.key} row={item.row} />
              : <UgcWorkCard key={item.key} item={item} onSubmitted={refresh} />)}
        </div> : <div className="creator-work-empty"><h2>{filter === "Pending" ? "No pending promotions" : filter === "Completed" ? "No completed promotions" : "No active promotions"}</h2>
          <ActionLink to="/creator/discover">Discover Promotions</ActionLink></div>;
      }}</Resource>}</Resource>
    }</Resource>}</Resource>
  </>;
}

function PromotionRequestCard({ row }: { row: CreatorRequest }) {
  return <article className="creator-promotion-row creator-work-row">
    <div className="creator-work-main">
      <div className="creator-work-top">
        <div className="creator-work-copy"><small>{row.business}</small><h3>{row.campaign}</h3></div>
        <CreatorStatus status={row.status} />
      </div>
      <p className="creator-work-type">{promotionTypeLabel(row.type)}</p>
      <p className="creator-work-summary">Request sent {date(row.appliedAtUtc)}</p>
    </div>
    <div className="creator-next-action">
      {row.status === "Pending" && <span>Waiting for Business decision</span>}
      {row.status === "Approved" && <span>Waiting for your Promotion workspace</span>}
      <ActionLink to={`/creator/discover/${row.campaignId}`} secondary>View details</ActionLink>
    </div>
  </article>;
}

function UgcWorkCard({ item, onSubmitted }: { item: Extract<CreatorWorkItem, { kind: "ugcAssignment" | "ugcRequest" }>; onSubmitted: () => void }) {
  const assignment = item.kind === "ugcAssignment" ? item.row : null;
  const request = item.kind === "ugcRequest" ? item.row : null;
  const opportunityId = item.row.opportunityId;
  const detail = useResource<UgcDetail>(`/creator/ugc/${opportunityId}`);
  const action = useAction();
  const [media, setMedia] = useState<File | null>(null);
  const [externalContentId, setExternalContentId] = useState("");
  const status = item.row.status;
  const opportunity = detail.data?.opportunity;
  return <article className="creator-promotion-row creator-work-row">
    <div className="creator-work-main">
      <div className="creator-work-top">
        <div className="creator-work-copy"><small>{assignment?.business ?? opportunity?.business ?? "UGC"}</small>
          <h3>{assignment?.opportunity ?? opportunity?.title ?? "Promotion"}</h3>
        </div>
        <CreatorStatus status={status} />
      </div>
      <p className="creator-work-type">{opportunity?.customerOfferEnabled ? "UGC + Sales" : "UGC"}</p>
      {assignment && <p className="creator-work-summary">Payment {amount(assignment.creatorPayment)} ETB · Due {date(assignment.dueDateUtc)}</p>}
      {request && <p className="creator-work-summary">Request {status.toLowerCase()}</p>}
      {detail.error && <div className="creator-work-detail-error"><span>Promotion details unavailable.</span><Button variant="secondary" onClick={detail.reload}>Retry</Button></div>}
      {assignment ? <details className="creator-work-disclosure">
        <summary>View details</summary>
        <div className="creator-work-disclosure-body">
          <p className="ugc-arrangement"><strong>Product arrangement:</strong> {assignment.productProvided ? "Product provided by Business" : assignment.creatorMustPurchase ? "Creator purchases product" : "Not selected"}</p>
          {assignment.instructions && <p className="preserve-lines">{assignment.instructions}</p>}
          <p className="fine-print">{assignment.platformRequirements.length
            ? `Post on ${assignment.platformRequirements.map((requirement) => requirement.platform).join(", ")}`
            : "Deliver content to the Business"}</p>
          {assignment.feedback && <Notice>{assignment.feedback}</Notice>}
          {request?.rejectionReason && <Notice>{request.rejectionReason}</Notice>}
          {assignment.reviewMediaUrl && <div className="private-review-media">
            <video controls preload="metadata" src={assignment.reviewMediaUrl} aria-label={`Revision ${assignment.contentRevisionNumber ?? 1} review video`} />
            <small>Private review · Revision {assignment.contentRevisionNumber ?? 1}</small>
          </div>}
          {assignment && !assignment.revisionAcceptanceRequired && (status === "InProgress" || status === "ChangesRequested") &&
            <form className="creator-work-submit" onSubmit={(event) => { event.preventDefault(); if (!media) return; void action.run(async (key) => {
              const form = new FormData(); form.append("media", media);
              await postForm(`/creator/ugc/assignments/${assignment.id}/submit-review`, form, key); setMedia(null); onSubmitted();
            }); }}>
              <label>Private review copy
                <input type="file" accept="video/mp4,image/jpeg,image/png" onChange={(event) => setMedia(event.target.files?.[0] ?? null)} required /></label>
              <small>Mark review video SAMPLE • WEYMELA REVIEW ONLY. Do not publish it yet.</small>
              <Button type="submit" disabled={action.busy || !media}>{action.busy ? "Submitting…" : status === "ChangesRequested" ? "Submit Revised Content" : "Submit for Review"}</Button>
            </form>}
          {assignment?.status === "Approved" && assignment.selectedPlatform && (!assignment.publication || ["Failed", "Expired"].includes(assignment.publication.status)) &&
            <form className="creator-work-submit" onSubmit={(event) => { event.preventDefault(); if (!externalContentId.trim() || !assignment.selectedSocialProfileId) return; void action.run(async (key) => {
              await post(`/creator/ugc/assignments/${assignment.id}/publication`, {
                provider: assignment.selectedPlatform,
                externalContentId: externalContentId.trim(),
                creatorSocialProfileId: assignment.selectedSocialProfileId,
              }, key); setExternalContentId(""); onSubmitted();
            }); }}>
              <label>{assignment.selectedPlatform} post ID
                <input value={externalContentId} onChange={(event) => setExternalContentId(event.target.value)} required maxLength={100} /></label>
              {assignment.selectedSocialProfileUrl && <a href={assignment.selectedSocialProfileUrl} target="_blank" rel="noopener noreferrer">Selected social profile</a>}
              <Button type="submit" disabled={action.busy || !externalContentId.trim()}>{action.busy ? "Checking…" : "Verify Publication"}</Button>
            </form>}
          {assignment?.publication?.status === "VerificationPending" && <Notice>Publication verification pending.</Notice>}
          {assignment?.publication?.status === "Verified" && !assignment.publication.wentLiveAtUtc && <Button disabled={action.busy} onClick={() => void action.run(async (key) => {
            await post(`/creator/ugc/assignments/${assignment.id}/go-live`, {}, key); onSubmitted();
          })}>{action.busy ? "Going live…" : "Go Live"}</Button>}
          {assignment?.publication?.wentLiveAtUtc && <Notice>Live for Customers.</Notice>}
          {action.error && <Notice error>{action.error}</Notice>}
        </div>
      </details> : request?.rejectionReason ? <details className="creator-work-disclosure">
        <summary>View details</summary>
        <div className="creator-work-disclosure-body">
          <Notice>{request.rejectionReason}</Notice>
          <ActionLink to={`/creator/ugc/${opportunityId}`} secondary>View details</ActionLink>
        </div>
      </details> : <ActionLink to={`/creator/ugc/${opportunityId}`} secondary>View details</ActionLink>}
    </div>
    <div className="creator-next-action">
      {status === "Pending" && <span>Waiting for Business decision</span>}
      {status === "Submitted" && <span>Waiting for Business review</span>}
      {request?.status === "Approved" && <span>Preparing your Promotion</span>}
      {assignment?.revisionAcceptanceRequired && <span>Review updated requirements with the Business before submitting</span>}
      {assignment?.status === "ChangesRequested" && <span>Submit the requested revision</span>}
      {assignment?.status === "Approved" && !assignment.selectedPlatform && <span>Content approved</span>}
      {assignment?.status === "Approved" && assignment.selectedPlatform && !assignment.publication && <span>Publish the approved content, then verify it</span>}
      {assignment?.publication?.status === "VerificationPending" && <span>Waiting for publication verification</span>}
      {assignment?.publication?.status === "Verified" && !assignment.publication.wentLiveAtUtc && <span>Ready to Go Live</span>}
    </div>
  </article>;
}

function PromotionParticipationCard({ row, onChanged }: { row: CreatorCampaign; onChanged: () => void }) {
  const action = useAction();
  const state = row.status;
  const canGoLive = state === "ReadyToGoLive" && row.contentReviewStatus === "Approved" && !row.participationId;
  return <article className="creator-promotion-row creator-work-row">
    <div className="creator-work-main">
      <div className="creator-work-top">
        <div className="creator-work-copy"><small>{row.business.displayName}</small><h3>{row.title}</h3></div>
        <CreatorStatus status={promotionStatus(row)} />
      </div>
      <p className="creator-work-type">{promotionTypeLabel(row.type)}</p>
      {row.participationId && <p className="creator-work-summary">{count(row.verifiedViews)} verified views · {amount(row.viewEarnings + row.saleCommissionEarnings)} earnings</p>}
      {row.participationId && row.remainingDays != null ? <p className="creator-work-summary">{row.remainingDays} days left</p> : null}
    </div>
    <div className="creator-next-action">
      {state === "UnderReview" && <span>Waiting for Business review</span>}
      {state === "Paused" && <span>Your Promotion is paused</span>}
      {state === "FundingRequired" && <span>Waiting for Business funding</span>}
      {state === "Approved" || state === "ChangesRequested" ? <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>{state === "Approved" ? "Add Content" : "Update Content"}</ActionLink> : null}
      {canGoLive && <Button disabled={action.busy} onClick={() => void action.run(async (key) => {
        await post(`/creator/creator-budgets/${row.budgetId}/go-live`, {}, key); onChanged();
      })}>{action.busy ? "Going live…" : "Go Live"}</Button>}
      {!canGoLive && state !== "Approved" && state !== "ChangesRequested" && row.participationId && <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>View progress</ActionLink>}
      {!canGoLive && state !== "Approved" && state !== "ChangesRequested" && !row.participationId && <ActionLink to={`/creator/promotions/${row.budgetId}`} secondary>View details</ActionLink>}
      {action.error && <Notice error>{action.error}</Notice>}
    </div>
  </article>;
}
