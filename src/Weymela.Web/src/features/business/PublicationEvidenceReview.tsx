import { useState } from "react";
import { post, useAction } from "../../api/client";
import { Button, Field, Notice } from "../../ui/components";

export function PublicationEvidenceReview({ id, watchUrl, profileUrl, needsViews, reload, workspace = "business", pending = true }: {
  id: string; watchUrl?: string | null; profileUrl?: string | null; needsViews: boolean;
  reload: () => void; workspace?: "business" | "admin"; pending?: boolean;
}) {
  const action = useAction();
  const [evidence, setEvidence] = useState("");
  const [checked, setChecked] = useState(false);
  const [views, setViews] = useState("");
  const review = (decision: string) => void action.run(async key => {
    await post(`/${workspace}/publication-verifications/${id}/review`, {
      action: decision, evidenceReference: evidence.trim() || null, evidenceChecked: checked,
      baselineViews: needsViews && views !== "" ? Number(views) : null,
    }, key);
    reload();
  });
  return <div className="business-review-actions" data-notification-resource={id}>
    <div className="actions">
      {watchUrl && <a className="button secondary" href={watchUrl} target="_blank" rel="noopener noreferrer">Watch public post</a>}
      {profileUrl && <a className="button secondary" href={profileUrl} target="_blank" rel="noopener noreferrer">Creator profile</a>}
    </div>
    {pending && <><Field label="Evidence reference"><input value={evidence} onChange={event => setEvidence(event.target.value)} maxLength={500} required /></Field>
    {needsViews && <Field label="Observed public views"><input type="number" min="0" step="1" value={views} onChange={event => setViews(event.target.value)} required /></Field>}
    <label><input type="checkbox" checked={checked} onChange={event => setChecked(event.target.checked)} /> I checked public access, Creator ownership and the approved video.</label>
    </>}
    <div className="actions">{pending && <Button disabled={action.busy || !evidence.trim() || !checked || needsViews && views === ""} onClick={() => review("verify")}>Accept Evidence</Button>}
      <Button variant="secondary" disabled={action.busy} onClick={() => review("fail")}>{pending ? "Reject" : "Mark Unavailable"}</Button></div>
    {action.error && <Notice error>{action.error}</Notice>}
  </div>;
}
