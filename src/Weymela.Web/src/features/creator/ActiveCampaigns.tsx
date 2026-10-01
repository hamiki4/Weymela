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
import {
  campaignType,
  count,
  date,
  isViewAndSale,
} from "../../ui/format";

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
        title="My Promotions"
        description="Your Promotion content, verified activity and Creator earnings—all in one place."
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
                  <FundsGrid values={[["Verified Views", r.verifiedViews], ["Your Earnings", r.viewEarnings + r.saleCommissionEarnings]]} />
                  <p className="fine-print">
                    {count(r.verifiedViews)} verified views ·{" "}
                    {r.remainingDays != null ? `${r.remainingDays} days left` : r.participationId ? "Ended" : "Not live yet"}
                  </p>
                  <ActionLink to={`/creator/promotions/${r.budgetId}`} secondary>
                    Open Promotion
                  </ActionLink>
                </article>
              ))}
            </div>
          ) : (
            <Section title="Your Promotions">
              <Empty
                title="No Promotions yet"
                message="Request to join a specific Promotion. After Business approval, your content workspace appears here."
                action={
                  <ActionLink to="/creator/discover">
                    Discover opportunities
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
  const maySubmit = !row.participationId &&
    (row.contentReviewStatus == null || row.contentReviewStatus === "ChangesRequested") &&
    !["Completed", "Cancelled", "Rejected"].includes(row.status);
  const mayGoLive = !row.participationId && row.contentReviewStatus === "Approved" && row.status === "ReadyToGoLive";
  return (
    <Section title="Promotion content" description={row.contentStatus}>
      {maySubmit ? (
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
              setMessage("Content submitted. The Business must approve this revision before you go live.");
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
              label="Promotion content reference"
              help="Use the public content ID. Business review is required before Go Live."
            >
              <input
                value={content}
                onChange={(e) => setContent(e.target.value)}
                pattern="[a-zA-Z0-9_\-]+"
                maxLength={100}
                required
              />
            </Field>
            {row.contentFeedback && <Notice>{row.contentFeedback}</Notice>}
            <Button type="submit" disabled={action.busy || !content}>
              {row.contentReviewStatus === "ChangesRequested" ? "Submit Revised Content" : "Submit Content for Review"}
            </Button>
          </fieldset>
        </form>
      ) : mayGoLive ? (
        <div className="creator-review-ready">
          <Notice>Content approved by the Business. You decide when to go live.</Notice>
          <Button disabled={action.busy} onClick={() => void action.run(async (key) => {
            await post(`/creator/creator-budgets/${row.budgetId}/go-live`, {}, key);
              setMessage(`Promotion is live. Your ${row.promotionLiveDurationDays}-day window has started.`);
            reload();
          })}>{action.busy ? "Going live…" : "Go Live"}</Button>
        </div>
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
          {row.participationId && row.remainingDays != null && <Button
            disabled={
              action.busy ||
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
          </Button>}
        </div>
      )}
      {row.contentReviewStatus === "UnderReview" && <Notice>Content is under Business review. Go Live is not available yet.</Notice>}
      {row.contentReviewStatus === "Rejected" && <Notice>This content revision was rejected and cannot go live.</Notice>}
      {row.participationId && row.remainingDays != null && <p className="creator-live-days">{row.remainingDays} days left</p>}
      {row.participationId && row.remainingDays == null && <p className="creator-live-days">Ended</p>}
      {action.error && <Notice error>{action.error}</Notice>}
      {message && <Notice>{message}</Notice>}
      {row.status === "FundingRequired" && (
        <Notice>
          Your participation needs Business attention before additional rewards can be recorded.
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
      <Link className="back-link" to="/creator/promotions">
        ← My Promotions
      </Link>
      <Resource resource={resource}>
        {(rows) => {
          const r = rows.find((row) => row.budgetId === id);
          if (!r)
            return (
              <Section title="Promotion unavailable">
                <Empty
                  title="This Promotion isn’t in your workspace"
                  message="You can open only Promotions you have been approved to join."
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
                <Section title="Your Promotion progress">
                  <FundsGrid
                    values={[
                      ["Verified Views", r.verifiedViews],
                      ["View Earnings", r.viewEarnings],
                      ...(isViewAndSale(r.type)
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
                    Your earnings accumulate across your Creator work.
                  </p>
                </Section>
                <Section title="Verified activity">
                  <FundsGrid
                    values={[
                      ["Rewarded Views", r.rewardedViews],
                    ]}
                  />
                  <dl className="detail-list">
                    <div>
                      <dt>Start Date</dt>
                      <dd>{date(r.startUtc)}</dd>
                    </div>
                    <div>
                      <dt>Promotion duration</dt>
                      <dd>{r.promotionLiveDurationDays} days after you go live</dd>
                    </div>
                    {r.wentLiveAtUtc && (
                      <div>
                        <dt>Started</dt>
                        <dd>{date(r.wentLiveAtUtc)}</dd>
                      </div>
                    )}
                    {r.expiresAtUtc && (
                      <div>
                        <dt>Ends</dt>
                        <dd>{date(r.expiresAtUtc)}</dd>
                      </div>
                    )}
                    <div>
                      <dt>Live window</dt>
                      <dd>{r.remainingDays == null ? "Not live or ended" : `${r.remainingDays} days left`}</dd>
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
