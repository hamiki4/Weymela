import { useState } from "react";
import { Link } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  BusinessHome,
  BusinessPricing,
  CampaignRow,
  UgcCard,
  UgcPricing,
  Wallet,
} from "../../api/types";
import {
  ActionLink,
  Badge,
  Button,
  Currency,
  DataTable,
  Empty,
  Field,
  FundsGrid,
  Metric,
  MoneyInput,
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
  daysLeft,
  isViewOnly,
} from "../../ui/format";
import { Icon } from "../../ui/Icon";
import { useSession } from "../../app/Session";
import { DepositSubmission } from "./DepositSubmission";
import { PromotionContentReviewQueue } from "./PromotionContentReviewQueue";

export function WalletMetrics({ wallet }: { wallet: Wallet }) {
  return (
    <div className="metric-grid fund-metrics">
      <Metric
        label="Total Balance"
        value={amount(wallet.totalBalance)}
        note="Your advertising funds"
        icon="wallet"
        emphasis
      />
      <Metric
        label="Available Balance"
        value={amount(wallet.available)}
        note="Ready for your next Promotion"
        icon="plus"
      />
      <Metric
        label="Reserved Balance"
        value={amount(wallet.reserved)}
        note="Committed to Promotions"
        icon="lock"
      />
    </div>
  );
}
export function BusinessDashboard() {
  const resource = useResource<BusinessHome>("/business/home");
  const ugc = useResource<UgcCard[]>("/business/ugc");
  return (
    <Resource resource={resource}>
      {(data) => (
        <>
          <PageHeader
            eyebrow={data.business.displayName}
            title="Business Home"
            description="Manage Promotions, UGC, checkout and your Business funds."
            action={
              <ActionLink to="/business/campaigns/new" icon="plus">
                New Promotion
              </ActionLink>
            }
          />
          <WalletMetrics wallet={data.wallet} />
          <div className="section-kicker">
            <h2>Business operations</h2>
            <Currency />
          </div>
          <div className="quick-grid">
            <Link className="quick-card" to="/business/campaigns">
              <Icon name="campaign" />
              <span className="quick-value">{count(data.activeCampaigns)}</span>
              <strong>Active Promotions</strong>
            </Link>
            <Link className="quick-card" to="/business/requests">
              <Icon name="people" />
              <span className="quick-value">{count(data.creatorRequests)}</span>
              <strong>Creator Requests</strong>
            </Link>
            <Resource resource={ugc}>
              {(rows) => (
                <Link className="quick-card" to="/business/ugc">
                  <Icon name="sparkle" />
                  <span className="quick-value">
                    {count(rows.filter((row) => row.status === "Open").length)}
                  </span>
                  <strong>Open UGC</strong>
                </Link>
              )}
            </Resource>
            <Link className="quick-card" to="/business/campaigns">
              <Icon name="chart" />
              <span className="quick-value">{count(data.confirmedSales)}</span>
              <strong>Confirmed Sales</strong>
            </Link>
            <Link className="quick-card" to="/business/wallet">
              <Icon name="wallet" />
              <strong>Wallet</strong>
              <span className="quick-card-arrow"><Icon name="arrow" /></span>
            </Link>
            <Link className="quick-card pricing-home-card" to="/business/pricing">
              <Icon name="settings" />
              <strong>Pricing</strong>
              <Icon name="arrow" />
            </Link>
          </div>
          <Section title="Quick actions" className="quick-actions-section">
            <div className="actions quick-actions">
              <ActionLink to="/business/campaigns/new" icon="plus">
                New Promotion
              </ActionLink>
              <ActionLink to="/business/ugc" secondary icon="sparkle">
                Create UGC
              </ActionLink>
              <ActionLink to="/checkout" secondary icon="qr">
                Checkout / Scan QR
              </ActionLink>
              <ActionLink to="/business/cashiers" secondary icon="people">
                Cashier Management
              </ActionLink>
              <ActionLink to="/business/wallet" secondary icon="wallet">
                Add Funds
              </ActionLink>
            </div>
          </Section>
          <div className="content-grid section-kicker-space">
            <Section
              title="Recent fund activity"
              action={
                <Link className="text-link" to="/business/wallet">
                  Your wallet <Icon name="arrow" size={16} />
                </Link>
              }
            >
              {data.wallet.history.length ? (
                data.wallet.history.slice(0, 4).map((item) => (
                  <div className="amount-row" key={item.id}>
                    <div>
                      <strong>{item.label}</strong>
                      <small>{date(item.atUtc)}{item.reason ? ` · Reason: ${item.reason}` : ""}</small>
                    </div>
                    <strong>{amount(item.amount)}</strong>
                  </div>
                ))
              ) : (
                <Empty
                  title="Your first Promotion starts here"
                  message="Add any positive amount, then reserve a budget when you’re ready."
                />
              )}
            </Section>
            <div className="overview-highlight">
              <h2>Create a Promotion</h2>
              <ActionLink to="/business/campaigns/new" secondary icon="plus">
                Create Promotion
              </ActionLink>
            </div>
          </div>
        </>
      )}
    </Resource>
  );
}
export function BusinessWallet() {
  const resource = useResource<Wallet>("/business/wallet");
  const [value, setValue] = useState("");
  const [success, setSuccess] = useState(false);
  const action = useAction();
  const { user } = useSession();
  return (
    <>
      <PageHeader
        eyebrow="Advertising Funds"
        title="Your wallet"
        description="Add funds when it suits your Business. Reserve them only when you fund a Campaign."
      />
      <Resource resource={resource}>
        {(wallet) => (
          <>
            <WalletMetrics wallet={wallet} />
            <div className="two-column">
              <Section
                title="Add Funds"
                description="Any positive amount. Choose what works for your Business."
              >
                {!user?.developmentMode ? <DepositSubmission /> : <form
                  onSubmit={(event) => {
                    event.preventDefault();
                    void action.run(async (key) => {
                      await post(
                        "/business/wallet/deposits",
                        {
                          amount: Number(value),
                          expectedVersion: wallet.version,
                        },
                        key,
                      );
                      setSuccess(true);
                      setValue("");
                      resource.reload();
                    });
                  }}
                >
                  <fieldset disabled={action.busy || !user?.developmentMode}>
                    <Field
                      label="Amount"
                      help="Enter the amount you want to add."
                    >
                      <MoneyInput
                        value={value}
                        onChange={(e) => {
                          setValue(e.target.value);
                          setSuccess(false);
                        }}
                      />
                    </Field>
                    {user?.developmentMode && (
                      <p className="fine-print">
                        Development funding only. This records test funds; no
                        payment is taken.
                      </p>
                    )}
                    {!user?.developmentMode && (
                      <Notice>
                        Deposit confirmation is not connected yet.
                      </Notice>
                    )}
                    {action.error && <Notice error>{action.error}</Notice>}
                    {success && (
                      <Notice>
                        Funds added. Your saved wallet balance is shown above.
                      </Notice>
                    )}
                    <Button
                      type="submit"
                      icon="plus"
                      disabled={action.busy || !value}
                    >
                      {action.busy ? "Adding funds…" : "Add Funds"}
                    </Button>
                  </fieldset>
                </form>}
              </Section>
              <Section title="How your funds work">
                <div className="pricing-note">
                  <strong>Available</strong>
                  <small>Funds you can reserve for a new Campaign.</small>
                </div>
                <div className="pricing-note">
                  <strong>Reserved</strong>
                  <small>
                    Committed Campaign funds. Each Creator uses only their own
                    Creator Budget.
                  </small>
                </div>
                <div className="pricing-note">
                  <strong>Campaign activity</strong>
                  <small>
                    Verified views and sales use reserved funds. Unused Creator
                    Budget stays within its Campaign.
                  </small>
                </div>
              </Section>
            </div>
            <Section title="Wallet history" action={<Currency />}>
              <DataTable
                rows={wallet.history}
                rowKey={(r) => r.id}
                label="Wallet history"
                columns={[
                  { label: "Activity", cell: (r) => <>{r.label}{r.reason && <small>Reason: {r.reason}</small>}</> },
                  {
                    label: "Amount",
                    cell: (r) => amount(r.amount),
                    numeric: true,
                  },
                  { label: "Date", cell: (r) => date(r.atUtc) },
                  {
                    label: "Reference",
                    cell: (r) => (
                      <details>
                        <summary>Reference</summary>
                        <code>{r.reference}</code>
                      </details>
                    ),
                  },
                ]}
                card={(r) => (
                  <>
                    <div className="card-head">
                      <strong>{r.label}</strong>
                      <strong>{amount(r.amount)}</strong>
                    </div>
                    <p className="fine-print">{date(r.atUtc)}{r.reason ? ` · Reason: ${r.reason}` : ""}</p>
                    <details>
                      <summary>Reference</summary>
                      <code>{r.reference}</code>
                    </details>
                  </>
                )}
                empty={
                  <Empty
                    title="No fund activity yet"
                    message="Your deposits and Campaign spending will appear here."
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
export function BusinessPricingPage() {
  const resource = useResource<BusinessPricing>("/business/pricing");
  const ugcResource = useResource<UgcPricing>("/business/ugc-pricing");
  return (
    <>
      <PageHeader
        eyebrow="Know your costs"
        title="Pricing"
        description="See current rates and how promotion costs are calculated."
      />
      <Resource resource={resource}>
        {(pricing) => (
          <Resource resource={ugcResource}>
            {(ugcPricing) => (
              <Section
                title="A simple cost for verified activity"
                description="Existing Promotions keep their saved pricing."
                action={<Currency />}
              >
                <div
                  className="pricing-card-grid"
                  role="region"
                  aria-label="Business pricing options"
                >
                  {pricing.rows.map((row) => (
                    <article className="pricing-card" key={row.type}>
                      <div className="pricing-card-heading">
                        <span className="pricing-card-kicker">Promotion type</span>
                        <h3>{campaignType(row.type)}</h3>
                      </div>
                      <dl className="pricing-card-details">
                        <div>
                          <dt>Verified views</dt>
                          <dd>{count(row.views)}</dd>
                        </div>
                        <div>
                          <dt>Business funds</dt>
                          <dd>{amount(row.businessPays)}</dd>
                        </div>
                        <div>
                          <dt>{isViewOnly(row.type) ? "Sale" : "Sale cost"}</dt>
                          <dd>
                            {isViewOnly(row.type)
                              ? "Not included"
                              : `${amount(row.saleCostPercent)}% per verified sale`}
                          </dd>
                        </div>
                      </dl>
                      {row.minimumCampaignBudget !== null && (
                        <p className="pricing-card-note">
                          Minimum Promotion Budget: {amount(row.minimumCampaignBudget)}
                        </p>
                      )}
                    </article>
                  ))}
                  <article className="pricing-card pricing-card-business-ugc">
                    <div className="pricing-card-heading">
                      <span className="pricing-card-kicker">UGC funding</span>
                      <h3>UGC</h3>
                    </div>
                    <dl className="pricing-card-details">
                      <div>
                        <dt>Starting Creator Payment</dt>
                        <dd>{amount(ugcPricing.minimumCreatorPayment)}</dd>
                      </div>
                      <div>
                        <dt>Platform Fee</dt>
                        <dd>{amount(ugcPricing.platformFeePercent)}%</dd>
                      </div>
                      {ugcPricing.minimumUgcBudget !== null && (
                        <div>
                          <dt>Minimum UGC Budget</dt>
                          <dd>{amount(ugcPricing.minimumUgcBudget)}</dd>
                        </div>
                      )}
                      {ugcPricing.customerOfferPlatformSalePercent !== null && (
                        <div>
                          <dt>UGC + Sale Platform Fee</dt>
                          <dd>{amount(ugcPricing.customerOfferPlatformSalePercent)}%</dd>
                        </div>
                      )}
                    </dl>
                    <p className="pricing-card-note">
                      For UGC + Sale, you choose the Customer Discount and fund the Customer Offer budget.
                    </p>
                  </article>
                </div>
                <p className="fine-print section-kicker-space">
                  Effective {date(pricing.effectiveFromUtc)}. You control your
                  Promotion Budget; Weymela sets activity pricing.
                </p>
                <p className="fine-print">
                  Promotion live duration: {pricing.promotionLiveDurationDays} days · Set by Weymela for new Promotions.
                </p>
              </Section>
            )}
          </Resource>
        )}
      </Resource>
    </>
  );
}
export function CampaignTable({
  campaigns,
  admin = false,
}: {
  campaigns: CampaignRow[];
  admin?: boolean;
}) {
  const base = admin ? "/admin" : "/business";
  const columns = [
    ...(admin
      ? [{ label: "Business", cell: (r: CampaignRow) => r.business }]
      : []),
    {
      label: "Campaign",
      cell: (r: CampaignRow) => (
        <div className="campaign-name">
          <Link to={`${base}/campaigns/${r.id}`}>
            <strong>{r.title}</strong>
          </Link>
          <small>{campaignType(r.type)}</small>
          {!admin && (
            <div className="row-links">
              <Link to={`${base}/campaigns/${r.id}?tab=applicants`}>
                View Applicants
              </Link>
              <Link to={`${base}/campaigns/${r.id}?tab=budgets`}>
                Manage Creator Budgets
              </Link>
            </div>
          )}
        </div>
      ),
    },
    {
      label: "Campaign Budget",
      cell: (r: CampaignRow) => amount(r.campaignBudget),
      numeric: true,
    },
    {
      label: admin ? "Assigned" : "Assigned to Creators",
      cell: (r: CampaignRow) => amount(r.assignedToCreators),
      numeric: true,
    },
    ...(!admin
      ? [
          {
            label: "Available Campaign Budget",
            cell: (r: CampaignRow) => amount(r.availableCampaignBudget),
            numeric: true,
          },
        ]
      : []),
    { label: "Used", cell: (r: CampaignRow) => amount(r.used), numeric: true },
    {
      label: "Remaining",
      cell: (r: CampaignRow) => amount(r.remaining),
      numeric: true,
    },
    {
      label: "Creators",
      cell: (r: CampaignRow) => r.creatorCount,
      numeric: true,
    },
    {
      label: "Live Duration",
      cell: (r: CampaignRow) => `${r.promotionLiveDurationDays} days`,
    },
    { label: "Start", cell: (r: CampaignRow) => date(r.startUtc) },
    {
      label: admin ? "End" : "Days Left",
      cell: (r: CampaignRow) => (admin ? date(r.endUtc) : daysLeft(r.endUtc)),
    },
    { label: "Status", cell: (r: CampaignRow) => <Badge status={r.status} /> },
  ];
  return (
    <DataTable
      rows={campaigns}
      columns={columns}
      rowKey={(r) => r.id}
      label={admin ? "Admin Campaigns" : "Business Campaigns"}
      card={(r) => (
        <>
          <div className="card-head">
            <div>
              {admin && <p className="subheading">{r.business}</p>}
              <h3>
                <Link to={`${base}/campaigns/${r.id}`}>{r.title}</Link>
              </h3>
              <small className="muted">{campaignType(r.type)}</small>
            </div>
            <Badge status={r.status} />
          </div>
          <FundsGrid
            values={[
              ["Campaign Budget", r.campaignBudget],
              ["Assigned to Creators", r.assignedToCreators],
              ["Available Campaign Budget", r.availableCampaignBudget],
              ["Used", r.used],
              ["Remaining", r.remaining],
              ["Creators", r.creatorCount],
            ]}
          />
          <div className="meta-row">
            <span>
              <Icon name="calendar" />
              {date(r.startUtc)}
            </span>
            <span>{r.promotionLiveDurationDays} days live after Creator goes live</span>
            <span>
              {admin
                ? `Ends ${date(r.endUtc)}`
                : `${daysLeft(r.endUtc)} days left`}
            </span>
          </div>
          <div className="actions">
            <ActionLink to={`${base}/campaigns/${r.id}`} secondary>
              View Campaign
            </ActionLink>
            {!admin && (
              <>
                <Link
                  className="text-link"
                  to={`${base}/campaigns/${r.id}?tab=applicants`}
                >
                  View Applicants
                </Link>
                <Link
                  className="text-link"
                  to={`${base}/campaigns/${r.id}?tab=budgets`}
                >
                  Manage Creator Budgets
                </Link>
              </>
            )}
          </div>
        </>
      )}
      empty={
        <Empty
          title="No Campaigns to show"
          message={
            admin
              ? "Try adjusting your filters, or check back when a Business creates a Campaign."
              : "Create a Campaign and choose a budget to start working with Creators."
          }
          action={
            !admin && (
              <ActionLink to="/business/campaigns/new" icon="plus">
                Create Campaign
              </ActionLink>
            )
          }
        />
      }
    />
  );
}
export function BusinessCampaigns() {
  const resource = useResource<CampaignRow[]>("/business/campaigns");
  return (
    <>
      <PageHeader
        eyebrow="Your stories, in motion"
        title="Campaigns"
        description="Keep your Campaigns, Creator Budgets and performance in one place."
        action={
          <ActionLink to="/business/campaigns/new" icon="plus">
            Create Campaign
          </ActionLink>
        }
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Active Campaigns and drafts" action={<Currency />}>
            <CampaignTable campaigns={rows} />
          </Section>
        )}
      </Resource>
      <PromotionContentReviewQueue />
    </>
  );
}
export function BusinessRequests() {
  const resource = useResource<CampaignRow[]>("/business/campaigns");
  return (
    <>
      <PageHeader
        eyebrow="Find your collaborators"
        title="Creator Requests"
        description="Review applicants within each Campaign, then approve a Creator with their own budget."
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Choose a Campaign">
            <div className="card-stack campaign-grid">
              {rows
                .filter((r) => ["Active", "Published"].includes(r.status))
                .map((row) => (
                  <article className="campaign-card" key={row.id}>
                    <Badge status={row.status} />
                    <h3>{row.title}</h3>
                    <p>{campaignType(row.type)}</p>
                    <FundsGrid
                      values={[
                        [
                          "Available Campaign Budget",
                          row.availableCampaignBudget,
                        ],
                        ["Creators", row.creatorCount],
                      ]}
                    />
                    <div className="actions">
                      <ActionLink
                        to={`/business/campaigns/${row.id}?tab=applicants`}
                        secondary
                      >
                        View Applicants
                      </ActionLink>
                    </div>
                  </article>
                ))}
            </div>
            {!rows.some((r) => ["Active", "Published"].includes(r.status)) && (
              <Empty
                title="No Creator requests yet"
                message="Publish a funded Campaign so eligible Creators can request to join."
              />
            )}
          </Section>
        )}
      </Resource>
    </>
  );
}
