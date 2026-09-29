export type CreatorPlatform = "TikTok" | "YouTube" | "Instagram" | "Facebook";

export function CreatorPlatformIcon({ platform }: { platform: CreatorPlatform }) {
  return <span className={`creator-platform-icon creator-platform-${platform.toLowerCase()}`} aria-hidden="true">
    {platform === "TikTok" && <svg viewBox="0 0 24 24"><path d="M13 3v12.2a4.2 4.2 0 1 1-4.2-4.2M13 3c.5 3.2 2.3 5 5.4 5.1" strokeLinecap="round" strokeLinejoin="round" /></svg>}
    {platform === "YouTube" && <svg viewBox="0 0 24 24"><path d="m9 6 9 6-9 6z" /></svg>}
    {platform === "Instagram" && <svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="5" /><circle cx="12" cy="12" r="4" /><circle cx="17.5" cy="6.5" r=".8" fill="currentColor" stroke="none" /></svg>}
    {platform === "Facebook" && <span>f</span>}
  </span>;
}
