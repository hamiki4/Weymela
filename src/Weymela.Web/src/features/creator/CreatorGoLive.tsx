import { useState } from "react";
import { Link } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type { PublicationStatus } from "../../api/types";
import { Button, Field, Notice } from "../../ui/components";
import { dateTime } from "../../ui/format";

export function CreatorGoLive({ path, publication, profileId, platform, reload }: {
  path: string; publication?: PublicationStatus | null; profileId?: string | null;
  platform?: string | null; reload: () => void;
}) {
  const action = useAction();
  const [link, setLink] = useState("");
  const [choice, setChoice] = useState("");
  const profiles = useResource<{ id: string; platform: string; profileUrl: string }[]>("/creator/social-accounts", !profileId);
  const selected = profiles.data?.find(row => row.id === choice);
  const current = publication && !["Failed", "Expired"].includes(publication.status) ? publication : null;
  if (publication?.wentLiveAtUtc) return <div className="creator-live-days">
    <strong>{publication.status !== "Verified" ? "Action Required" : publication.remainingDays === 0 ? "Completed" : "Live"}</strong>
    {publication.remainingDays != null && <p>{publication.remainingDays} days left</p>}
    <p><span>Starts</span> <span data-no-translate>{dateTime(publication.wentLiveAtUtc)}</span></p>
    {publication.expiresAtUtc && <p><span>Ends</span> <span data-no-translate>{dateTime(publication.expiresAtUtc)}</span></p>}
  </div>;
  if (current?.status === "VerificationPending" && current.goLiveRequestedAtUtc)
    return <Notice>Awaiting Review</Notice>;
  return <form className="contained-form" onSubmit={event => {
    event.preventDefault();
    void action.run(async key => {
      await post(path, current ? null : {
        provider: platform ?? selected?.platform,
        creatorSocialProfileId: profileId ?? selected?.id,
        externalContentId: link.trim(),
      }, key);
      reload();
    });
  }}>
    {!current && <>
      {!profileId && <Field label="Social profile"><select value={choice} onChange={event => setChoice(event.target.value)} required>
        <option value="">Choose your social profile</option>
        {profiles.data?.map(row => <option key={row.id} value={row.id}>{row.platform} · {row.profileUrl}</option>)}
      </select></Field>}
      {profiles.error && <Notice error>{profiles.error.message}</Notice>}
      {!profileId && profiles.data?.length === 0 && <Link to="/creator/profile">Add your social profile</Link>}
      <Field label="Video Link"><input type="url" inputMode="url" value={link} onChange={event => setLink(event.target.value)} maxLength={1000} placeholder="https://…" required /></Field>
    </>}
    {current?.watchUrl && <Field label="Video Link"><input type="url" value={current.watchUrl} readOnly /></Field>}
    <Button type="submit" disabled={action.busy || !current && (!link.trim() || !(profileId ?? selected?.id))}>
      {action.busy ? "Submitting…" : "Go Live"}
    </Button>
    {action.error && <Notice error>{action.error}</Notice>}
  </form>;
}
