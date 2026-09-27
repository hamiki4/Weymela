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
          return <div className="creator-social-row" key={platform}>
            <div><strong>{platform}</strong><small>{account ? "Profile on file" : "Not connected"}</small></div>
            {account && <span className="creator-social-status">{account.verificationStatus}</span>}
            {url && <a href={url} target="_blank" rel="noreferrer">View</a>}
          </div>;
        })}
      </div>}</Resource>
    </Section>
  </>;
}
