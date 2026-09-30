import { CreatorPlatformIcon } from "../creator/CreatorPlatformIcon";

const PLATFORMS = ["TikTok", "Instagram", "YouTube", "Facebook"] as const;

export type PlatformCapacity = { platform: string; capacity: number };

export function PlatformCapacityPicker({ value, onChange, title = "Creators / platforms" }: {
  value: PlatformCapacity[];
  onChange: (next: PlatformCapacity[]) => void;
  title?: string;
}) {
  const set = (platform: string, capacity: number) => onChange(
    capacity <= 0
      ? value.filter((item) => item.platform !== platform)
      : value.some((item) => item.platform === platform)
        ? value.map((item) => item.platform === platform ? { platform, capacity } : item)
        : [...value, { platform, capacity }],
  );
  return <fieldset className="platform-capacity-picker wide">
    <legend>{title}</legend>
    {PLATFORMS.map((platform) => {
      const capacity = value.find((item) => item.platform === platform)?.capacity ?? 0;
      return <div className="platform-capacity-row" key={platform}>
        <span className="platform-capacity-name"><CreatorPlatformIcon platform={platform} />{platform}</span>
        <div className="platform-capacity-controls">
          <button type="button" aria-label={`Remove ${platform} Creator slot`} disabled={capacity === 0}
            onClick={() => set(platform, capacity - 1)}>−</button>
          <output aria-label={`${platform} Creator slots`}>{capacity}</output>
          <button type="button" aria-label={`Add ${platform} Creator slot`} disabled={capacity >= 100}
            onClick={() => set(platform, capacity + 1)}>+</button>
        </div>
      </div>;
    })}
  </fieldset>;
}
