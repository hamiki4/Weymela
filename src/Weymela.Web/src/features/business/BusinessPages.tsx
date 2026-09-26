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
        icon="wallet"
        emphasis
      />
      <Metric
        label="Available Balance"
        value={amount(wallet.available)}
        icon="plus"
      />
      <Metric
        label="Reserved Balance"
        value={amount(wallet.reserved)}
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
            title="Home"
            action={
              <ActionLink to="/business/campaigns/new" icon="plus">
                Create Promotion
              </ActionLink>
            }
          />
          <div className="product-home-summary">
            <Link className="product-summary-row" to="/business/wallet"><span>Available funds</span><strong>{amount(data.wallet.available)}</strong></Link>
            <Link className="product-summary-row" to="/business/wallet"><span>Total balance</span><strong>{amount(data.wallet.totalBalance)}</strong></Link>
            <Link className="product-summary-row" to="/business/wallet"><span>Reserved funds</span><strong>{amount(data.wallet.reserved)}</strong></Link>
            <Link className="product-summary-row" to="/business/campaigns"><span>Active Promotions</span><strong>{count(data.activeCampaigns)}</strong></Link>
            <Link className="product-summary-row" to="/business/requests"><span>Creator Requests</span><strong>{count(data.creatorRequests)}</strong></Link>
            <Resource resource={ugc}>
              {(rows) => (
                <Link className="product-summary-row" to="/business/ugc"><span>Open UGC</span><strong>{count(rows.filter((row) => row.status === "Open").length)}</strong></Link>
              )}
            </Resource>
          </div>
          <Section title="Quick actions" className="quick-actions-section">
            <div className="actions quick-actions">
              <ActionLink to="/business/ugc" secondary icon="sparkle">Create UGC</ActionLink>
              <ActionLink to="/checkout" secondary icon="qr">Checkout / Scan QR</ActionLink>
              <ActionLink to="/business/cashiers" secondary icon="people">Cashier Management</ActionLink>
              <ActionLink to="/business/pricing" secondary icon="settings">Pricing</ActionLink>
            </div>
          </Section>
          <div className="section-kicker-space">
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
                  title="No fund activity"
                />
              )}
            </Section>
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
      label: admin ? "Campaign" : "Promotion",
      cell: (r: CampaignRow) => (
        <div className="campaign-name">
          <Link to={`${base}/campaigns/${r.id}`}>
            <strong>{r.title}</strong>
          </Link>
          <small>{campaignType(r.type)}{!admin && ` · ${daysLeft(r.endUtc)} days left`}</small>
          {!admin && <small>Assigned {amount(r.assignedToCreators)} · Available {amount(r.availableCampaignBudget)}</small>}
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
      label: admin ? "Campaign Budget" : "Budget",
      cell: (r: CampaignRow) => amount(r.campaignBudget),
      numeric: true,
    },
    ...(admin ? [{ label: "Assigned", cell: (r: CampaignRow) => amount(r.assignedToCreators), numeric: true }] : []),
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
    ...(admin ? [
      { label: "Live Duration", cell: (r: CampaignRow) => `${r.promotionLiveDurationDays} days` },
      { label: "Start", cell: (r: CampaignRow) => date(r.startUtc) },
      { label: "End", cell: (r: CampaignRow) => date(r.endUtc) },
    ] : []),
    { label: "Status", cell: (r: CampaignRow) => <Badge status={r.status} /> },
  ];
  return (
    <DataTable
      rows={campaigns}
      columns={columns}
      rowKey={(r) => r.id}
      label={admin ? "Admin Campaigns" : "Business Promotions"}
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
              [admin ? "Campaign Budget" : "Budget", r.campaignBudget],
              ["Available", r.availableCampaignBudget],
              ["Used", r.used],
              ["Remaining", r.remaining],
            ]}
          />
          <div className="meta-row">
            <span>{r.creatorCount} {r.creatorCount === 1 ? "Creator" : "Creators"} · {amount(r.assignedToCreators)} assigned</span>
            <span>
              {admin
                ? `Ends ${date(r.endUtc)}`
                : `${daysLeft(r.endUtc)} days left`}
            </span>
          </div>
          <div className="actions">
            <ActionLink to={`${base}/campaigns/${r.id}`} secondary>
              {admin ? "View Campaign" : "View Promotion"}
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
          title={admin ? "No Campaigns to show" : "No Promotions to show"}
          message={
            admin
              ? "Try adjusting your filters, or check back when a Business creates a Campaign."
              : "Create a Promotion and choose a budget to start working with Creators."
          }
          action={
            !admin && (
              <ActionLink to="/business/campaigns/new" icon="plus">
                Create Promotion
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
        title="Promotions"
        action={
          <ActionLink to="/business/campaigns/new" icon="plus">
            Create Promotion
          </ActionLink>
        }
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Active Promotions and drafts" action={<Currency />}>
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
        title="Creator Requests"
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
              <Empty title="No Creator requests yet" />
            )}
          </Section>
        )}
      </Resource>
    </>
  );
}
