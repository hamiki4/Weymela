import { useState } from "react";
import { useParams, useSearchParams, Link } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import { PlatformOccupancy } from "../creator/CreatorPlatformIcon";
import { PlatformCapacityPicker, type PlatformCapacity } from "./PlatformCapacityPicker";
import type {
  Applicant,
  BusinessCampaign,
  CreatorBudget,
  CreatorCard,
} from "../../api/types";
import {
  ActivityList,
  Badge,
  Button,
  CreatorProfile,
  Currency,
  DataTable,
  Dialog,
  Empty,
  Field,
  FundsGrid,
  MoneyInput,
  Notice,
  PageHeader,
  Person,
  Resource,
  Section,
  Tabs,
} from "../../ui/components";
import {
  amount,
  campaignType,
  count,
  date,
  isViewAndSale,
} from "../../ui/format";

export function BusinessCampaignDetail() {
  const { id } = useParams();
  const resource = useResource<BusinessCampaign>(`/business/campaigns/${id}`);
  const [search, setSearch] = useSearchParams();
  const tab = search.get("tab") ?? "overview";
  const action = useAction();
  const [profile, setProfile] = useState<CreatorCard | null>(null);
  const [applicant, setApplicant] = useState<Applicant | null>(null);
  const [budget, setBudget] = useState<CreatorBudget | null>(null);
  const [value, setValue] = useState("");
  const [editOpen, setEditOpen] = useState(false);
  const [editDescription, setEditDescription] = useState("");
  const [editLocation, setEditLocation] = useState("");
  const [editTitle, setEditTitle] = useState("");
  const [editType, setEditType] = useState("ViewOnly");
  const [editCloses, setEditCloses] = useState("");
  const [editDue, setEditDue] = useState("");
  const [editRegion, setEditRegion] = useState("");
  const [editBudget, setEditBudget] = useState("");
  const [editPlatforms, setEditPlatforms] = useState<PlatformCapacity[]>([]);
  const [success, setSuccess] = useState("");
  const changed = (message: string) => {
    setSuccess(message);
    resource.reload();
  };
  const openEdit = (campaign: BusinessCampaign) => {
    setEditDescription(campaign.description);
    setEditLocation(campaign.campaign.location ?? "");
    setEditTitle(campaign.campaign.title);
    setEditType(campaign.campaign.type);
    const localDate = (value?: string | null) => {
      if (!value) return "";
      const parsed = new Date(value);
      const year = parsed.getFullYear();
      const month = String(parsed.getMonth() + 1).padStart(2, "0");
      const day = String(parsed.getDate()).padStart(2, "0");
      return `${year}-${month}-${day}`;
    };
    setEditCloses(localDate(campaign.campaign.applicationClosesAtUtc));
    setEditDue(localDate(campaign.campaign.contentDueAtUtc));
    setEditRegion(campaign.region ?? "");
    setEditBudget(String(campaign.campaign.campaignBudget));
    setEditPlatforms((campaign.campaign.platforms ?? []).map(row => ({ platform: row.platform, capacity: row.capacity, minimumAudience: row.minimumAudience ?? null })));
    setEditOpen(true);
  };
  const run = (path: string, body: unknown, message: string) =>
    action.run(async (key) => {
      await post(path, body, key);
      changed(message);
    });
  return (
    <Resource resource={resource}>
      {(data) => {
        const c = data.campaign;
        const approvedCount = Math.max(c.creatorCount, data.creators.length, c.platforms?.reduce((total, slot) => total + slot.approved, 0) ?? 0);
        const hasApproved = approvedCount > 0;
        const funds: [string, number][] = [
          ["Promotion budget", c.campaignBudget],
          ["Assigned to Creators", c.assignedToCreators],
          ["Available Promotion budget", c.availableCampaignBudget],
          ["Used", c.used],
          ["Remaining", c.remaining],
        ];
        return (
          <>
            <Link className="back-link" to="/business/campaigns">
              ← Promotions
            </Link>
            <PageHeader
              eyebrow={campaignType(c.type)}
              title={c.title}
              description={`${c.applicationClosesAtUtc ? `Applications close ${date(c.applicationClosesAtUtc)} · ` : ""}${c.contentDueAtUtc ? `Content due ${date(c.contentDueAtUtc)}` : ""}`}
              action={c.status === "Draft" ? undefined : <div className="actions"><Badge status={c.status} /><Button variant="secondary" onClick={() => openEdit(data)}>Edit</Button></div>}
            />
            {success && <Notice>{success}</Notice>}
            {action.error && !applicant && !budget && (
              <Notice error>{action.error}</Notice>
            )}
            {c.status === "Draft" && (
              <Notice error>
                This Promotion is not available in the current publishing workflow. Create a new Promotion to publish it directly.
              </Notice>
            )}
            {["Funded", "Published"].includes(c.status) && (
              <div className="callout">
                <div>
                  <h2>
                    {c.status === "Funded"
                        ? "Your Promotion is funded"
                        : "Your Promotion is published"}
                  </h2>
                  <p>
                    {c.status === "Funded"
                        ? "Publish it so eligible Creators can request to join."
                        : "Eligible Creators can request to join."}
                  </p>
                </div>
                <Button
                  disabled={
                    action.busy ||
                    (c.status === "Published" &&
                      new Date(c.startUtc).getTime() > Date.now())
                  }
                  onClick={() =>
                    void run(
                      `/business/campaigns/${id}/${c.status === "Funded" ? "publish" : "start"}`,
                      { version: c.version },
                      c.status === "Funded"
                        ? "Promotion published. Eligible Creators can now find it."
                        : "Promotion started.",
                    )
                  }
                >
                  {c.status === "Funded" ? "Publish Promotion" : "Open Promotion"}
                </Button>
              </div>
            )}
            <Tabs
              label="Promotion sections"
              value={tab}
              onChange={(value) => setSearch({ tab: value })}
              items={[
                { value: "overview", label: "Overview" },
                { value: "applicants", label: "Creator Applicants" },
                { value: "budgets", label: "Approved Creators" },
                { value: "funds", label: "Promotion funds" },
                { value: "performance", label: "Performance" },
              ]}
            />
            {tab === "overview" && (
              <div className="two-column">
                <Section title="Promotion">
                  {c.slogan?.trim() && <p className="business-promotion-slogan">{c.slogan}</p>}
                  <p className="preserve-lines">{data.description}</p>
                  <dl className="detail-list">
                    {data.requirements?.trim() && <div>
                      <dt>Requirements</dt>
                      <dd>{data.requirements}</dd>
                    </div>}
                    {data.category && <div>
                      <dt>Creator category</dt>
                      <dd>{data.category}</dd>
                    </div>}
                    {data.region && <div>
                      <dt>Region</dt>
                      <dd>{data.region}</dd>
                    </div>}
                    {data.minimumVerifiedFollowers != null && <div>
                      <dt>Verified followers</dt>
                      <dd>{count(data.minimumVerifiedFollowers)} minimum</dd>
                    </div>}
                  </dl>
                  {!!c.platforms?.length && <div className="business-platform-summary"><strong>Social platforms</strong><PlatformOccupancy slots={c.platforms} /><small>Creators {c.platforms.reduce((total, slot) => total + slot.approved, 0)}/{c.platforms.reduce((total, slot) => total + slot.capacity, 0)} · {data.applicants.filter((applicant) => applicant.status === "Pending").length} pending requests</small></div>}
                </Section>
                <Section title="Promotion funds" action={<Currency />}>
                  <FundsGrid values={funds} />
                  <div className="pricing-note">
                    <strong>Saved Promotion pricing</strong>
                    <p>
                      {amount(data.pricing.businessPays)} per{" "}
                      {count(data.pricing.views)} verified views
                      {isViewAndSale(c.type) &&
                        ` · ${amount(data.pricing.saleCostPercent)}% per verified sale`}
                    </p>
                  </div>
                </Section>
              </div>
            )}
            {tab === "applicants" && (
              <Section
                title="Creator Applicants"
                description="Discover their ideas, then give each approved Creator a budget for verified activity."
              >
                <div className="campaign-grid">
                  {data.applicants.map((a) => (
                    <article className="data-card" key={a.id}>
                      <div className="card-head">
                        <Person person={a.creator} detail photo />
                        <Badge status={a.status} />
                      </div>
                      <p>
                        {a.creator.region} ·{" "}
                        {count(a.creator.verifiedFollowers)} verified followers
                      </p>
                      {a.socialProfile && <p className="fine-print"><strong>{a.platform}</strong> · <a href={a.socialProfile.profileUrl} target="_blank" rel="noreferrer">{a.socialProfile.profileUrl}</a><br />
                        Self-reported: {count(a.socialProfile.selfReportedAudience)} · Admin-verified: {a.socialProfile.verifiedAudience == null ? "Not verified" : count(a.socialProfile.verifiedAudience)}{a.socialProfile.audienceVerificationSource === "AdminVerified" ? " · Admin Verified" : ""}</p>}
                      {a.message?.trim() && <p className="preserve-lines">{a.message}</p>}
                      {a.contentConcept && (
                        <div className="pricing-note">
                          <strong>Content concept</strong>
                          <p>{a.contentConcept}</p>
                        </div>
                      )}
                      <div className="actions">
                        {a.status === "Pending" && (
                          <>
                            <Button
                              onClick={() => {
                                setApplicant(a);
                                setValue("");
                              }}
                            >
                              Approve
                            </Button>
                            <Button
                              variant="secondary"
                              disabled={action.busy}
                              onClick={() =>
                                void run(
                                  `/business/applicants/${a.id}/reject`,
                                  { version: c.version },
                                  "Request rejected.",
                                )
                              }
                            >
                              Reject
                            </Button>
                          </>
                        )}
                        <Button
                          variant="quiet"
                          onClick={() => setProfile(a.creator)}
                        >
                          View Profile
                        </Button>
                      </div>
                    </article>
                  ))}
                </div>
                {!data.applicants.length && (
                  <Empty
                    title="No Creator applicants yet"
                    message="Publish the funded Promotion to receive requests."
                    icon="people"
                  />
                )}
              </Section>
            )}
            {tab === "budgets" && (
              <Section
                title="Approved Creators"
                description={`Available Promotion budget: ${amount(c.availableCampaignBudget)}`}
                action={<Currency />}
              >
                <DataTable
                  rows={data.creators}
                  rowKey={(r) => r.id}
                  label="Creator Budgets"
                  columns={[
                    {
                      label: "Creator",
                      cell: (r) => <Person person={r.creator} photo />,
                    },
                    {
                      label: "Creator Budget",
                      cell: (r) => amount(r.creatorBudget),
                      numeric: true,
                    },
                    {
                      label: "Used",
                      cell: (r) => amount(r.used),
                      numeric: true,
                    },
                    {
                      label: "Budget Remaining",
                      cell: (r) => amount(r.budgetRemaining),
                      numeric: true,
                    },
                    {
                      label: "Views",
                      cell: (r) => count(r.views),
                      numeric: true,
                    },
                    {
                      label: "Sales",
                      cell: (r) => count(r.sales),
                      numeric: true,
                    },
                    {
                      label: "Status",
                      cell: (r) => <Badge status={r.status} />,
                    },
                    {
                      label: "Action",
                      cell: (r) => (
                        <Button
                          variant="secondary"
                          disabled={!r.canIncrease}
                          onClick={() => {
                            setBudget(r);
                            setValue("");
                          }}
                        >
                          Increase Budget
                        </Button>
                      ),
                    },
                  ]}
                  card={(r) => (
                    <>
                      <div className="card-head">
                        <Person person={r.creator} photo />
                        <Badge status={r.status} />
                      </div>
                      <FundsGrid
                        values={[
                          ["Creator Budget", r.creatorBudget],
                          ["Used", r.used],
                          ["Budget Remaining", r.budgetRemaining],
                        ]}
                      />
                      <p className="fine-print">
                        {count(r.views)} verified views · {count(r.sales)} sales
                      </p>
                      <Button
                        variant="secondary"
                        disabled={!r.canIncrease}
                        onClick={() => {
                          setBudget(r);
                          setValue("");
                        }}
                      >
                        Increase Budget
                      </Button>
                    </>
                  )}
                  empty={
                    <Empty
                      title="Your Creator team starts here"
                      message="Approve an applicant and set a Creator Budget to get started."
                      icon="people"
                    />
                  }
                />
              </Section>
            )}
            {tab === "funds" && (
              <Section title="Promotion funds" action={<Currency />}>
                <FundsGrid values={funds} />
                <Notice>
                  Unused Creator Budget returns to the Available Promotion
                  Budget, not your available wallet balance. Earned funds remain
                  earned.
                </Notice>
              </Section>
            )}
            {tab === "performance" && (
              <div className="two-column">
                <Section title="Verified performance">
                  <FundsGrid
                    values={[
                      [
                        "Verified views",
                        data.creators.reduce((sum, r) => sum + r.views, 0),
                      ],
                      [
                        "Confirmed Sales",
                        data.creators.reduce((sum, r) => sum + r.sales, 0),
                      ],
                      ["Promotion spend", c.used],
                    ]}
                  />
                </Section>
                <Section title="Promotion history">
                  <ActivityList items={data.history} />
                </Section>
              </div>
            )}
            <Dialog title="Edit Promotion details" open={editOpen} onClose={() => { if (!action.busy) setEditOpen(false); }}>
              <p className="muted">{hasApproved ? "Some terms are locked because a Creator has been approved." : "You can update the full Promotion while no Creator is approved. Pending applications do not lock editing."}</p>
              <form className="form-grid" onSubmit={(event) => { event.preventDefault(); void action.run(async key => {
                const endOfLocalDayUtc = (value: string) => {
                  if (!value) return null;
                  const [year, month, day] = value.split("-").map(Number);
                  return new Date(year, month - 1, day, 23, 59, 59, 999).toISOString();
                };
                await post(`/business/promotions/${id}/update`, { description: editDescription, slogan: c.slogan ?? null, location: editLocation || null, resources: null, version: c.version,
                  ...(hasApproved ? {} : { title: editTitle, type: editType, startUtc: c.startUtc, endUtc: c.endUtc, applicationClosesAtUtc: endOfLocalDayUtc(editCloses), contentDueAtUtc: endOfLocalDayUtc(editDue), requirements: data.requirements ?? null, region: editRegion || null, platforms: editPlatforms, campaignBudget: Number(editBudget) }) }, key);
                setEditOpen(false); changed("Promotion details updated.");
              }); }}>
                {!hasApproved && <>
                  <Field label="Promotion title" wide><input value={editTitle} onChange={event => setEditTitle(event.target.value)} maxLength={120} required /></Field>
                  <Field label="Promotion type"><select value={editType} onChange={event => setEditType(event.target.value)}><option value="ViewOnly">View Only</option><option value="ViewPlusCommission">View + Sale</option></select></Field>
                  <Field label="Promotion budget"><MoneyInput value={editBudget} onChange={event => setEditBudget(event.target.value)} /></Field>
                  <Field label="Application closes"><input type="date" value={editCloses} onChange={event => setEditCloses(event.target.value)} /></Field>
                  <Field label="Content due"><input type="date" min={editCloses || undefined} value={editDue} onChange={event => setEditDue(event.target.value)} /></Field>
                  <Field label="Region"><input value={editRegion} onChange={event => setEditRegion(event.target.value)} maxLength={80} /></Field>
                  <PlatformCapacityPicker value={editPlatforms} onChange={setEditPlatforms} title="Creators / platforms" />
                </>}
                <Field label="Description" wide><textarea value={editDescription} onChange={event => setEditDescription(event.target.value)} maxLength={3000} rows={5} required /></Field>
                <Field label="Location"><input value={editLocation} onChange={event => setEditLocation(event.target.value)} maxLength={160} /></Field>
                {action.error && <Notice error>{action.error}</Notice>}
                <div className="form-actions wide"><Button type="submit" disabled={action.busy || !editDescription.trim()}>{action.busy ? "Saving…" : "Save changes"}</Button></div>
              </form>
            </Dialog>
            <Dialog
              title={
                applicant
                  ? `Approve ${applicant.creator.displayName}`
                  : "Approve Creator"
              }
              open={!!applicant}
              onClose={() => {
                if (!action.busy) setApplicant(null);
              }}
            >
              <p>
                Available Promotion budget:{" "}
                <strong>{amount(c.availableCampaignBudget)}</strong>
              </p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void action.run(async (key) => {
                    await post(
                      `/business/applicants/${applicant!.id}/approve`,
                      { amount: Number(value), version: c.version },
                      key,
                    );
                    setApplicant(null);
                    changed("Creator approved and Creator Budget saved.");
                  });
                }}
              >
                <fieldset disabled={action.busy}>
                  <Field
                    label="Creator Budget"
                    help="Maximum Promotion budget available for this Creator’s verified activity."
                  >
                    <MoneyInput
                      value={value}
                      max={c.availableCampaignBudget}
                      onChange={(e) => setValue(e.target.value)}
                    />
                  </Field>
                  {action.error && <Notice error>{action.error}</Notice>}
                  <Button
                    type="submit"
                    disabled={
                      action.busy ||
                      !value ||
                      Number(value) > c.availableCampaignBudget
                    }
                  >
                    {action.busy ? "Saving…" : "Approve & Set Budget"}
                  </Button>
                </fieldset>
              </form>
            </Dialog>
            <Dialog
              title={
                budget
                  ? `Increase ${budget.creator.displayName}’s Budget`
                  : "Increase Budget"
              }
              open={!!budget}
              onClose={() => {
                if (!action.busy) setBudget(null);
              }}
            >
              <p>
                Available Promotion budget:{" "}
                <strong>{amount(c.availableCampaignBudget)}</strong>
              </p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void action.run(async (key) => {
                    await post(
                      `/business/creator-budgets/${budget!.id}/increase`,
                      { amount: Number(value), version: budget!.version },
                      key,
                    );
                    setBudget(null);
                    changed("Creator Budget increased.");
                  });
                }}
              >
                <fieldset disabled={action.busy}>
                  <Field
                    label="Amount to add"
                    help="Uses only your available Promotion budget."
                  >
                    <MoneyInput
                      value={value}
                      max={c.availableCampaignBudget}
                      onChange={(e) => setValue(e.target.value)}
                    />
                  </Field>
                  {value && budget && (
                    <p>
                      New Creator Budget:{" "}
                      {amount(budget.creatorBudget + Number(value))}
                    </p>
                  )}
                  {action.error && <Notice error>{action.error}</Notice>}
                  <Button
                    type="submit"
                    disabled={
                      action.busy ||
                      !value ||
                      Number(value) > c.availableCampaignBudget
                    }
                  >
                    {action.busy ? "Saving…" : "Confirm Increase"}
                  </Button>
                </fieldset>
              </form>
            </Dialog>
            <Dialog
              title="Creator Profile"
              open={!!profile}
              onClose={() => setProfile(null)}
            >
              {profile && <CreatorProfile person={profile} photo />}
            </Dialog>
          </>
        );
      }}
    </Resource>
  );
}
