import { count, safeExternal } from "../../ui/format";

export type CreatorPlatform = "TikTok" | "YouTube" | "Instagram" | "Facebook";

const platforms = new Set<string>(["TikTok", "YouTube", "Instagram", "Facebook"]);
export function isCreatorPlatform(platform: string): platform is CreatorPlatform {
  return platforms.has(platform);
}

export function creatorPublicHandle(platform: CreatorPlatform, url: string | undefined) {
  const safe = safeExternal(url);
  if (!safe) return null;
  const parsed = new URL(safe);
  const domains: Record<CreatorPlatform, string> = {
    TikTok: "tiktok.com", YouTube: "youtube.com", Instagram: "instagram.com", Facebook: "facebook.com",
  };
  if (parsed.hostname !== domains[platform] && parsed.hostname !== `www.${domains[platform]}` && parsed.hostname !== `m.${domains[platform]}`) return null;
  const segment = parsed.pathname.split("/").filter(Boolean)[0] ?? "";
  if (platform === "TikTok" || platform === "YouTube") return /^@[a-zA-Z0-9._-]+$/.test(segment) ? segment : null;
  if (!/^[a-zA-Z][a-zA-Z0-9._-]*$/.test(segment) || ["profile.php", "pages", "reel", "reels", "stories", "watch"].includes(segment.toLowerCase())) return null;
  return `@${segment}`;
}

export function CreatorPlatformIcon({ platform }: { platform: CreatorPlatform }) {
  return <span className={`creator-platform-icon creator-platform-${platform.toLowerCase()}`} aria-hidden="true">
    {platform === "TikTok" && <svg viewBox="0 0 24 24"><path d="M13 3v12.2a4.2 4.2 0 1 1-4.2-4.2M13 3c.5 3.2 2.3 5 5.4 5.1" strokeLinecap="round" strokeLinejoin="round" /></svg>}
    {platform === "YouTube" && <svg viewBox="0 0 24 24"><path d="m9 6 9 6-9 6z" /></svg>}
    {platform === "Instagram" && <svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="5" /><circle cx="12" cy="12" r="4" /><circle cx="17.5" cy="6.5" r=".8" fill="currentColor" stroke="none" /></svg>}
    {platform === "Facebook" && <span>f</span>}
  </span>;
}

export function PlatformRequirements({ slots, label = "Platform requirements", showIcons = true }: {
  slots: { platform: string; capacity?: number; minimumAudience?: number | null }[];
  label?: string;
  showIcons?: boolean;
}) {
  const configured = slots.filter(slot => (slot.capacity ?? 0) > 0);
  return configured.length ? <div className="creator-platform-requirements" aria-label={label}>
    {configured.map(slot => {
      const audienceLabel = slot.platform === "YouTube" ? "subscribers" : "followers";
      const minimum = slot.minimumAudience && slot.minimumAudience > 0
        ? `${count(slot.minimumAudience)} ${audienceLabel} minimum`
        : "No minimum";
      return <span key={slot.platform}>
        {showIcons && (isCreatorPlatform(slot.platform) ? <CreatorPlatformIcon platform={slot.platform} /> : <strong>{slot.platform}</strong>)}
        <span><strong>{slot.platform}</strong><small>{minimum}</small></span>
      </span>;
    })}
  </div> : null;
}
