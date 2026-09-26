import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  Activity,
  AdminCampaign,
  AdminHome,
  OperationsHome,
  BusinessOversight,
  CampaignRow,
  CreatorOversight,
  OperationsBusinessView,
  OperationsCampaignView,
  OperationsCreatorView,
  OperationsCustomerView,
  OperationsUgcView,
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
import {
  amount,
  campaignType,
  count,
  date,
  promotionTypeCode,
} from "../../ui/format";
import { CampaignTable } from "../business/BusinessPages";
import { useSession } from "../../app/Session";

export function OperationsDashboard() {
  const resource = useResource<OperationsHome>("/admin/operations/home");
  return <Resource resource={resource}>{(d) => <>
    <PageHeader title="Operations Dashboard" />
    <div className="metric-grid four">
      <Metric label="Profile requests" value={count(d.pendingReviews)} icon="people" />
      <Metric label="Active campaigns" value={count(d.activeCampaigns)} icon="campaign" />
      <Metric label="Creator payouts" value={count(d.pendingCreatorPayouts)} icon="money" />
      <Metric label="Customer payouts" value={count(d.pendingCustomerPayouts)} icon="wallet" />
    </div>
    <div className="two-column">
      <Section title="Operational queues">
        <div className="actions">
          <ActionLink to="/admin/role-enrollments">Review profile requests</ActionLink>
          <ActionLink to="/admin/payouts" secondary>Process payouts</ActionLink>
        </div>
      </Section>
      <Section title="Operational visibility">
        <div className="actions">
          <ActionLink to="/admin/operations/businesses" secondary>Businesses</ActionLink>
          <ActionLink to="/admin/operations/creators" secondary>Creators</ActionLink>
          <ActionLink to="/admin/operations/customers" secondary>Customers</ActionLink>
          <ActionLink to="/admin/campaigns" secondary>Campaigns</ActionLink>
          <ActionLink to="/admin/ugc" secondary>UGC</ActionLink>
        </div>
      </Section>
    </div>
  </>}</Resource>;
}

export function OperationsBusinesses() {
  const resource = useResource<OperationsBusinessView[]>("/admin/businesses");
  return <><PageHeader eyebrow="Business operations" title="Businesses" description="Operational Business status and campaign activity. Wallet balances remain restricted to Platform Admin." />
    <Resource resource={resource}>{(rows) => <Section title="Business accounts"><DataTable rows={rows} rowKey={(r) => r.business.id} label="Operational Business accounts" columns={[
      { label: "Business", cell: (r) => <><strong>{r.business.displayName}</strong><small>{r.business.region}</small></> },
      { label: "Status", cell: (r) => <Badge status={r.status} /> },
      { label: "Active Campaigns", cell: (r) => count(r.activeCampaigns), numeric: true },
      { label: "Last Deposit", cell: (r) => date(r.lastDepositUtc) },
    ]} card={(r) => <><div className="card-head"><h3>{r.business.displayName}</h3><Badge status={r.status} /></div><p>{r.business.region}</p><p className="fine-print">{count(r.activeCampaigns)} active campaigns · Last deposit {date(r.lastDepositUtc)}</p></>} empty={<Empty title="No Business accounts yet" message="Approved Business accounts appear here." />} /></Section>}</Resource>
  </>;
}

export function OperationsCreators() {
  const resource = useResource<OperationsCreatorView[]>("/admin/creators");
  return <><PageHeader eyebrow="Creator operations" title="Creators" description="Operational Creator status and campaign participation. Payout amounts are handled in the payout queue." />
    <Resource resource={resource}>{(rows) => <Section title="Creator accounts"><DataTable rows={rows} rowKey={(r) => r.creator.id} label="Operational Creator accounts" columns={[
      { label: "Creator", cell: (r) => <Person person={r.creator} /> },
      { label: "Status", cell: (r) => <Badge status={r.status} /> },
      { label: "Active Campaigns", cell: (r) => count(r.activeCampaigns), numeric: true },
      { label: "Payout Queue", cell: (r) => <Badge status={r.payoutEligible ? "Eligible" : "No pending payout"} /> },
    ]} card={(r) => <><div className="card-head"><Person person={r.creator} /><Badge status={r.status} /></div><p>{count(r.activeCampaigns)} active campaigns</p><Badge status={r.payoutEligible ? "Eligible for payout" : "No pending payout"} /></>} empty={<Empty title="No Creator accounts yet" message="Approved Creators appear here." icon="people" />} /></Section>}</Resource>
  </>;
}

export function OperationsCustomers() {
  const resource = useResource<OperationsCustomerView[]>("/admin/customers");
  return <><PageHeader eyebrow="Customer operations" title="Customers" description="Operational Customer status for support and account review." />
    <Resource resource={resource}>{(rows) => <Section title="Customer accounts"><DataTable rows={rows} rowKey={(r) => r.customer.id} label="Operational Customer accounts" columns={[
      { label: "Customer", cell: (r) => <><strong>{r.customer.displayName}</strong><small>{r.customer.publicId}</small></> },
      { label: "Status", cell: (r) => <Badge status={r.status} /> },
    ]} card={(r) => <div className="card-head"><div><strong>{r.customer.displayName}</strong><small>{r.customer.publicId}</small></div><Badge status={r.status} /></div>} empty={<Empty title="No Customer accounts yet" message="Active Customer accounts appear here." icon="people" />} /></Section>}</Resource>
  </>;
}

export function OperationsCampaigns() {
  const resource = useResource<OperationsCampaignView[]>("/admin/campaigns");
  return <><PageHeader eyebrow="Campaign operations" title="Campaigns" description="Operational promotion status and participation without Platform financial totals." />
    <Resource resource={resource}>{(rows) => <Section title="Campaign activity"><DataTable rows={rows} rowKey={(r) => r.id} label="Operational campaigns" columns={[
      { label: "Campaign", cell: (r) => <><strong>{r.title}</strong><small>{r.publicId}</small></> },
      { label: "Business", cell: (r) => r.business },
      { label: "Type", cell: (r) => r.type },
      { label: "Creators", cell: (r) => count(r.creatorCount), numeric: true },
      { label: "Status", cell: (r) => <Badge status={r.status} /> },
      { label: "Window", cell: (r) => `${date(r.startUtc)} – ${date(r.endUtc)}` },
    ]} card={(r) => <><div className="card-head"><strong>{r.title}</strong><Badge status={r.status} /></div><p>{r.business} · {r.type}</p><p className="fine-print">{count(r.creatorCount)} creators · {date(r.startUtc)} – {date(r.endUtc)}</p></>} empty={<Empty title="No campaigns yet" message="Campaigns appear here as Businesses create them." icon="campaign" />} /></Section>}</Resource>
  </>;
}

export function OperationsUgc() {
  const resource = useResource<OperationsUgcView[]>("/admin/ugc");
  return <><PageHeader eyebrow="UGC operations" title="UGC" description="Operational UGC opportunities and delivery status. Global UGC financial configuration remains restricted." />
    <Resource resource={resource}>{(rows) => <Section title="UGC opportunities"><DataTable rows={rows} rowKey={(r) => r.id} label="Operational UGC opportunities" columns={[
      { label: "Opportunity", cell: (r) => <><strong>{r.title}</strong><small>{r.business}</small></> },
      { label: "Status", cell: (r) => <Badge status={r.status} /> },
      { label: "Creators", cell: (r) => `${r.approvedCreators}/${r.creatorsNeeded}` },
      { label: "Due", cell: (r) => date(r.dueDateUtc) },
      { label: "Offer", cell: (r) => r.customerOfferStatus ?? "None" },
    ]} card={(r) => <><div className="card-head"><strong>{r.title}</strong><Badge status={r.status} /></div><p>{r.business} · {r.approvedCreators}/{r.creatorsNeeded} creators</p><p className="fine-print">Due {date(r.dueDateUtc)} · Customer offer {r.customerOfferStatus ?? "None"}</p></>} empty={<Empty title="No UGC opportunities yet" message="UGC opportunities appear here for operational support." icon="sparkle" />} /></Section>}</Resource>
  </>;
}

export function AdminRoleEnrollments() {
  const resource = useResource<EnrollmentRow[]>("/admin/role-enrollments");
  const action = useAction();
  return <><PageHeader eyebrow="People and access" title="Profile requests" description="Approve additional profiles without replacing an existing role." />
    <Resource resource={resource}>{(rows) => rows.length === 0 ? <Section title="No pending requests"><Empty title="Everything is up to date" message="New Creator and Business requests will appear here." /></Section> : <Section title="Under review"><div className="stack-list">{rows.map((row) => <article className="amount-row" key={row.id}><div><strong>{row.role} · {row.displayName}</strong><small>{row.publicId}</small></div><div className="actions"><Button disabled={action.busy} onClick={() => void action.run(async key => { await post(`/admin/role-enrollments/${row.id}/review`, { approve: true, expectedVersion: row.version }, key); resource.reload(); })}>Approve</Button><Button variant="secondary" disabled={action.busy} onClick={() => void action.run(async key => { await post(`/admin/role-enrollments/${row.id}/review`, { approve: false, expectedVersion: row.version }, key); resource.reload(); })}>Reject</Button></div></article>)}</div>{action.error && <Notice error>{action.error}</Notice>}</Section>}</Resource></>;
}

type EnrollmentRow = { id: string; role: string; status: string; displayName: string; publicId: string; version: number };

export function AdminDashboard() {
  const resource = useResource<AdminHome>("/admin/home");
  const operations = useResource<OperationsHome>("/admin/operations/home");
  const deposits = useResource<{ status: string }[]>("/admin/deposit-requests");
  return <div className="admin-page"><PageHeader title="Dashboard" />
    <Resource resource={operations}>{o => <Section title="Needs attention"><div className="admin-dashboard-links">
      <Link to="/admin/role-enrollments"><span>Pending profile approvals</span><strong>{count(o.pendingReviews)}</strong></Link>
      <Link to="/admin/payouts"><span>Pending payouts</span><strong>{count(o.pendingCreatorPayouts + o.pendingCustomerPayouts)}</strong></Link>
      <Link to="/admin/wallets"><span>Pending deposits</span><strong>{deposits.data ? count(deposits.data.filter(row => row.status === "Pending").length) : "—"}</strong></Link>
    </div></Section>}</Resource>
    <Resource resource={resource}>{d => <Section title="Workspaces"><div className="admin-dashboard-links">
      <Link to="/admin/businesses"><span>Businesses</span><strong>{count(d.businesses)}</strong></Link>
      <Link to="/admin/creators"><span>Creators</span><strong>{count(d.creators)}</strong></Link>
      <Link to="/admin/customers"><span>Customers</span><strong>{operations.data ? count(operations.data.customers) : "—"}</strong></Link>
      <Link to="/admin/campaigns"><span>Active Campaigns</span><strong>{count(d.activeCampaigns)}</strong></Link>
    </div></Section>}</Resource>
  </div>;
}
export function AdminCampaigns() {
  const { user } = useSession();
  if (user?.role === "OperationsAdmin") return <OperationsCampaigns />;
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
      <PageHeader title="Campaigns" />
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
              (!filters.type || promotionTypeCode(r.type) === filters.type) &&
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
                      View &amp; Sale
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
  const { user } = useSession();
  if (user?.role === "OperationsAdmin") return <OperationsCampaigns />;
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
                <Section title="Campaign history">
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
  const { user } = useSession();
  if (user?.role === "OperationsAdmin") return <OperationsBusinesses />;
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
  const { user } = useSession();
  if (user?.role === "OperationsAdmin") return <OperationsCreators />;
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
export function AdminActivity() {
  const resource = useResource<Activity[]>("/admin/notifications");
  return (
    <>
      <PageHeader
        eyebrow="Operational visibility"
        title="Notifications"
        description="Recent platform notifications."
      />
      <Resource resource={resource}>
        {(items) => (
          <Section
            title="Latest platform activity"
          >
            <ActivityList items={items} />
          </Section>
        )}
      </Resource>
    </>
  );
}
