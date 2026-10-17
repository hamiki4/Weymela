import { AdminSearch, AdminPager, useAdminSearch } from "./AdminSearch";
import { useResource } from "../../api/client";
import type { AdminPublicationReview as ReviewRow } from "../../api/types";
import { Badge, Empty, PageHeader, Resource, Section } from "../../ui/components";
import { PublicationEvidenceReview } from "../business/PublicationEvidenceReview";

function PublicationCard({ row, reload }: { row: ReviewRow; reload: () => void }) {
  return <article className="data-card admin-publication-card" data-notification-resource={row.id}>
    <div className="card-head"><div><small>{row.business} · {row.creator}</small><h3>{row.work}</h3></div><Badge status={row.status} label={row.wentLiveAtUtc ? "Live" : "Awaiting Review"} /></div>
    {row.approvedMediaUrl && (row.approvedMediaContentType?.startsWith("image/")
      ? <img className="private-review-media" src={row.approvedMediaUrl} alt="Approved video sample" />
      : <video className="private-review-media" src={row.approvedMediaUrl} controls preload="metadata" aria-label="Approved video sample" />)}
    {row.approvedWatchUrl && <a href={row.approvedWatchUrl} target="_blank" rel="noopener noreferrer">Approved sample</a>}
    {row.verificationMethod && <p>{row.verificationMethod === "Provider" ? "Provider verified" : "Evidence approved"}</p>}
    <PublicationEvidenceReview pending={row.status === "VerificationPending"} id={row.id} watchUrl={row.watchUrl} profileUrl={row.socialProfileUrl} needsViews={row.workType === "Promotion"} workspace="admin" reload={reload} />
  </article>;
}

export function AdminPublicationReview() {
  const search = useAdminSearch("/admin/publication-verifications");
  const resource = useResource<ReviewRow[]>(search.path);
  return <><PageHeader title="Publication Verification" compact /><AdminSearch value={search} statuses={["VerificationPending", "Verified"]} types={[]} />
    <Section title="Publication status"><Resource resource={resource}>{rows => rows.length
      ? <div className="card-stack">{rows.map(row => <PublicationCard key={row.id} row={row} reload={resource.reload} />)}</div>
      : <Empty title="No publications need attention." />}</Resource></Section>
    <AdminPager value={search} count={resource.data?.length ?? 0} loading={resource.loading} />
  </>;
}
