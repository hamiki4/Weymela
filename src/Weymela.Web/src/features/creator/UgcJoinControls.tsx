import { useState } from "react";
import { post, useAction } from "../../api/client";
import type { UgcCard } from "../../api/types";
import { Button, Notice } from "../../ui/components";
import { CreatorPlatformIcon, creatorPublicHandle, isCreatorPlatform, PlatformOccupancy } from "./CreatorPlatformIcon";

export function UgcJoinControls({ item, onChanged }: { item: UgcCard; onChanged: () => void }) {
  const action = useAction();
  const [selectedId, setSelectedId] = useState("");
  const slots = (item.platformCapacities ?? []).filter(slot => slot.capacity > 0);
  const eligible = (item.eligibleSocialProfiles ?? []).filter(profile =>
    slots.some(slot => slot.platform === profile.platform && slot.available > 0));
  const selected = eligible.length === 1 ? eligible[0] : eligible.find(profile => profile.id === selectedId);
  const allFull = slots.length > 0 && slots.every(slot => slot.available <= 0);
  const arrangementReady = item.productProvided !== item.creatorMustPurchase;

  return <>
    <PlatformOccupancy slots={slots} />
    {!item.requestStatus && slots.length > 0 && eligible.length > 1 && <fieldset className="ugc-join-profiles">
      <legend>Choose verified social profile</legend>
      {eligible.map(profile => {
        const slot = slots.find(row => row.platform === profile.platform)!;
        return <label key={profile.id}>
          <input type="radio" name={`ugc-profile-${item.id}`} value={profile.id}
            checked={selectedId === profile.id} onChange={() => setSelectedId(profile.id)} />
          {isCreatorPlatform(profile.platform) && <CreatorPlatformIcon platform={profile.platform} />}
          <span><strong>{profile.platform}</strong><small>{isCreatorPlatform(profile.platform) ? creatorPublicHandle(profile.platform, profile.profileUrl) ?? "Verified profile" : "Verified profile"}</small></span>
          <span className="ugc-join-count">{slot.approved}/{slot.capacity}</span>
        </label>;
      })}
    </fieldset>}
    {!item.requestStatus && slots.length > 0 && eligible.length === 1 && <p className="ugc-join-single">
      {isCreatorPlatform(eligible[0].platform) && <CreatorPlatformIcon platform={eligible[0].platform} />}
      <span>Verified {eligible[0].platform} profile</span>
    </p>}
    {!item.requestStatus && allFull && <p className="creator-opportunity-unavailable">All Creator spots filled</p>}
    {!item.requestStatus && slots.length > 0 && !allFull && eligible.length === 0 &&
      <p className="creator-opportunity-unavailable">No available verified profile</p>}
    {!item.requestStatus && <Button disabled={action.busy || !arrangementReady || allFull || (slots.length > 0 && !selected)}
      onClick={() => void action.run(async key => {
        if (slots.length > 0 && !selected) return;
        const query = selected
          ? `?selectedPlatform=${encodeURIComponent(selected.platform)}&verifiedSocialProfileId=${encodeURIComponent(selected.id)}`
          : "";
        await post(`/creator/ugc/${item.id}/request${query}`, undefined, key);
        onChanged();
      })}>{action.busy ? "Requesting…" : "Request to Join"}</Button>}
    {action.error && <Notice error>{action.error}</Notice>}
  </>;
}
