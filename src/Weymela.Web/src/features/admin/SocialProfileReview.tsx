import { useState } from "react";
import { notifyAdminActionCountsChanged, post, useAction, useResource } from "../../api/client";
import type { AdminSocialProfileReview as Review } from "../../api/types";
import { Button, Empty, Field, PageHeader, Resource, Section } from "../../ui/components";
import { CreatorPlatformIcon, isCreatorPlatform, type CreatorPlatform } from "../creator/CreatorPlatformIcon";

export function AdminSocialProfileReview() {
  const resource = useResource<Review[]>("/admin/social-profiles/review");
  return <><PageHeader title="Social profile review" compact />
    <Resource resource={resource}>{rows => rows.length === 0 ? <Empty title="No social profiles to review" message="Creator social profiles will appear here." icon="people" /> :
      <div className="stack-list">{rows.map(row => <ReviewCard key={row.id} row={row} reload={resource.reload} />)}</div>}</Resource></>;
}

function ReviewCard({ row, reload }: { row: Review; reload: () => void }) {
  const action = useAction();
  const [verified, setVerified] = useState(String(row.verifiedAudience ?? row.selfReportedAudience));
  return <Section title={`${row.creatorName}${row.creatorNumber ? ` · ${row.creatorNumber}` : ""}`}>
    <div className="social-review-card">
      <div className="platform-capacity-name">{isCreatorPlatform(row.platform) && <CreatorPlatformIcon platform={row.platform as CreatorPlatform} />}<strong>{row.platform}</strong></div>
      <a href={row.profileUrl} target="_blank" rel="noreferrer">Open profile</a>
      <p>Creator reported: <strong>{row.selfReportedAudience.toLocaleString()}</strong> {row.platform === "YouTube" ? "subscribers" : "followers"}</p>
      <Field label="Admin verified audience"><input type="number" min="0" max="9000000000000000" step="1" value={verified} onChange={e => setVerified(e.target.value)} /></Field>
      <small>Status: {row.audienceVerificationSource === "AdminVerified" ? "Admin Verified" : row.verificationStatus}</small>
      <div className="actions"><Button disabled={action.busy || !/^\d+$/.test(verified)} onClick={() => void action.run(async key => { await post(`/admin/social-profiles/${row.id}/review`, { action: "approve", verifiedAudience: Number(verified) }, key); reload(); notifyAdminActionCountsChanged(); })}>Approve</Button>
        <Button variant="secondary" disabled={action.busy} onClick={() => void action.run(async key => { await post(`/admin/social-profiles/${row.id}/review`, { action: "reject", verifiedAudience: null }, key); reload(); notifyAdminActionCountsChanged(); })}>Reject</Button></div>
      {action.error && <p role="alert">{action.error}</p>}
    </div>
  </Section>;
}
