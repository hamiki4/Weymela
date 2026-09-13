import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  Activity,
  AdminCampaign,
  AdminHome,
  BusinessOversight,
  CampaignRow,
  CreatorOversight,
} from "../../api/types";
import {
  ActionLink,
  ActivityList,
  Badge,
  Button,
  Currency,
  DataTable,
  Empty,
  Field,
  FundsGrid,
  Metric,
  Notice,
  PageHeader,
  Person,
  Resource,
  Section,
} from "../../ui/components";
import { amount, campaignType, count, date } from "../../ui/format";
import { CampaignTable } from "../business/BusinessPages";

export function AdminRoleEnrollments() {
  const resource = useResource<EnrollmentRow[]>("/admin/role-enrollments");
  const action = useAction();
  return <><PageHeader eyebrow="People and access" title="Profile requests" description="Approve additional profiles without replacing an existing role." />
    <Resource resource={resource}>{(rows) => rows.length === 0 ? <Section title="No pending requests"><Empty title="Everything is up to date" message="New Creator and Business requests will appear here." /></Section> : <Section title="Under review"><div className="stack-list">{rows.map((row) => <article className="amount-row" key={row.id}><div><strong>{row.role} · {row.displayName}</strong><small>{row.publicId}</small></div><div className="actions"><Button disabled={action.busy} onClick={() => void action.run(async key => { await post(`/admin/role-enrollments/${row.id}/review`, { approve: true, expectedVersion: row.version }, key); resource.reload(); })}>Approve</Button><Button variant="secondary" disabled={action.busy} onClick={() => void action.run(async key => { await post(`/admin/role-enrollments/${row.id}/review`, { approve: false, expectedVersion: row.version }, key); resource.reload(); })}>Reject</Button></div></article>)}</div>{action.error && <Notice error>{action.error}</Notice>}</Section>}</Resource></>;
}

type EnrollmentRow = { id: string; role: string; status: string; displayName: string; publicId: string; version: number };

export function AdminDashboard() {
  const resource = useResource<AdminHome>("/admin/home");
  return (
    <Resource resource={resource}>
      {(d) => (
        <>
          <PageHeader
            eyebrow="Platform control tower"
            title="A clear view of Weymela."
            description="Campaign activity, financial oversight and the people making it happen."
            action={
              <ActionLink to="/admin/campaigns">Explore Campaigns</ActionLink>
            }
          />
          <div className="metric-grid four">
            <Metric
              label="Active Campaigns"
              value={count(d.activeCampaigns)}
              emphasis
              icon="campaign"
            />
            <Metric
              label="Businesses"
              value={count(d.businesses)}
              icon="business"
            />
            <Metric label="Creators" value={count(d.creators)} icon="people" />
            <Metric
              label="Campaign spend"
              value={amount(d.campaignSpend)}
              note="ETB · verified activity"
            />
          </div>
          <div className="two-column">
            <Section title="Financial overview" action={<Currency />}>
              <FundsGrid
                values={[
                  ["Creator earnings", d.creatorEarnings],
                  ["Customer cashback", d.customerCashback],
                  ["Platform revenue", d.platformRevenue],
                ]}
              />
              <div className="actions section-kicker-space">
                <ActionLink to="/admin/payouts" secondary>
                  Manage Payouts
                </ActionLink>
                <Link className="text-link" to="/admin/financial-settings">
                  Financial Settings
                </Link>
              </div>
            </Section>
            <Section title="Recent activity">
              <ActivityList items={d.activity.slice(0, 6)} />
            </Section>
          </div>
        </>
      )}
    </Resource>
  );
}
export function AdminCampaigns() {
  const resource = useResource<CampaignRow[]>("/admin/campaigns");
  const [filters, set] = useState({
    business: "",
    campaign: "",
    type: "",
    status: "",
    start: "",
    end: "",
  });
  return (
    <>
      <PageHeader
        eyebrow="Campaign control tower"
        title="Campaigns"
        description="Follow every Campaign from committed funds to verified activity."
      />
      <Resource resource={resource}>
        {(rows) => {
          const filtered = rows.filter(
            (r) =>
              r.business
                .toLowerCase()
                .includes(filters.business.toLowerCase()) &&
              `${r.title} ${r.publicId}`
                .toLowerCase()
                .includes(filters.campaign.toLowerCase()) &&
              (!filters.type || r.type === filters.type) &&
              (!filters.status || r.status === filters.status) &&
              (!filters.start || r.startUtc.slice(0, 10) >= filters.start) &&
              (!filters.end || r.startUtc.slice(0, 10) <= filters.end),
          );
          return (
            <Section title="All Campaigns" action={<Currency />}>
              <div className="filter-grid six">
                <Field label="Business">
                  <input
                    type="search"
                    value={filters.business}
                    onChange={(e) =>
                      set({ ...filters, business: e.target.value })
                    }
                    placeholder="Business name"
                  />
                </Field>
                <Field label="Campaign">
                  <input
                    type="search"
                    value={filters.campaign}
                    onChange={(e) =>
                      set({ ...filters, campaign: e.target.value })
                    }
                    placeholder="Title or public ID"
                  />
                </Field>
                <Field label="Type">
                  <select
                    value={filters.type}
                    onChange={(e) => set({ ...filters, type: e.target.value })}
                  >
                    <option value="">All types</option>
                    <option value="ViewOnly">View Only</option>
                    <option value="ViewPlusCommission">
                      View + Commission
                    </option>
                  </select>
                </Field>
                <Field label="Status">
                  <select
                    value={filters.status}
                    onChange={(e) =>
                      set({ ...filters, status: e.target.value })
                    }
                  >
                    <option value="">All statuses</option>
                    {[
                      "Draft",
                      "Funded",
                      "Published",
                      "Active",
                      "BudgetExhausted",
                      "Completed",
                    ].map((s) => (
                      <option key={s} value={s}>
                        {s === "BudgetExhausted" ? "Budget exhausted" : s}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Starts from">
                  <input
                    type="date"
                    value={filters.start}
                    onChange={(e) => set({ ...filters, start: e.target.value })}
                  />
                </Field>
                <Field label="Starts through">
                  <input
                    type="date"
                    value={filters.end}
                    min={filters.start}
                    onChange={(e) => set({ ...filters, end: e.target.value })}
                  />
                </Field>
              </div>
              <p className="fine-print">{filtered.length} Campaigns</p>
              <div className="admin-table">
                <CampaignTable campaigns={filtered} admin />
              </div>
            </Section>
          );
        }}
      </Resource>
    </>
  );
}
export function AdminCampaignDetail() {
  const { id } = useParams();
  const resource = useResource<AdminCampaign>(`/admin/campaigns/${id}`);
  return (
    <>
      <Link className="back-link" to="/admin/campaigns">
        ← Campaigns
      </Link>
      <Resource resource={resource}>
        {(d) => {
          const c = d.campaign;
          return (
            <>
              <PageHeader
                eyebrow={`${c.business} · ${c.publicId}`}
                title={c.title}
                description={`${campaignType(c.type)} · ${date(c.startUtc)} – ${date(c.endUtc)}`}
                action={<Badge status={c.status} />}
              />
              <Section title="Business / Campaign" action={<Currency />}>
                <FundsGrid
                  values={[
                    ["Campaign Budget", c.campaignBudget],
                    ["Assigned", c.assignedToCreators],
                    ["Unassigned", c.availableCampaignBudget],
                    ["Used", c.used],
                    ["Remaining", c.remaining],
                  ]}
                />
              </Section>
              <Section title="Creator participation" action={<Currency />}>
                <div className="admin-table">
                  <DataTable
                    rows={d.creators}
                    rowKey={(r) => r.creator.id}
                    label="Admin Creator participation"
                    columns={[
                      {
                        label: "Creator",
                        cell: (r) => <Person person={r.creator} />,
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
                        label: "Verified Views",
                        cell: (r) => count(r.verifiedViews),
                        numeric: true,
                      },
                      {
                        label: "Rewarded Views",
                        cell: (r) => count(r.rewardedViews),
                        numeric: true,
                      },
                      {
                        label: "View Earnings",
                        cell: (r) => amount(r.viewEarnings),
                        numeric: true,
                      },
                      {
                        label: "Verified Sales",
                        cell: (r) => count(r.verifiedSales),
                        numeric: true,
                      },
                      {
                        label: "Sale Commission",
                        cell: (r) => amount(r.saleCommission),
                        numeric: true,
                      },
                      {
                        label: "Status",
                        cell: (r) => <Badge status={r.status} />,
                      },
                    ]}
                    card={(r) => (
                      <>
                        <div className="card-head">
                          <Person person={r.creator} />
                          <Badge status={r.status} />
                        </div>
                        <FundsGrid
                          values={[
                            ["Creator Budget", r.creatorBudget],
                            ["Used", r.used],
                            ["Budget Remaining", r.budgetRemaining],
                            ["Verified Views", r.verifiedViews],
                            ["Rewarded Views", r.rewardedViews],
                            ["View Earnings", r.viewEarnings],
                            ["Verified Sales", r.verifiedSales],
                            ["Sale Commission", r.saleCommission],
                          ]}
                        />
                      </>
                    )}
                    empty={
                      <Empty
                        title="No approved Creators yet"
                        message="Creator participation appears after Business approval and a Creator Budget is assigned."
                        icon="people"
                      />
                    }
                  />
                </div>
                {d.creators.map((r) => (
                  <details className="audit-detail" key={r.creator.id}>
                    <summary>
                      {r.creator.displayName} · verification and financial
                      detail
                    </summary>
                    <FundsGrid
                      values={[
                        ["Baseline views", r.baselineViews],
                        ["Latest verified views", r.latestVerifiedViews],
                        ["Customer cashback", r.customerCashback],
                        ["Platform revenue", r.platformRevenue],
                      ]}
                    />
                  </details>
                ))}
              </Section>
              <div className="two-column">
                <Section title="Financial summary" action={<Currency />}>
                  <FundsGrid
                    values={[
                      ["Business Campaign spend", c.used],
                      ["Creator earnings", d.creatorEarnings],
                      ["Customer cashback", d.customerCashback],
                      ["Platform revenue", d.platformRevenue],
                    ]}
                  />
                </Section>
                <Section title="Campaign audit / history">
                  <ActivityList items={d.history} />
                </Section>
              </div>
            </>
          );
        }}
      </Resource>
    </>
  );
}
export function AdminBusinesses() {
  const resource = useResource<BusinessOversight[]>("/admin/businesses");
  return (
    <>
      <PageHeader
        eyebrow="Business oversight"
        title="Businesses"
        description="Authoritative wallet balances and Campaign activity."
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Business accounts" action={<Currency />}>
            <DataTable
              rows={rows}
              rowKey={(r) => r.business.id}
              label="Business accounts"
              columns={[
                {
                  label: "Business",
                  cell: (r) => (
                    <>
                      <strong>{r.business.displayName}</strong>
                      <small>{r.business.region}</small>
                    </>
                  ),
                },
                {
                  label: "Account Status",
                  cell: (r) => <Badge status={r.status} />,
                },
                {
                  label: "Total Balance",
                  cell: (r) => amount(r.totalBalance),
                  numeric: true,
                },
                {
                  label: "Available",
                  cell: (r) => amount(r.available),
                  numeric: true,
                },
                {
                  label: "Reserved",
                  cell: (r) => amount(r.reserved),
                  numeric: true,
                },
                {
                  label: "Active Campaigns",
                  cell: (r) => count(r.activeCampaigns),
                  numeric: true,
                },
                { label: "Last Deposit", cell: (r) => date(r.lastDepositUtc) },
              ]}
              card={(r) => (
                <>
                  <div className="card-head">
                    <h3>{r.business.displayName}</h3>
                    <Badge status={r.status} />
                  </div>
                  <p className="fine-print">{r.business.region}</p>
                  <FundsGrid
                    values={[
                      ["Total Balance", r.totalBalance],
                      ["Available", r.available],
                      ["Reserved", r.reserved],
                      ["Active Campaigns", r.activeCampaigns],
                    ]}
                  />
                  <p className="fine-print">
                    Last Deposit: {date(r.lastDepositUtc)}
                  </p>
                </>
              )}
              empty={
                <Empty
                  title="No Business accounts yet"
                  message="Approved Business accounts and their wallet balances will appear here."
                />
              }
            />
          </Section>
        )}
      </Resource>
    </>
  );
}
export function AdminCreators() {
  const resource = useResource<CreatorOversight[]>("/admin/creators");
  return (
    <>
      <PageHeader
        eyebrow="Creator oversight"
        title="Creators"
        description="Verified profiles, active Campaigns and payout eligibility."
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Creator accounts" action={<Currency />}>
            <DataTable
              rows={rows}
              rowKey={(r) => r.creator.id}
              label="Creator accounts"
              columns={[
                {
                  label: "Creator",
                  cell: (r) => <Person person={r.creator} />,
                },
                { label: "Status", cell: (r) => <Badge status={r.status} /> },
                {
                  label: "Verified Followers",
                  cell: (r) => count(r.creator.verifiedFollowers),
                  numeric: true,
                },
                {
                  label: "Verified Views",
                  cell: (r) => count(r.creator.verifiedViews),
                  numeric: true,
                },
                {
                  label: "Active Campaigns",
                  cell: (r) => count(r.activeCampaigns),
                  numeric: true,
                },
                {
                  label: "Available Earnings",
                  cell: (r) => amount(r.availableEarnings),
                  numeric: true,
                },
                {
                  label: "Payout",
                  cell: (r) => (
                    <Badge
                      status={r.payoutEligible ? "Eligible" : "Below threshold"}
                    />
                  ),
                },
              ]}
              card={(r) => (
                <>
                  <div className="card-head">
                    <Person person={r.creator} />
                    <Badge status={r.status} />
                  </div>
                  <FundsGrid
                    values={[
                      ["Verified Followers", r.creator.verifiedFollowers],
                      ["Verified Views", r.creator.verifiedViews],
                      ["Active Campaigns", r.activeCampaigns],
                      ["Available Earnings", r.availableEarnings],
                    ]}
                  />
                  <Badge
                    status={
                      r.payoutEligible
                        ? "Eligible for payout"
                        : "Below threshold"
                    }
                  />
                </>
              )}
              empty={
                <Empty
                  title="No Creator accounts yet"
                  message="Verified Creators will appear here."
                  icon="people"
                />
              }
            />
          </Section>
        )}
      </Resource>
    </>
  );
}
export function AdminActivity({
  notifications = false,
}: {
  notifications?: boolean;
}) {
  const resource = useResource<Activity[]>(
    notifications ? "/admin/notifications" : "/admin/audit",
  );
  return (
    <>
      <PageHeader
        eyebrow="Operational visibility"
        title={notifications ? "Notifications" : "Audit"}
        description={
          notifications
            ? "Recent recorded platform events. This is a read-only activity feed; opening it does not change notification delivery state."
            : "Significant recorded actions with timestamps and audit references."
        }
      />
      <Resource resource={resource}>
        {(items) => (
          <Section
            title={notifications ? "Latest platform activity" : "Audit history"}
          >
            <ActivityList items={items} />
          </Section>
        )}
      </Resource>
    </>
  );
}
