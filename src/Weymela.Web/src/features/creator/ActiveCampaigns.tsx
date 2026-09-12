import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type { CreatorCampaign } from "../../api/types";
import {
  ActionLink,
  Badge,
  Button,
  Currency,
  Empty,
  Field,
  FundsGrid,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { campaignType, count, date, daysLeft } from "../../ui/format";

export function contentUrl(provider: string | null, id: string | null) {
  if (!id || !/^[a-zA-Z0-9_-]+$/.test(id)) return undefined;
  return provider === "TikTok"
    ? `https://www.tiktok.com/@creator/video/${id}`
    : provider === "YouTube"
      ? `https://www.youtube.com/watch?v=${id}`
      : provider === "Instagram"
        ? `https://www.instagram.com/p/${id}/`
        : undefined;
}
export function CreatorActiveCampaigns() {
  const resource = useResource<CreatorCampaign[]>("/creator/campaigns");
  return (
    <>
      <PageHeader
        eyebrow="Your collaborations"
        title="Active Campaigns"
        description="Your content, verified activity and Creator Budgets—all in one place."
      />
      <Resource resource={resource}>
        {(rows) =>
          rows.length ? (
            <div className="card-stack campaign-grid">
              {rows.map((r) => (
                <article className="campaign-card" key={r.budgetId}>
                  <p className="card-eyebrow">{r.business.displayName}</p>
                  <div className="card-head">
                    <h3>{r.title}</h3>
                    <Badge status={r.status} />
                  </div>
                  <p>{campaignType(r.type)}</p>
                  <FundsGrid
                    values={[
                      ["Your Budget", r.yourBudget],
                      ["Budget Remaining", r.budgetRemaining],
                    ]}
                  />
                  <p className="fine-print">
                    ETB · {count(r.verifiedViews)} verified views ·{" "}
                    {daysLeft(r.endUtc)} days left
                  </p>
                  <ActionLink to={`/creator/campaigns/${r.budgetId}`} secondary>
                    Open Campaign
                  </ActionLink>
                </article>
              ))}
            </div>
          ) : (
            <Section title="Your Campaigns">
              <Empty
                title="No active Campaigns yet"
                message="Request to join a Campaign. After Business approval, your Creator Budget and content workspace appear here."
                action={
                  <ActionLink to="/creator/discover">
                    Discover Campaigns
                  </ActionLink>
                }
              />
            </Section>
          )
        }
      </Resource>
    </>
  );
}
function CreatorContent({
  row,
  reload,
}: {
  row: CreatorCampaign;
  reload: () => void;
}) {
  const [provider, setProvider] = useState("TikTok");
  const [content, setContent] = useState("");
  const [message, setMessage] = useState("");
  const action = useAction();
  const url = contentUrl(row.provider, row.externalContentId);
  return (
    <Section title="Video" description={row.contentStatus}>
      {!row.participationId &&
      !["Completed", "Cancelled"].includes(row.status) ? (
        <form
          className="contained-form"
          onSubmit={(e) => {
            e.preventDefault();
            void action.run(async (key) => {
              await post(
                `/creator/creator-budgets/${row.budgetId}/content`,
                { provider, externalContentId: content.trim() },
                key,
              );
              setMessage(
                "Content connected. Your verified starting views have been recorded.",
              );
              reload();
            });
          }}
        >
          <fieldset disabled={action.busy}>
            <Field label="Platform">
              <select
                value={provider}
                onChange={(e) => setProvider(e.target.value)}
              >
                <option>TikTok</option>
                <option>YouTube</option>
                <option>Instagram</option>
              </select>
            </Field>
            <Field
              label="Video reference"
              help="Use the video ID from your public content, not a private link."
            >
              <input
                value={content}
                onChange={(e) => setContent(e.target.value)}
                pattern="[a-zA-Z0-9_\-]+"
                maxLength={100}
                required
              />
            </Field>
            <p className="fine-print">
              Development uses a test verification provider. No live social
              account is contacted.
            </p>
            <Button type="submit" disabled={action.busy || !content}>
              Connect Content
            </Button>
          </fieldset>
        </form>
      ) : (
        <div className="actions">
          {url && (
            <a
              className="button secondary"
              href={url}
              target="_blank"
              rel="noreferrer"
            >
              Open {row.provider}
            </a>
          )}
          <Button
            disabled={
              action.busy ||
              !row.participationId ||
              ["Completed", "Cancelled", "Paused"].includes(row.status)
            }
            onClick={() =>
              void action.run(async (key) => {
                await post(
                  `/creator/participations/${row.participationId}/refresh`,
                  {},
                  key,
                );
                setMessage(
                  "Verified views refreshed. Complete eligible rewards have been recorded.",
                );
                reload();
              })
            }
          >
            {action.busy ? "Refreshing…" : "Refresh Views"}
          </Button>
        </div>
      )}
      {action.error && <Notice error>{action.error}</Notice>}
      {message && <Notice>{message}</Notice>}
      {row.status === "FundingRequired" && (
        <Notice>
          Your Creator Budget cannot cover the next activity reward. The
          Business can increase your budget from its Campaign funds.
        </Notice>
      )}
    </Section>
  );
}
export function CreatorActiveDetail() {
  const { id } = useParams();
  const resource = useResource<CreatorCampaign[]>("/creator/campaigns");
  return (
    <>
      <Link className="back-link" to="/creator/campaigns">
        ← Active Campaigns
      </Link>
      <Resource resource={resource}>
        {(rows) => {
          const r = rows.find((row) => row.budgetId === id);
          if (!r)
            return (
              <Section title="Campaign unavailable">
                <Empty
                  title="This Campaign isn’t in your workspace"
                  message="You can open only Campaigns you have been approved to join."
                />
              </Section>
            );
          return (
            <>
              <PageHeader
                eyebrow={r.business.displayName}
                title={r.title}
                description={campaignType(r.type)}
                action={<Badge status={r.status} />}
              />
              <div className="two-column">
                <Section title="Your Creator Budget" action={<Currency />}>
                  <FundsGrid
                    values={[
                      ["Your Budget", r.yourBudget],
                      ["Budget Remaining", r.budgetRemaining],
                      ["View Earnings", r.viewEarnings],
                      ...(r.type === "ViewPlusCommission"
                        ? [
                            [
                              "Sale Commission Earnings",
                              r.saleCommissionEarnings,
                            ] as [string, number],
                          ]
                        : []),
                    ]}
                  />
                  <p className="fine-print section-kicker-space">
                    Your earnings accumulate with earnings from all your
                    Campaigns.
                  </p>
                </Section>
                <Section title="Verified activity">
                  <FundsGrid
                    values={[
                      ["Verified Views", r.verifiedViews],
                      ["Rewarded Views", r.rewardedViews],
                    ]}
                  />
                  <dl className="detail-list">
                    <div>
                      <dt>Start Date</dt>
                      <dd>{date(r.startUtc)}</dd>
                    </div>
                    <div>
                      <dt>Days Left</dt>
                      <dd>{daysLeft(r.endUtc)}</dd>
                    </div>
                    <div>
                      <dt>Participation Status</dt>
                      <dd>
                        <Badge status={r.status} />
                      </dd>
                    </div>
                  </dl>
                </Section>
              </div>
              <CreatorContent
                key={r.budgetId}
                row={r}
                reload={resource.reload}
              />
            </>
          );
        }}
      </Resource>
    </>
  );
}
