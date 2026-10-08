import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { postForm, request, useResource } from "../api/client";
import { CreatorProfile } from "../features/creator/CreatorProfile";
import { Button, CreatorAvatar, Notice, PageHeader, Resource } from "../ui/components";

interface OwnProfile {
  role: "Customer" | "Creator" | "Business";
  displayName: string;
  email: string | null;
  phone: string | null;
  status: string;
  businessType: string | null;
  region: string | null;
  creatorId: number | null;
  hasCreatorPhoto: boolean;
}

function InfoRows({ rows }: { rows: [string, string | null | undefined][] }) {
  const available = rows.filter(([, value]) => value?.trim());
  if (!available.length) return null;
  return <dl className="profile-info-rows">{available.map(([label, value]) =>
    <div className="profile-info-row" key={label}><dt>{label}</dt><dd>{value}</dd></div>,
  )}</dl>;
}

function InfoSection({ title, rows }: { title: string; rows: [string, string | null | undefined][] }) {
  if (!rows.some(([, value]) => value?.trim())) return null;
  return <section className="profile-section"><h2>{title}</h2><InfoRows rows={rows} /></section>;
}

export function ProfilePage() {
  const profile = useResource<OwnProfile>("/profile");
  const input = useRef<HTMLInputElement>(null);
  const [selected, setSelected] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [photoOverride, setPhotoOverride] = useState<boolean | null>(null);
  const [photoRevision, setPhotoRevision] = useState(0);
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);
  const choosePhoto = (file?: File) => {
    setError(""); setSuccess(""); setSelected(null);
    setPreview(null);
    if (!file) return;
    if (!["image/jpeg", "image/png"].includes(file.type) || file.size < 1 || file.size > 4 * 1024 * 1024) {
      setError("Choose a JPEG or PNG photo smaller than 4 MB."); return;
    }
    setSelected(file); setPreview(URL.createObjectURL(file));
  };
  const savePhoto = async () => {
    if (!selected || busy) return;
    setBusy(true); setError(""); setSuccess("");
    try {
      const form = new FormData(); form.append("photo", selected);
      await postForm("/creator/photo", form, crypto.randomUUID());
      setSelected(null); setPreview(null); setPhotoOverride(true); setPhotoRevision(value => value + 1);
      setSuccess("Photo updated."); profile.reload();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Upload failed."); }
    finally { setBusy(false); }
  };
  const removePhoto = async () => {
    if (busy) return;
    setBusy(true); setError(""); setSuccess("");
    try {
      await request("/creator/photo", { method: "DELETE", headers: { "X-Weymela-Activity": "1" } });
      setPhotoOverride(false); setPhotoRevision(value => value + 1); setSuccess("Photo removed."); profile.reload();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Photo could not be removed."); }
    finally { setBusy(false); }
  };
  const location = useLocation();
  useEffect(() => {
    if (profile.data?.role === "Creator" && ["#social-profiles", "#social-accounts"].includes(location.hash))
      document.getElementById("social-profiles")?.scrollIntoView?.();
  }, [profile.data, location.hash]);
  return <div className="profile-page">
    <PageHeader title="Profile" compact />
    <Resource resource={profile}>{data => <>
      <div className="profile-identity">
        {data.role === "Creator" ? <CreatorAvatar name={data.displayName} path={(photoOverride ?? data.hasCreatorPhoto) ? "/creator/photo" : undefined} large revision={photoRevision} />
          : <span className="profile-avatar" aria-hidden="true">{data.displayName.trim().slice(0, 1).toUpperCase()}</span>}
        <div><strong>{data.displayName}</strong><span>{data.role}{data.status ? ` · ${data.status}` : ""}</span>
          {data.role === "Creator" && data.creatorId != null && <span>Creator ID {data.creatorId}</span>}</div>
      </div>
      {data.role === "Creator" && <>
        <input ref={input} type="file" accept="image/jpeg,image/png" className="sr-only" aria-label="Choose Creator profile photo" onChange={event => { choosePhoto(event.target.files?.[0]); event.target.value = ""; }} />
        <div className="creator-photo-actions">
          <Button variant="secondary" disabled={busy} onClick={() => input.current?.click()}>{(photoOverride ?? data.hasCreatorPhoto) ? "Change photo" : "Add photo"}</Button>
          {(photoOverride ?? data.hasCreatorPhoto) && <Button variant="quiet" disabled={busy} onClick={() => void removePhoto()}>Remove photo</Button>}
        </div>
        {preview && <div className="creator-photo-preview"><img src={preview} alt="Selected profile photo preview" /><Button disabled={busy} onClick={() => void savePhoto()}>{busy ? "Uploading…" : "Save photo"}</Button><Button variant="quiet" disabled={busy} onClick={() => choosePhoto()}>Cancel</Button></div>}
        {error && <Notice error>{error}</Notice>}{success && <Notice>{success}</Notice>}
      </>}
      {data.role === "Business" ? <>
        <InfoSection title="Business Information" rows={[["Business name", data.displayName], ["Business type", data.businessType], ["Account status", data.status]]} />
        <InfoSection title="Contact Information" rows={[["Email", data.email], ["Phone", data.phone]]} />
        <InfoSection title="Address" rows={[["Region", data.region]]} />
      </> : <>
        <InfoSection title="Personal Information" rows={[["Email", data.email], ["Phone", data.phone], ["Account status", data.status]]} />
        {data.role === "Creator" && <div id="social-profiles"><CreatorProfile sectionOnly /></div>}
      </>}
    </>}</Resource>
  </div>;
}
