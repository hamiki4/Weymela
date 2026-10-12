import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type {
  FinancialSettings as Settings,
  SettingsWorkspace,
  UgcSettings,
  ViewPriceInput,
  ReceivingDestination,
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

type PriceDraft = { [K in keyof ViewPriceInput]: number | null };
type UgcDraft = { [K in keyof UgcSettings]: number | null };
type SettingsDraft = Omit<Settings, "viewOnly" | "viewPlusCommission" | "ugc" | "creatorCommissionPercent" | "customerCashbackPercent" | "platformPercent"> & {
  viewOnly: PriceDraft | null; viewPlusCommission: PriceDraft | null; ugc: UgcDraft | null;
  creatorCommissionPercent: number | null; customerCashbackPercent: number | null; platformPercent: number | null;
};
const emptyPrice = (): PriceDraft => ({ viewsPerReward: null, businessPays: null, creatorEarns: null, platformKeeps: null, minimumCampaignBudget: null });
const numeric = (value: string) => value === "" ? null : Number(value);
const validAmount = (value: number | null, positive = true) => value !== null && Number.isFinite(value) && (positive ? value > 0 : value >= 0) && Math.abs(value * 100 - Math.round(value * 100)) < 0.000001;
const validPercent = (value: number | null) => value !== null && Number.isFinite(value) && value >= 0 && value <= 100 && Math.abs(value * 10000 - Math.round(value * 10000)) < 0.000001;

function SettingsForm({ data, reload }: { data: SettingsWorkspace; reload: () => void }) {
  const [settings, set] = useState<SettingsDraft>(() => data.current ? structuredClone(data.current) : {
    viewOnly: null, viewPlusCommission: null, ugc: null,
    creatorCommissionPercent: data.inactiveBaseline?.creatorCommissionPercent ?? null,
    customerCashbackPercent: data.inactiveBaseline?.customerCashbackPercent ?? null,
    platformPercent: data.inactiveBaseline?.platformPercent ?? null,
    creatorThreshold: null, customerThreshold: null, promotionLiveDurationDays: null,
    enforceAudienceRequirements: null, effectiveFromUtc: null,
  });
  const [scheduled, schedule] = useState(false);
  const [when, setWhen] = useState("");
  const action = useAction();
  const update = (key: "creatorCommissionPercent" | "customerCashbackPercent" | "platformPercent" | "creatorThreshold" | "customerThreshold" | "promotionLiveDurationDays", value: string) => set({ ...settings, [key]: numeric(value) });
  const validPrice = (price: PriceDraft | null) => price === null || (
    price.viewsPerReward !== null && Number.isInteger(price.viewsPerReward) && price.viewsPerReward >= 1
    && validAmount(price.businessPays) && validAmount(price.creatorEarns, false) && validAmount(price.platformKeeps, false)
    && Math.abs(price.businessPays! - price.creatorEarns! - price.platformKeeps!) < 0.000001
    && (price.minimumCampaignBudget === null || validAmount(price.minimumCampaignBudget)));
  const percentages = [settings.creatorCommissionPercent, settings.customerCashbackPercent, settings.platformPercent];
  const valid = percentages.every(validPercent) && percentages.reduce<number>((sum, value) => sum + (value ?? 0), 0) <= 100
    && validPrice(settings.viewOnly) && validPrice(settings.viewPlusCommission)
    && (settings.creatorThreshold === null || validAmount(settings.creatorThreshold))
    && (settings.customerThreshold === null || validAmount(settings.customerThreshold))
    && (settings.promotionLiveDurationDays === null || (Number.isInteger(settings.promotionLiveDurationDays) && settings.promotionLiveDurationDays >= 1 && settings.promotionLiveDurationDays <= 365))
    && (settings.ugc === null || (validAmount(settings.ugc.minimumCreatorPayment) && validPercent(settings.ugc.platformFeePercent)
      && (settings.ugc.minimumUgcBudget === null || validAmount(settings.ugc.minimumUgcBudget))
      && (settings.ugc.customerOfferPlatformSalePercent === null || validPercent(settings.ugc.customerOfferPlatformSalePercent))));
  return <form className="settings-form" onSubmit={event => {
    event.preventDefault();
    if (!valid) return;
    void action.run(async key => {
      await post("/admin/financial-settings", { settings: {
        ...settings, effectiveFromUtc: scheduled ? new Date(when).toISOString() : null,
      }, expectedVersion: Math.max(data.version, ...data.versions.map(version => version.version)) }, key);
      reload();
    });
  }}><fieldset disabled={action.busy}>
    <Notice>Unset prices and payout minimums restrict only their dependent operations. Saving settings does not activate financial writes.</Notice>
    <Section title="Audience enforcement" description="Off allows Creators to apply regardless of audience size. On enforces Admin-verified audience requirements.">
      <div className="settings-audience"><Field label="Enforce audience requirements"><select value={settings.enforceAudienceRequirements === null ? "" : String(settings.enforceAudienceRequirements)}
        onChange={event => set({ ...settings, enforceAudienceRequirements: event.target.value === "" ? null : event.target.value === "true" })}>
        <option value="">Not configured</option><option value="false">Off</option><option value="true">On</option>
      </select></Field></div>
    </Section>
    <Section title="View Pricing" description="Business Pays must equal Creator Earns plus Platform Keeps." action={<Currency />}>
      <table className="settings-table"><caption className="sr-only">Admin View Pricing</caption>
        <thead><tr>{["Campaign Type", "Views per Reward", "Business Pays", "Creator Earns", "Platform Keeps", "Minimum Campaign Budget"].map(label => <th scope="col" key={label}>{label}</th>)}</tr></thead>
        <tbody>{(["viewOnly", "viewPlusCommission"] as const).map(mode => {
          const label = mode === "viewOnly" ? "View Only" : "View & Sale";
          return <tr key={mode}><th scope="row">{label}<label className="check-row"><input type="checkbox" aria-label={`Configure ${label} pricing`}
            checked={settings[mode] !== null} onChange={event => set({ ...settings, [mode]: event.target.checked ? emptyPrice() : null })} />Configure</label></th>
            {([["viewsPerReward", "Views per Reward"], ["businessPays", "Business Pays"], ["creatorEarns", "Creator Earns"], ["platformKeeps", "Platform Keeps"], ["minimumCampaignBudget", "Minimum Campaign Budget"]] as [keyof ViewPriceInput, string][]).map(([field, title]) => <td key={field} data-label={title}>
              <input aria-label={`${label} ${title}`} type="number" inputMode={field === "viewsPerReward" ? "numeric" : "decimal"}
                disabled={settings[mode] === null} required={settings[mode] !== null && field !== "minimumCampaignBudget"}
                min={field === "viewsPerReward" ? 1 : field === "businessPays" || field === "minimumCampaignBudget" ? 0.01 : 0}
                step={field === "viewsPerReward" ? 1 : 0.01} value={settings[mode]?.[field] ?? ""}
                onChange={event => set({ ...settings, [mode]: { ...settings[mode]!, [field]: numeric(event.target.value) } })}
                placeholder={field === "minimumCampaignBudget" ? "Optional" : "Not configured"} />
            </td>)}
          </tr>;
        })}</tbody>
      </table>
    </Section>
    <div className="two-column"><Section title="Verified Sale Split" description="Only View & Sale Promotions. Rates are saved with each Promotion.">
      <div className="settings-splits">{([["creatorCommissionPercent", "Creator Commission %"], ["customerCashbackPercent", "Customer Cashback %"], ["platformPercent", "Platform %"]] as const).map(([key, label]) => <Field key={key} label={label}>
        <input type="number" min="0" max="100" step="0.0001" required value={settings[key] ?? ""} onChange={event => update(key, event.target.value)} />
      </Field>)}</div>
      <p className="fine-print">Total Business sale cost: {percentages.every(validPercent) ? amount(percentages.reduce<number>((sum, value) => sum + value!, 0)) : "Not configured"}%</p>
    </Section>
    <Section title="Payout Thresholds" description="Minimum to cash out"><div className="form-grid">{([["creatorThreshold", "Creator"], ["customerThreshold", "Customer"]] as const).map(([key, label]) => <Field key={key} label={label}>
      <input type="number" min="0.01" step="0.01" value={settings[key] ?? ""} onChange={event => update(key, event.target.value)} placeholder="Not configured" />
    </Field>)}</div></Section>
    <Section title="Promotion live duration" description="Number of days each new Promotion can remain live after a Creator goes live.">
      <Field label="Promotion live duration"><input type="number" min="1" max="365" step="1" value={settings.promotionLiveDurationDays ?? ""} onChange={event => update("promotionLiveDurationDays", event.target.value)} placeholder="Not configured" /></Field>
      <p className="fine-print">Set by Weymela. Existing Promotions keep their saved duration.</p>
    </Section></div>
    <Section title="UGC Pricing" description="Applied to UGC creator payments and optional Customer Offers." action={<Currency />}>
      <label className="check-row"><input type="checkbox" aria-label="Configure UGC pricing" checked={settings.ugc !== null}
        onChange={event => set({ ...settings, ugc: event.target.checked ? { minimumCreatorPayment: null, platformFeePercent: null, minimumUgcBudget: null, customerOfferPlatformSalePercent: null } : null })} />Configure UGC pricing</label>
      <div className="form-grid">{([["minimumCreatorPayment", "Minimum Creator Payment"], ["platformFeePercent", "Platform Fee %"], ["minimumUgcBudget", "Minimum UGC Budget"], ["customerOfferPlatformSalePercent", "Customer Offer Platform Sale Fee %"]] as [keyof UgcSettings, string][]).map(([key, label]) => <Field key={key} label={label}>
        <input type="number" disabled={settings.ugc === null} required={settings.ugc !== null && (key === "minimumCreatorPayment" || key === "platformFeePercent")}
          min={key.includes("Percent") ? 0 : 0.01} max={key.includes("Percent") ? 100 : undefined} step={key.includes("Percent") ? 0.0001 : 0.01}
          value={settings.ugc?.[key] ?? ""} placeholder={key === "minimumUgcBudget" || key === "customerOfferPlatformSalePercent" ? "Optional" : "Not configured"}
          onChange={event => set({ ...settings, ugc: { ...settings.ugc!, [key]: numeric(event.target.value) } })} />
      </Field>)}</div>
    </Section>
    <Section title="Effective date" description={`Current effective version: ${data.version}. Existing Campaign pricing never changes retroactively.`}>
      <div className="settings-effective"><label><input type="radio" name="effective" checked={!scheduled} onChange={() => schedule(false)} />Effective Now</label>
        <label><input type="radio" name="effective" checked={scheduled} onChange={() => schedule(true)} />Schedule for Later</label>
        {scheduled && <Field label="Effective from"><input type="datetime-local" value={when} onChange={event => setWhen(event.target.value)} required /></Field>}
      </div>
      {!valid && <Notice error>Check the configured pricing, percentages and positive payout minimums before saving.</Notice>}
      {action.error && <Notice error>{action.error}</Notice>}
      <Button type="submit" disabled={action.busy || !valid}>{action.busy ? "Saving…" : "Save Financial Settings"}</Button>
    </Section>
  </fieldset></form>;
}
export function AdminFinancialSettings() {
  const resource = useResource<SettingsWorkspace>("/admin/financial-settings");
  const [saved, setSaved] = useState(false);
  return (
    <>
      <PageHeader
        title="Financial Settings"
        compact
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
            {data.inactiveBaseline && <Section title="Approved inactive baseline">
              {data.inactiveBaseline && <p>{`Customer cashback: ${data.inactiveBaseline.customerCashbackPercent}% · Creator commission: ${data.inactiveBaseline.creatorCommissionPercent}% · Platform: ${data.inactiveBaseline.platformPercent}%`}</p>}
              <Notice>No financial pricing version is effective yet.</Notice>
              <p>Configure approved values above. Unconfigured products remain unavailable for their dependent financial operations.</p>
            </Section>}
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
                      {v.settings.viewOnly?.viewsPerReward.toLocaleString() ?? "Not configured"}{" "}
                      views · Business{" "}
                      {amount(v.settings.viewOnly?.businessPays)} / Creator{" "}
                      {amount(v.settings.viewOnly?.creatorEarns)} / Platform{" "}
                      {amount(v.settings.viewOnly?.platformKeeps)}
                    </p>
                    <p>
                      View &amp; Sale:{" "}
                      {v.settings.viewPlusCommission?.viewsPerReward.toLocaleString() ?? "Not configured"}{" "}
                      views · Business{" "}
                      {amount(v.settings.viewPlusCommission?.businessPays)} /
                      Creator{" "}
                      {amount(v.settings.viewPlusCommission?.creatorEarns)} /
                      Platform{" "}
                      {amount(v.settings.viewPlusCommission?.platformKeeps)}
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
                    <p>Promotion live duration: {v.settings.promotionLiveDurationDays ?? "Not configured"} days</p>
                    <p>
                      Minimum Campaign Budgets: View Only{" "}
                      {v.settings.viewOnly?.minimumCampaignBudget ??
                        "Not configured"}{" "}
                      / View &amp; Sale{" "}
                      {v.settings.viewPlusCommission?.minimumCampaignBudget ??
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
      <ReceivingDestinations />
    </>
  );
}

function ReceivingDestinationRow({ row, reload }: { row: ReceivingDestination; reload: () => void }) {
  const [draft, setDraft] = useState(row);
  const action = useAction();
  return <tr><td data-label="Active"><input type="checkbox" aria-label={`${row.name} active`} checked={draft.isActive}
    onChange={event => setDraft({ ...draft, isActive: event.target.checked })} /></td>
    <td data-label="Method"><select aria-label={`${row.name} method`} value={draft.method}
      onChange={event => setDraft({ ...draft, method: event.target.value as ReceivingDestination["method"] })}><option>Telebirr</option><option>Bank</option></select></td>
    <td data-label="Name"><input aria-label={`${row.name} name`} required maxLength={100} value={draft.name}
      onChange={event => setDraft({ ...draft, name: event.target.value })} /></td>
    <td data-label="Account / phone"><input aria-label={`${row.name} account`} required maxLength={100} value={draft.accountReference}
      onChange={event => setDraft({ ...draft, accountReference: event.target.value })} /></td>
    <td data-label="Order"><input aria-label={`${row.name} order`} type="number" min="0" step="1" value={draft.sortOrder}
      onChange={event => setDraft({ ...draft, sortOrder: Number(event.target.value) })} /></td>
    <td data-label="Action"><Button type="button" variant="secondary" disabled={action.busy || !draft.name.trim() || !draft.accountReference.trim()}
      onClick={() => void action.run(async key => { await post(`/admin/receiving-destinations/${row.id}`, {
        method: draft.method, name: draft.name.trim(), accountReference: draft.accountReference.trim(), isActive: draft.isActive,
        sortOrder: draft.sortOrder, expectedVersion: row.version,
      }, key); reload(); })}>{action.busy ? "Saving…" : "Save"}</Button>{action.error && <Notice error>{action.error}</Notice>}</td></tr>;
}

function ReceivingDestinations() {
  const resource = useResource<ReceivingDestination[]>("/admin/receiving-destinations");
  return <Section title="Business deposit destinations" description="Active destinations appear in Business Add Funds.">
    <Resource resource={resource}>{rows => <table className="settings-table"><caption className="sr-only">Business deposit destinations</caption>
      <thead><tr><th scope="col">Active</th><th scope="col">Method</th><th scope="col">Name</th><th scope="col">Account / phone</th><th scope="col">Order</th><th scope="col">Action</th></tr></thead>
      <tbody>{rows.map(row => <ReceivingDestinationRow key={`${row.id}-${row.version}`} row={row} reload={resource.reload} />)}</tbody>
    </table>}</Resource>
  </Section>;
}
