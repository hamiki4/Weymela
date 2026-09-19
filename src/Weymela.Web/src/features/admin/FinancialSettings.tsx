import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type {
  FinancialSettings as Settings,
  SettingsWorkspace,
  UgcSettings,
  ViewPriceInput,
} from "../../api/types";
import {
  Button,
  Currency,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, dateTime } from "../../ui/format";

function SettingsForm({
  data,
  reload,
}: {
  data: SettingsWorkspace;
  reload: () => void;
}) {
  const [settings, set] = useState<Settings>(structuredClone(data.current));
  const [scheduled, schedule] = useState(false);
  const [when, setWhen] = useState("");
  const action = useAction();
  const update = (key: keyof Settings, value: number) =>
    set({ ...settings, [key]: value });
  const updateUgc = (key: keyof UgcSettings, value: number | null) =>
    settings.ugc && set({ ...settings, ugc: { ...settings.ugc, [key]: value } });
  const price = (
    mode: "viewOnly" | "viewPlusCommission",
    field: keyof ViewPriceInput,
    value: string,
  ) =>
    set({
      ...settings,
      [mode]: {
        ...settings[mode],
        [field]:
          value === "" && field === "minimumCampaignBudget"
            ? null
            : Number(value),
      },
    });
  const validSplit = (p: ViewPriceInput) =>
    Math.abs(p.businessPays - p.creatorEarns - p.platformKeeps) < 0.000001;
  const valid =
    validSplit(settings.viewOnly) &&
    validSplit(settings.viewPlusCommission) &&
    settings.creatorCommissionPercent +
      settings.customerCashbackPercent +
      settings.platformPercent <=
      100 &&
    (!settings.ugc ||
      (settings.ugc.minimumCreatorPayment > 0 &&
        settings.ugc.platformFeePercent >= 0 &&
        settings.ugc.platformFeePercent <= 100 &&
        (settings.ugc.minimumUgcBudget === null || settings.ugc.minimumUgcBudget > 0) &&
        (settings.ugc.customerOfferPlatformSalePercent === null ||
          (settings.ugc.customerOfferPlatformSalePercent >= 0 && settings.ugc.customerOfferPlatformSalePercent <= 100))));
  return (
    <form
      className="settings-form"
      onSubmit={(e) => {
        e.preventDefault();
        void action.run(async (key) => {
          await post(
            "/admin/financial-settings",
            {
              settings: {
                ...settings,
                effectiveFromUtc: scheduled
                  ? new Date(when).toISOString()
                  : null,
              },
              expectedVersion: Math.max(
                data.version,
                ...data.versions.map((v) => v.version),
              ),
            },
            key,
          );
          reload();
        });
      }}
    >
      <fieldset disabled={action.busy}>
        <Section
          title="View Pricing"
          description="Business Pays must equal Creator Earns plus Platform Keeps."
          action={<Currency />}
        >
          <table className="settings-table">
            <caption className="sr-only">Admin View Pricing</caption>
            <thead>
              <tr>
                {[
                  "Campaign Type",
                  "Views per Reward",
                  "Business Pays",
                  "Creator Earns",
                  "Platform Keeps",
                  "Minimum Campaign Budget",
                ].map((label) => (
                  <th scope="col" key={label}>
                    {label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {(["viewOnly", "viewPlusCommission"] as const).map((mode) => (
                <tr key={mode}>
                  <th scope="row">
                    {mode === "viewOnly" ? "View Only" : "View & Sale"}
                  </th>
                  {(
                    [
                      ["viewsPerReward", "Views per Reward"],
                      ["businessPays", "Business Pays"],
                      ["creatorEarns", "Creator Earns"],
                      ["platformKeeps", "Platform Keeps"],
                      ["minimumCampaignBudget", "Minimum Campaign Budget"],
                    ] as [keyof ViewPriceInput, string][]
                  ).map(([field, label]) => (
                    <td key={field} data-label={label}>
                      <input
                        aria-label={`${mode === "viewOnly" ? "View Only" : "View & Sale"} ${label}`}
                        type="number"
                        inputMode={
                          field === "viewsPerReward" ? "numeric" : "decimal"
                        }
                        min={
                          field === "viewsPerReward" ||
                          field === "businessPays" ||
                          field === "minimumCampaignBudget"
                            ? field === "viewsPerReward"
                              ? 1
                              : 0.01
                            : 0
                        }
                        step={field === "viewsPerReward" ? 1 : 0.01}
                        required={field !== "minimumCampaignBudget"}
                        value={settings[mode][field] ?? ""}
                        onChange={(e) => price(mode, field, e.target.value)}
                        placeholder={
                          field === "minimumCampaignBudget"
                            ? "Optional"
                            : undefined
                        }
                      />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
        <div className="two-column">
          <Section
            title="Verified Sale Split"
            description="Only View & Sale Promotions. Rates are saved with each Promotion."
          >
            <div className="settings-splits">
              {(
                [
                  ["creatorCommissionPercent", "Creator Commission %"],
                  ["customerCashbackPercent", "Customer Cashback %"],
                  ["platformPercent", "Platform %"],
                ] as const
              ).map(([key, label]) => (
                <Field key={key} label={label}>
                  <input
                    type="number"
                    min="0"
                    max="100"
                    step="0.0001"
                    value={settings[key]}
                    onChange={(e) => update(key, Number(e.target.value))}
                    required
                  />
                </Field>
              ))}
            </div>
            <p className="fine-print">
              Total Business sale cost:{" "}
              {amount(
                settings.creatorCommissionPercent +
                  settings.customerCashbackPercent +
                  settings.platformPercent,
              )}
              %
            </p>
          </Section>
          <Section
            title="Payout Thresholds"
            description="Minimum to cash out"
          >
            <div className="form-grid">
              <Field label="Creator">
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={settings.creatorThreshold}
                  onChange={(e) =>
                    update("creatorThreshold", Number(e.target.value))
                  }
                  required
                />
              </Field>
              <Field label="Customer">
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={settings.customerThreshold}
                  onChange={(e) =>
                    update("customerThreshold", Number(e.target.value))
                  }
                  required
                />
              </Field>
            </div>
          </Section>
        </div>
        {settings.ugc && (
          <Section
            title="UGC Pricing"
            description="Applied to UGC creator payments and optional Customer Offers."
            action={<Currency />}
          >
            <div className="form-grid">
              <Field label="Minimum Creator Payment">
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={settings.ugc.minimumCreatorPayment}
                  onChange={(e) => updateUgc("minimumCreatorPayment", Number(e.target.value))}
                  required
                />
              </Field>
              <Field label="Platform Fee %">
                <input
                  type="number"
                  min="0"
                  max="100"
                  step="0.0001"
                  value={settings.ugc.platformFeePercent}
                  onChange={(e) => updateUgc("platformFeePercent", Number(e.target.value))}
                  required
                />
              </Field>
              <Field label="Minimum UGC Budget">
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={settings.ugc.minimumUgcBudget ?? ""}
                  onChange={(e) => updateUgc("minimumUgcBudget", e.target.value === "" ? null : Number(e.target.value))}
                  placeholder="Optional"
                />
              </Field>
              <Field label="Customer Offer Platform Sale Fee %">
                <input
                  type="number"
                  min="0"
                  max="100"
                  step="0.0001"
                  value={settings.ugc.customerOfferPlatformSalePercent ?? ""}
                  onChange={(e) => updateUgc("customerOfferPlatformSalePercent", e.target.value === "" ? null : Number(e.target.value))}
                  placeholder="Optional"
                />
              </Field>
            </div>
          </Section>
        )}
        <Section
          title="Effective date"
          description={`Current effective version: ${data.version}. Existing Campaign pricing never changes retroactively.`}
        >
          <div className="settings-effective">
            <label>
              <input
                type="radio"
                name="effective"
                checked={!scheduled}
                onChange={() => schedule(false)}
              />
              Effective Now
            </label>
            <label>
              <input
                type="radio"
                name="effective"
                checked={scheduled}
                onChange={() => schedule(true)}
              />
              Schedule for Later
            </label>
            {scheduled && (
              <Field label="Effective from">
                <input
                  type="datetime-local"
                  value={when}
                  onChange={(e) => setWhen(e.target.value)}
                  required
                />
              </Field>
            )}
          </div>
          {!valid && (
            <Notice error>
              Check the view pricing splits and total sale percentage before
              saving.
            </Notice>
          )}
          {action.error && <Notice error>{action.error}</Notice>}
          <Button type="submit" disabled={action.busy || !valid}>
            {action.busy ? "Saving…" : "Save Financial Settings"}
          </Button>
        </Section>
      </fieldset>
    </form>
  );
}
export function AdminFinancialSettings() {
  const resource = useResource<SettingsWorkspace>("/admin/financial-settings");
  const [saved, setSaved] = useState(false);
  return (
    <>
      <PageHeader
        eyebrow="Pricing authority"
        title="Financial Settings"
        description="Versioned pricing for views, verified sales and payouts. Clear today, preserved for every Campaign."
      />
      <Resource resource={resource}>
        {(data) => (
          <>
            {saved && (
              <Notice>
                Settings saved. The authoritative current values and saved
                versions are shown below.
              </Notice>
            )}
            <SettingsForm
              key={`${data.version}-${data.versions.length}`}
              data={data}
              reload={() => {
                setSaved(true);
                resource.reload();
              }}
            />
            <Section
              title="Saved versions"
              description="Open any version to review its stored values, including scheduled changes."
            >
              {data.versions.map((v) => (
                <details className="audit-detail" key={v.id}>
                  <summary>
                    Version {v.version} · {dateTime(v.effectiveFromUtc)} ·{" "}
                    {v.version === data.version
                      ? "Effective"
                      : new Date(v.effectiveFromUtc) > new Date()
                        ? "Scheduled"
                        : "Historical"}
                  </summary>
                  <div className="version-details">
                    <p>
                      View Only:{" "}
                      {v.settings.viewOnly.viewsPerReward.toLocaleString()}{" "}
                      views · Business{" "}
                      {amount(v.settings.viewOnly.businessPays)} / Creator{" "}
                      {amount(v.settings.viewOnly.creatorEarns)} / Platform{" "}
                      {amount(v.settings.viewOnly.platformKeeps)}
                    </p>
                    <p>
                      View &amp; Sale:{" "}
                      {v.settings.viewPlusCommission.viewsPerReward.toLocaleString()}{" "}
                      views · Business{" "}
                      {amount(v.settings.viewPlusCommission.businessPays)} /
                      Creator{" "}
                      {amount(v.settings.viewPlusCommission.creatorEarns)} /
                      Platform{" "}
                      {amount(v.settings.viewPlusCommission.platformKeeps)}
                    </p>
                    <p>
                      Sales: Creator {v.settings.creatorCommissionPercent}% /
                      Customer {v.settings.customerCashbackPercent}% / Platform{" "}
                      {v.settings.platformPercent}%
                    </p>
                    <p>
                      Thresholds: Creator {amount(v.settings.creatorThreshold)}{" "}
                      / Customer {amount(v.settings.customerThreshold)}
                    </p>
                    <p>
                      Minimum Campaign Budgets: View Only{" "}
                      {v.settings.viewOnly.minimumCampaignBudget ??
                        "Not configured"}{" "}
                      / View &amp; Sale{" "}
                      {v.settings.viewPlusCommission.minimumCampaignBudget ??
                        "Not configured"}
                    </p>
                    <code>
                      Changed by {v.changedBy} · {v.id}
                    </code>
                  </div>
                </details>
              ))}
            </Section>
          </>
        )}
      </Resource>
    </>
  );
}
