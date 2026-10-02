import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
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
  isViewOnly,
} from "../../ui/format";
import { Icon } from "../../ui/Icon";
import { useSession } from "../../app/Session";
import { DepositSubmission } from "./DepositSubmission";
import { PromotionContentReviewQueue } from "./PromotionContentReviewQueue";
import { BusinessUgcCard } from "./UgcPages";

export function WalletMetrics({ wallet }: { wallet: Wallet }) {
  return (
    <div className="metric-grid fund-metrics">
      <Metric
        label="Total"
        value={`${amount(wallet.totalBalance)} ETB`}
        icon="wallet"
        emphasis
      />
      <Metric
        label="Available"
        value={`${amount(wallet.available)} ETB`}
        icon="plus"
      />
      <Metric
        label="Reserved"
        value={`${amount(wallet.reserved)} ETB`}
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
          <PageHeader title="Home" compact />
          <Link className="business-balance-summary" to="/business/wallet" aria-label="Available funds, view wallet">
            <div><span>Available</span><strong>{amount(data.wallet.available)}</strong></div>
            <div><span>Total</span><strong>{amount(data.wallet.totalBalance)}</strong></div>
            <div><span>Reserved</span><strong>{amount(data.wallet.reserved)}</strong></div>
          </Link>
          <div className="business-operation-list">
            <Link className="business-operation-row" to="/business/campaigns?filter=Active"><Icon name="campaign" /><span>Active Promotions</span><strong>{count(data.activeCampaigns)}</strong><Icon name="arrow" /></Link>
            <Link className="business-operation-row" to="/business/requests"><Icon name="people" /><span>Creator Requests</span><strong>{count(data.creatorRequests)}</strong><Icon name="arrow" /></Link>
            <Resource resource={ugc}>
              {(rows) => (
                <Link className="business-operation-row" to="/business/ugc?filter=Open"><Icon name="sparkle" /><span>Open UGC</span><strong>{count(rows.filter((row) => row.status === "Open").length)}</strong><Icon name="arrow" /></Link>
              )}
            </Resource>
          </div>
          <Section title="Quick actions" className="quick-actions-section">
            <div className="actions quick-actions">
              <ActionLink to="/business/campaigns/new" icon="plus">Create Promotion</ActionLink>
              <ActionLink to="/checkout" secondary icon="qr">Checkout / Scan QR</ActionLink>
              <ActionLink to="/business/transactions" secondary icon="document">Transactions</ActionLink>
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
      <PageHeader title="Wallet" compact />
      <Resource resource={resource}>
        {(wallet) => (
          <>
            <WalletMetrics wallet={wallet} />
            <div className="wallet-content">
              <Section
                title="Add Funds"
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
                    title="No activity yet."
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
                        <dt>Creator payment</dt>
                        <dd>You choose the UGC budget.</dd>
                      </div>
                      <div>
                        <dt>Weymela service fee</dt>
                        <dd>{amount(ugcPricing.platformFeePercent)}%</dd>
                      </div>
                      {ugcPricing.minimumUgcBudget !== null && (
                        <div>
                          <dt>Minimum UGC Budget</dt>
                          <dd>{amount(ugcPricing.minimumUgcBudget)}</dd>
                        </div>
                      )}
                    </dl>
                    <p className="pricing-card-note">The service fee is included in the Creator payment.</p>
                  </article>
                  <article className="pricing-card pricing-card-business-ugc">
                    <div className="pricing-card-heading"><span className="pricing-card-kicker">UGC customer offer</span><h3>UGC + Sale</h3></div>
                    <dl className="pricing-card-details">
                      <div><dt>Creator payment</dt><dd>You choose the UGC budget.</dd></div>
                      <div><dt>Customer offer</dt><dd>You choose the discount % and discount budget.</dd></div>
                      <div><dt>Weymela transaction fee</dt><dd>{ugcPricing.customerOfferPlatformSalePercent === null ? "Not available" : `${amount(ugcPricing.customerOfferPlatformSalePercent)}% per eligible purchase`}</dd></div>
                    </dl>
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
          <small>{campaignType(r.type)}</small>
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
            {admin && <span>{`Ends ${date(r.endUtc)}`}</span>}
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
          title={admin ? "No Campaigns to show" : "No promotions yet."}
        />
      }
    />
  );
}
export function BusinessCampaigns() {
  const campaigns = useResource<CampaignRow[]>("/business/campaigns");
  const ugc = useResource<UgcCard[]>("/business/ugc");
  const [params] = useSearchParams();
  const activeOnly = params.get("filter") === "Active";

  return (
    <>
      <PageHeader
        title="Promotions"
        compact
        action={
          <span className="business-create-action">
            <ActionLink to="/business/campaigns/new" icon="plus">
              Create Promotion
            </ActionLink>
          </span>
        }
      />

      <Resource resource={campaigns}>
        {(rows) => {
          const visible = rows.filter(
            (row) =>
              row.status !== "Draft" &&
              (!activeOnly || ["Active", "Published"].includes(row.status)),
          );

          return (
            <Section
              title={activeOnly ? "Active Views Promotions" : "Views Promotions"}
              action={
                activeOnly ? (
                  <Link className="text-link" to="/business/campaigns">
                    Show all
                  </Link>
                ) : (
                  <Currency />
                )
              }
            >
              <CampaignTable campaigns={visible} />
            </Section>
          );
        }}
      </Resource>

      <Resource resource={ugc}>
        {(rows) => {
          const visible = rows.filter(
            (item) =>
              item.status !== "Draft" &&
              (!activeOnly || item.status === "Open"),
          );

          return (
            <Section title={activeOnly ? "Open UGC Promotions" : "UGC Promotions"}>
              {visible.length ? (
                <div className="card-stack">
                  {visible.map((item) => (
                    <BusinessUgcCard
                      key={item.id}
                      item={item}
                      onChanged={() => ugc.reload()}
                    />
                  ))}
                </div>
              ) : (
                <Empty
                  title={
                    activeOnly
                      ? "No open UGC Promotions."
                      : "No UGC Promotions yet."
                  }
                />
              )}
            </Section>
          );
        }}
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
        description="Review requests for your promotions."
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
