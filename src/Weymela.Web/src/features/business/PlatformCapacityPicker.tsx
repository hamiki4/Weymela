import { CreatorPlatformIcon } from "../creator/CreatorPlatformIcon";

const PLATFORMS = ["TikTok", "Instagram", "YouTube", "Facebook"] as const;

export type PlatformCapacity = {
  platform: string;
  capacity: number;
  minimumAudience?: number | null;
};

export function PlatformCapacityPicker({
  value,
  onChange,
  title = "Creators / platforms",
}: {
  value: PlatformCapacity[];
  onChange: (next: PlatformCapacity[]) => void;
  title?: string;
}) {
  const set = (platform: string, capacity: number) =>
    onChange(
      capacity <= 0
        ? value.filter((item) => item.platform !== platform)
        : value.some((item) => item.platform === platform)
          ? value.map((item) =>
              item.platform === platform ? { ...item, capacity } : item,
            )
          : [...value, { platform, capacity, minimumAudience: null }],
    );

  const setMinimum = (platform: string, text: string) =>
    onChange(
      value.map((item) =>
        item.platform === platform
          ? {
              ...item,
              minimumAudience: text === "" ? null : Number(text),
            }
          : item,
      ),
    );

  return (
    <fieldset className="platform-capacity-picker wide">
      <legend>{title}</legend>

      {PLATFORMS.map((platform) => {
        const item = value.find((row) => row.platform === platform);
        const capacity = item?.capacity ?? 0;
        const minimumAudience = item?.minimumAudience ?? null;
        const audienceLabel =
          platform === "YouTube"
            ? "Minimum subscribers"
            : "Minimum followers";

        return (
          <div className="platform-capacity-row" key={platform}>
            <div className="platform-capacity-header">
              <span className="platform-capacity-name">
                <CreatorPlatformIcon platform={platform} />
                {platform}
              </span>

              <div className="platform-capacity-controls">
                <button
                  type="button"
                  aria-label={`Remove ${platform} Creator slot`}
                  disabled={capacity === 0}
                  onClick={() => set(platform, capacity - 1)}
                >
                  −
                </button>

                <output aria-label={`${platform} Creator slots`}>
                  {capacity}
                </output>

                <button
                  type="button"
                  aria-label={`Add ${platform} Creator slot`}
                  disabled={capacity >= 100}
                  onClick={() => set(platform, capacity + 1)}
                >
                  +
                </button>
              </div>
            </div>

            {capacity > 0 && (
              <div className="platform-capacity-details">
                <label className="platform-audience-control">
                  <span>{audienceLabel}</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    value={minimumAudience ?? ""}
                    placeholder="0 = No minimum"
                    onChange={(event) =>
                      setMinimum(platform, event.target.value)
                    }
                  />
                </label>
              </div>
            )}
          </div>
        );
      })}
    </fieldset>
  );
}
