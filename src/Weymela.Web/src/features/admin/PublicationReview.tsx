import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type { AdminPublicationReview } from "../../api/types";
import { Badge, Button, Empty, Field, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { dateTime } from "../../ui/format";

function PublicationCard({ row, reload }: { row: AdminPublicationReview; reload: () => void }) {
  const action = useAction();
  const [evidence, setEvidence] = useState("");
  const [views, setViews] = useState("");
  const pending = row.status === "VerificationPending";
  const review = (decision: "verify" | "fail") => void action.run(async key => {
    await post(`/admin/publication-verifications/${row.id}/review`, {
      action: decision,
      evidenceReference: evidence.trim() || null,
      baselineViews: row.workType === "Promotion" && decision === "verify" ? Number(views) : null,
    }, key);
    reload();
  });
  return <article className="data-card admin-publication-card">
    <div className="card-head"><div><small>{row.workType} · {row.business}</small><h3>{row.work}</h3></div><Badge status={row.status} label={pending ? "Verification pending" : row.wentLiveAtUtc ? "Live" : "Verified"} /></div>
    <p><strong>{row.creator}</strong> · {row.provider}</p>
    <div className="actions"><a className="button secondary" href={row.socialProfileUrl} target="_blank" rel="noopener noreferrer">Creator profile</a><a className="button secondary" href={row.watchUrl} target="_blank" rel="noopener noreferrer">Watch public post</a></div>
    <small>{row.wentLiveAtUtc ? `Live since ${dateTime(row.wentLiveAtUtc)}` : `Requested ${dateTime(row.requestedAtUtc)}`}</small>
    {pending && <><Notice>Confirm the account, post, public access, and approved work before verifying.</Notice>
      <Field label="Verification note or evidence reference"><input value={evidence} onChange={event => setEvidence(event.target.value)} maxLength={500} /></Field>
      {row.workType === "Promotion" && <Field label="Observed public views"><input type="number" min="0" step="1" value={views} onChange={event => setViews(event.target.value)} /></Field>}</>}
    <div className="actions">{pending && <Button disabled={action.busy || !evidence.trim() || (row.workType === "Promotion" && views === "")} onClick={() => review("verify")}>Verify Manually</Button>}<Button variant="secondary" disabled={action.busy} onClick={() => review("fail")}>{pending ? "Mark Failed" : "Mark Unavailable"}</Button></div>
    {action.error && <Notice error>{action.error}</Notice>}
  </article>;
}

export function AdminPublicationReview() {
  const resource = useResource<AdminPublicationReview[]>("/admin/publication-verifications");
  return <><PageHeader title="Publication Verification" compact />
    <Section title="Publication status"><Resource resource={resource}>{rows => rows.length
      ? <div className="card-stack">{rows.map(row => <PublicationCard key={row.id} row={row} reload={resource.reload} />)}</div>
      : <Empty title="No publications need attention." />}</Resource></Section>
  </>;
}
