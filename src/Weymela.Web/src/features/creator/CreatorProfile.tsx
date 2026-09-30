import { useState } from "react";
import { post, request, useAction, useResource } from "../../api/client";
import { useSession } from "../../app/Session";
import { Button, Dialog, Field, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { safeExternal } from "../../ui/format";
import { CreatorPlatformIcon, creatorPublicHandle } from "./CreatorPlatformIcon";

interface SocialProfile { id: string; platform: string; profileUrl: string }
const platforms = ["TikTok", "YouTube", "Instagram", "Facebook"] as const;
type Platform = typeof platforms[number];

export function CreatorProfile({ sectionOnly = false }: { sectionOnly?: boolean }) {
  const { user } = useSession();
  const resource = useResource<SocialProfile[]>("/creator/social-accounts");
  const action = useAction();
  const [editing, setEditing] = useState<Platform | null>(null);
  const [url, setUrl] = useState("");
  const edit = (platform: Platform, existing?: SocialProfile) => {
    setUrl(existing?.profileUrl ?? "");
    setEditing(platform);
  };
  const done = () => { setEditing(null); resource.reload(); };
  return <>
    {!sectionOnly && <PageHeader title="Creator Profile" eyebrow={user?.displayName} />}
    <Section title="Social Profiles">
      <Resource resource={resource}>{profiles => <div className="creator-social-list">
        {platforms.map(platform => {
          const profile = profiles.find(row => row.platform === platform);
          const view = safeExternal(profile?.profileUrl);
          const handle = creatorPublicHandle(platform, view);
          return <div className="creator-social-row" key={platform}>
            <CreatorPlatformIcon platform={platform} />
            <div><strong>{platform}</strong><small className={handle ? "creator-social-handle" : ""}>{profile ? handle ?? "Profile added" : "Not added"}</small></div>
            <span className="creator-social-actions">
              {view && <a href={view} target="_blank" rel="noopener noreferrer" aria-label={`View ${platform} profile`}>View</a>}
              <button type="button" onClick={() => edit(platform, profile)}>{profile ? "Edit" : "Add profile"}</button>
            </span>
          </div>;
        })}
      </div>}</Resource>
    </Section>
    <Dialog title={editing ? `${editing} profile` : "Social profile"} open={editing !== null} onClose={() => setEditing(null)} className="creator-social-dialog">
      {editing && <form onSubmit={event => { event.preventDefault(); void action.run(async key => {
        await post(`/creator/social-profiles/${editing}`, { profileUrl: url }, key);
        done();
      }); }}>
        <Field label={`${editing} profile URL`}><input type="url" value={url} onChange={event => setUrl(event.target.value)} placeholder={editing === "TikTok" ? "https://www.tiktok.com/@username" : `https://www.${editing.toLowerCase()}.com/username`} maxLength={500} required autoComplete="url" /></Field>
        {action.error && <Notice error>{action.error}</Notice>}
        <div className="actions">
          <Button type="button" variant="secondary" onClick={() => setEditing(null)}>Cancel</Button>
          {resource.data?.some(row => row.platform === editing) && <Button type="button" variant="quiet" disabled={action.busy} onClick={() => void action.run(async () => {
            await request(`/creator/social-profiles/${editing}`, { method: "DELETE", headers: { "X-Weymela-Activity": "1" } });
            done();
          })}>Remove</Button>}
          <Button type="submit" disabled={action.busy || !url.trim()}>{action.busy ? "Saving…" : "Save"}</Button>
        </div>
      </form>}
    </Dialog>
  </>;
}
