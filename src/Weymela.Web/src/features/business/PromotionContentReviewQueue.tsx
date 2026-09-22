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
  const url = contentUrl(row.provider, row.contentReference);
  return <article className="business-promotion-review-row">
    <div className="business-review-heading"><div><small>{row.creator} · {row.promotion}</small><h3>{row.provider} content · Revision {row.revisionNumber}</h3></div><Badge status={row.reviewStatus} /></div>
    <p>Submitted {dateTime(row.submittedAtUtc)}</p>
    {url ? <a href={url} target="_blank" rel="noreferrer">View submitted content</a> : <p className="fine-print">Content reference: {row.contentReference}</p>}
    {row.feedback && <Notice>{row.feedback}</Notice>}
    {row.reviewStatus === "UnderReview" && <div className="business-review-actions">
      <Field label="Feedback (required for changes requested)"><textarea value={feedback} onChange={(event) => setFeedback(event.target.value)} maxLength={2000} rows={3} /></Field>
      <div className="actions"><Button disabled={action.busy} onClick={() => review("approve")}>Approve</Button><Button variant="secondary" disabled={action.busy || !feedback.trim()} onClick={() => review("requestchanges")}>Request Changes</Button><Button variant="quiet" disabled={action.busy} onClick={() => review("reject")}>Reject</Button></div>
    </div>}
    {action.error && <Notice error>{action.error}</Notice>}
  </article>;
}

export function PromotionContentReviewQueue() {
  const resource = useResource<PromotionContentReviewCard[]>("/business/promotion-content-submissions");
  return <Section title="Promotion content review" description="Review content submitted by Creators for your Promotions. Approval only makes it ready for the Creator to go live.">
    <Resource resource={resource}>{(rows) => rows.length ? <div className="business-promotion-review-list">{rows.map((row) => <ReviewRow key={row.submissionId} row={row} reload={resource.reload} />)}</div> : <Empty title="No Promotion content to review" message="Creator submissions will appear here after you approve their request to join." />}</Resource>
  </Section>;
}
