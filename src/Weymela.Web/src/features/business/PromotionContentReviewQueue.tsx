import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type { PromotionContentReviewCard } from "../../api/types";
import { Badge, Button, Empty, Field, Notice, Resource, Section } from "../../ui/components";
import { dateTime } from "../../ui/format";
import { contentUrl } from "../creator/ActiveCampaigns";

function ReviewRow({ row, reload }: { row: PromotionContentReviewCard; reload: () => void }) {
  const action = useAction();
  const [feedback, setFeedback] = useState("");
  const review = (decision: "approve" | "requestchanges" | "reject") => void action.run(async (key) => {
    await post(`/business/promotion-content-submissions/${row.submissionId}/review`, {
      action: decision,
      feedback: feedback.trim() || null,
    }, key);
    reload();
  });
  const url = row.watchUrl;
  const legacyUrl = !url ? contentUrl(row.provider ?? null, row.contentReference ?? null) : undefined;
  return <article className="business-promotion-review-row">
    <div className="business-review-heading"><div><small>{row.creator} · {row.promotion}</small><h3>Revision {row.revisionNumber}</h3></div><Badge status={row.reviewStatus} /></div>
    <p>Submitted {dateTime(row.submittedAtUtc)}</p>
    {row.reviewMediaUrl ? <video className="private-review-media" controls preload="metadata" src={row.reviewMediaUrl}>Private review video</video>
      : url ? <a className="button secondary" href={url} target="_blank" rel="noopener noreferrer">Watch Sample on TikTok</a>
      : legacyUrl ? <a className="button secondary" href={legacyUrl} target="_blank" rel="noopener noreferrer">View submitted content</a>
      : <Notice error>Review unavailable: this TikTok video could not be opened from the submitted link.</Notice>}
    {row.feedback && <Notice>{row.feedback}</Notice>}
    {row.reviewStatus === "UnderReview" && <div className="business-review-actions">
      <Field label="Feedback (required for changes requested)"><textarea value={feedback} onChange={(event) => setFeedback(event.target.value)} maxLength={2000} rows={3} /></Field>
      <div className="actions"><Button disabled={action.busy || (!url && !legacyUrl && !row.reviewMediaUrl)} onClick={() => review("approve")}>Approve</Button><Button variant="secondary" disabled={action.busy || !feedback.trim()} onClick={() => review("requestchanges")}>Request Changes</Button><Button variant="quiet" disabled={action.busy} onClick={() => review("reject")}>Reject</Button></div>
    </div>}
    {action.error && <Notice error>{action.error}</Notice>}
  </article>;
}

export function PromotionContentReviewQueue({ promotionId }: { promotionId?: string }) {
  const resource = useResource<PromotionContentReviewCard[]>("/business/promotion-content-submissions");
  return <Section title="Video review" className="business-content-review">
    <Resource resource={resource}>{(rows) => {
      const visible = promotionId ? rows.filter(row => row.promotionId === promotionId) : rows;
      return visible.length ? <div className="business-promotion-review-list">{visible.map((row) => <ReviewRow key={row.submissionId} row={row} reload={resource.reload} />)}</div> : <Empty title="Nothing to review." />;
    }}</Resource>
  </Section>;
}
