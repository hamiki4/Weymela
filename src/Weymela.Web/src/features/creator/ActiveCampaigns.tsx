import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { post, postForm, useAction, useResource } from "../../api/client";
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
  amount,
  count,
  date,
  isViewAndSale,
  promotionTypeLabel,
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
                  <p>{promotionTypeLabel(r.type)}</p>
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
  const [content, setContent] = useState("");
  const [media, setMedia] = useState<File | null>(null);
  const [message, setMessage] = useState("");
  const action = useAction();
  const url = contentUrl(row.provider, row.externalContentId);
  const maySubmit = !row.participationId &&
    (row.contentReviewStatus == null || row.contentReviewStatus === "ChangesRequested") &&
    !["Completed", "Cancelled", "Rejected"].includes(row.status);
  const mayPublish = !row.participationId && row.contentReviewStatus === "Approved"
    && (!row.publication || ["Failed", "Expired"].includes(row.publication.status));
  const mayGoLive = !row.participationId && row.publication?.status === "Verified";
  return (
    <Section title="Your next step" description={row.contentStatus}>
      {maySubmit ? (
        <form
          className="contained-form"
          onSubmit={(e) => {
            e.preventDefault();
            void action.run(async (key) => {
              if (!media) return;
              const form = new FormData(); form.append("media", media);
              await postForm(`/creator/creator-budgets/${row.budgetId}/content/review`, form, key);
              setMedia(null);
              setMessage("Revision submitted for Business review.");
              reload();
            });
          }}
        >
          <fieldset disabled={action.busy}>
            <Field
              label="Private review video"
              help="Upload an MP4 review copy marked SAMPLE • WEYMELA REVIEW ONLY. Do not publish it yet."
            >
              <input
                type="file"
                accept="video/mp4"
                onChange={(e) => setMedia(e.target.files?.[0] ?? null)}
                required
              />
            </Field>
            {row.contentFeedback && <Notice>{row.contentFeedback}</Notice>}
            <Button type="submit" disabled={action.busy || !media}>
              {row.contentReviewStatus === "ChangesRequested" ? "Submit Revised Video" : "Submit for Review"}
            </Button>
          </fieldset>
        </form>
      ) : mayPublish ? (
        <form className="contained-form" onSubmit={(event) => {
          event.preventDefault();
          void action.run(async (key) => {
            if (!row.selectedSocialProfileId || !row.selectedPlatform) return;
            await post(`/creator/creator-budgets/${row.budgetId}/publication`, {
              provider: row.selectedPlatform,
              externalContentId: content.trim(),
              creatorSocialProfileId: row.selectedSocialProfileId,
            }, key);
            setMessage("Publication submitted for verification."); reload();
          });
        }}>
          <Notice>Video approved. Publish this approved revision on {row.selectedPlatform ?? "the selected platform"}, then enter the public post ID.</Notice>
          {row.selectedSocialProfileUrl && <p><a href={row.selectedSocialProfileUrl} target="_blank" rel="noopener noreferrer">Open selected social profile</a></p>}
          {row.selectedSocialProfileId && row.selectedPlatform ? <>
            <Field label="Public post ID"><input value={content} onChange={(event) => setContent(event.target.value)} pattern={row.selectedPlatform === "TikTok" ? "[0-9]+" : "[a-zA-Z0-9_-]+"} maxLength={100} required /></Field>
            <Button type="submit" disabled={action.busy || !content.trim()}>{action.busy ? "Checking…" : "Verify Publication"}</Button>
          </> : <Notice error>This work has no selected social profile. Contact the Business before publishing.</Notice>}
        </form>
      ) : mayGoLive ? (
        <div className="creator-review-ready">
          <Notice>{row.publication?.verificationLabel}. Go Live when you want the Customer offer and reward window to begin.</Notice>
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
      {row.reviewMediaUrl && !row.participationId && <video className="private-review-media" src={row.reviewMediaUrl} controls preload="metadata">Private review video</video>}
      {row.contentReviewStatus === "UnderReview" && <Notice>Content is under Business review. Go Live is not available yet.</Notice>}
      {row.contentReviewStatus === "Rejected" && <Notice>This content revision was rejected and cannot go live.</Notice>}
      {row.publication?.status === "VerificationPending" && <Notice>Publication verification is pending. Go Live remains unavailable.</Notice>}
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
                description={promotionTypeLabel(r.type)}
                action={<Badge status={r.status} />}
              />
              <Section title="Brief">
                {r.description && <p className="preserve-lines">{r.description}</p>}
                <dl className="detail-list">
                  {r.requirements && <div><dt>Requirements</dt><dd>{r.requirements}</dd></div>}
                  {r.location && <div><dt>Location</dt><dd>{r.location}</dd></div>}
                  {r.contentDueAtUtc && <div><dt>Deadline</dt><dd>{date(r.contentDueAtUtc)}</dd></div>}
                  <div><dt>Creator Budget</dt><dd>{amount(r.yourBudget)} ETB</dd></div>
                  {r.selectedPlatform && <div><dt>Platform</dt><dd>{r.selectedPlatform}</dd></div>}
                </dl>
              </Section>
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
                      <dt>Status</dt>
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
              <Section title="Activity">
                <ol className="collaboration-timeline">
                  <li>Business approved your request</li>
                  {r.contentRevisionNumber && <li>Revision {r.contentRevisionNumber} submitted</li>}
                  {r.contentReviewStatus === "ChangesRequested" && <li>Business requested changes</li>}
                  {r.contentReviewStatus === "Approved" && <li>Business approved revision {r.contentRevisionNumber}</li>}
                  {r.publication && <li>Publication {r.publication.verificationLabel.toLowerCase()}</li>}
                  {r.participationId && <li>Promotion went live</li>}
                </ol>
              </Section>
            </>
          );
        }}
      </Resource>
    </>
  );
}
