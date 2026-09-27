import { useResource } from "../../api/client";
import { useSession } from "../../app/Session";
import { PageHeader, Resource, Section } from "../../ui/components";
import { safeExternal } from "../../ui/format";

interface SocialAccount {
  id: string;
  platform: string;
  profileUrl: string;
  verificationStatus: string;
}

const platforms = ["TikTok", "YouTube", "Instagram", "Facebook"] as const;

function publicHandle(platform: string, safeUrl: string | undefined) {
  if (!safeUrl) return null;
  const url = new URL(safeUrl);
  const domains: Record<string, string> = {
    TikTok: "tiktok.com", YouTube: "youtube.com", Instagram: "instagram.com", Facebook: "facebook.com",
  };
  const domain = domains[platform];
  if (!domain || (url.hostname !== domain && !url.hostname.endsWith(`.${domain}`))) return null;
  const segment = url.pathname.split("/").filter(Boolean)[0] ?? "";
  if (platform === "TikTok" || platform === "YouTube") {
    return /^@[a-zA-Z0-9._-]+$/.test(segment) ? segment : null;
  }
  if (!/^[a-zA-Z][a-zA-Z0-9._-]*$/.test(segment) || ["profile.php", "pages", "reel", "reels", "stories", "watch"].includes(segment.toLowerCase())) return null;
  return `@${segment}`;
}

export function CreatorProfile() {
  const { user } = useSession();
  const resource = useResource<SocialAccount[]>("/creator/social-accounts");
  return <>
    <PageHeader title="Creator Profile" eyebrow={user?.displayName} />
    <Section title="Social Accounts">
      <Resource resource={resource}>{(accounts) => <div className="creator-social-list">
        {platforms.map((platform) => {
          const account = accounts.find((row) => row.platform === platform);
          const url = safeExternal(account?.profileUrl);
          const handle = publicHandle(platform, url);
          return <div className="creator-social-row" key={platform}>
            <div><strong>{platform}</strong>{handle && <small className="creator-social-handle">{handle}</small>}<small>{account ? "Profile on file" : "Not connected"}</small></div>
            {account && <span className="creator-social-status">{account.verificationStatus}</span>}
            {url && <a href={url} target="_blank" rel="noreferrer">View</a>}
          </div>;
        })}
      </div>}</Resource>
    </Section>
  </>;
}
