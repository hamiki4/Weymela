import type { PublicationStatus } from "../api/types";
import { dateTime } from "../ui/format";

export function CampaignTime({ publication }: { publication?: PublicationStatus | null }) {
  if (!publication?.wentLiveAtUtc) return null;
  return <div className="campaign-live-window">
    <span><span>Starts</span><time data-no-translate dateTime={publication.wentLiveAtUtc}>{dateTime(publication.wentLiveAtUtc)}</time></span>
    {publication.expiresAtUtc && <span><span>Ends</span><time data-no-translate dateTime={publication.expiresAtUtc}>{dateTime(publication.expiresAtUtc)}</time></span>}
    {publication.remainingDays != null && <span><span>Days left</span><strong>{publication.remainingDays}</strong></span>}
  </div>;
}
