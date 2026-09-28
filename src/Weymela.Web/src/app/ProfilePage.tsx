import { useEffect } from "react";
import { useLocation } from "react-router-dom";
import { useResource } from "../api/client";
import { CreatorProfile } from "../features/creator/CreatorProfile";
import { PageHeader, Resource } from "../ui/components";

interface OwnProfile {
  role: "Customer" | "Creator" | "Business";
  displayName: string;
  publicId: string;
  email: string | null;
  phone: string | null;
  status: string;
  businessType: string | null;
  region: string | null;
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
  const location = useLocation();
  useEffect(() => {
    if (profile.data?.role === "Creator" && location.hash === "#social-accounts")
      document.getElementById("social-accounts")?.scrollIntoView?.();
  }, [profile.data, location.hash]);
  return <div className="profile-page">
    <PageHeader title="Profile" />
    <Resource resource={profile}>{data => <>
      <div className="profile-identity">
        <span className="profile-avatar" aria-hidden="true">{data.displayName.trim().slice(0, 1).toUpperCase()}</span>
        <div><strong>{data.displayName}</strong><span>{data.role}{data.status ? ` · ${data.status}` : ""}</span></div>
      </div>
      {data.role === "Business" ? <>
        <InfoSection title="Business Information" rows={[["Business name", data.displayName], ["Public ID", data.publicId], ["Business type", data.businessType], ["Account status", data.status]]} />
        <InfoSection title="Contact Information" rows={[["Email", data.email], ["Phone", data.phone]]} />
        <InfoSection title="Address" rows={[["Region", data.region]]} />
      </> : <>
        <InfoSection title="Personal Information" rows={[["Email", data.email], ["Phone", data.phone], ["Public ID", data.publicId], ["Account status", data.status]]} />
        {data.role === "Creator" && <div id="social-accounts"><CreatorProfile sectionOnly /></div>}
      </>}
    </>}</Resource>
  </div>;
}
