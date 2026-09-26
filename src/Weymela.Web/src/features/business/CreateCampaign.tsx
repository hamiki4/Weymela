import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type { BusinessPricing, CampaignTypeCode } from "../../api/types";
import {
  Button,
  Field,
  MoneyInput,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import {
  amount,
  campaignType,
  count,
  isViewAndSale,
  promotionTypeCode,
} from "../../ui/format";

const SOCIAL_PLATFORMS = ["TikTok", "Instagram", "YouTube", "Facebook"] as const;

export function CreateCampaign() {
  const pricing = useResource<BusinessPricing>("/business/pricing");
  const action = useAction();
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [type, setType] = useState<CampaignTypeCode>("ViewOnly");
  const [form, setForm] = useState({
    title: "",
    slogan: "",
    description: "",
    campaignBudget: "",
    requirements: "",
    category: "",
    region: "",
    minimumVerifiedFollowers: "",
    startUtc: "",
    endUtc: "",
  });
  const [platforms, setPlatforms] = useState<{ platform: string; capacity: number }[]>([]);
  const togglePlatform = (platform: string, selected: boolean) => setPlatforms((current) =>
    selected ? [...current, { platform, capacity: 1 }] : current.filter((row) => row.platform !== platform));
  const set = (name: keyof typeof form, value: string) =>
    setForm((current) => ({ ...current, [name]: value }));
  return (
    <>
      <PageHeader
        title="Create Promotion"
      />
      <Resource resource={pricing}>
        {(data) => {
          const price = data.rows.find(
            (row) => promotionTypeCode(row.type) === type,
          );
          if (!price)
            return <Notice error>Promotion pricing is unavailable.</Notice>;
          return (
            <div className="content-grid form-layout">
              <Section
                title={
                  [
                    "Promotion",
                    "Creators",
                    "Budget",
                  ][step]
                }
              >
                <ol className="stepper" aria-label="Campaign setup progress">
                  {["Promotion", "Creators", "Budget"].map((label, index) => (
                    <li
                      key={label}
                      aria-current={index === step ? "step" : undefined}
                    >
                      <span>{index + 1}</span>
                      {label}
                    </li>
                  ))}
                </ol>
                <form
                  onSubmit={(event) => {
                    event.preventDefault();
                    if (step < 2) {
                      setStep(step + 1);
                      return;
                    }
                    void action.run(async (key) => {
                      const result = await post<{ id: string }>(
                        "/business/campaigns",
                        {
                          ...form,
                          slogan: form.slogan.trim() || null,
                          platforms,
                          type,
                          campaignBudget: Number(form.campaignBudget),
                          minimumVerifiedFollowers:
                            form.minimumVerifiedFollowers
                              ? Number(form.minimumVerifiedFollowers)
                              : null,
                          startUtc: new Date(form.startUtc).toISOString(),
                          endUtc: new Date(form.endUtc).toISOString(),
                        },
                        key,
                      );
                      navigate(`/business/campaigns/${result.id}`);
                    });
                  }}
                >
                  <fieldset disabled={action.busy}>
                    {step === 0 && (
                      <div className="form-grid">
                        <Field label="Promotion title" wide>
                          <input
                            value={form.title}
                            onChange={(e) => set("title", e.target.value)}
                            maxLength={120}
                            required
                          />
                        </Field>
                        <Field label="Promotion slogan (optional)" wide>
                          <input value={form.slogan} onChange={(e) => set("slogan", e.target.value)} maxLength={160} />
                        </Field>
                        <Field
                          label="Description"
                          wide
                        >
                          <textarea
                            value={form.description}
                            onChange={(e) => set("description", e.target.value)}
                            maxLength={3000}
                            required
                            rows={4}
                          />
                        </Field>
                        <Field label="Promotion type" wide>
                          <select
                            value={type}
                            onChange={(e) =>
                              setType(e.target.value as CampaignTypeCode)
                            }
                          >
                            <option value="ViewOnly">View Only</option>
                            <option value="ViewPlusCommission">
                              View &amp; Sale
                            </option>
                          </select>
                        </Field>
                        <Field label="Start date">
                          <input
                            type="datetime-local"
                            value={form.startUtc}
                            onChange={(e) => set("startUtc", e.target.value)}
                            required
                          />
                        </Field>
                        <Field label="End date">
                          <input
                            type="datetime-local"
                            value={form.endUtc}
                            min={form.startUtc}
                            onChange={(e) => set("endUtc", e.target.value)}
                            required
                          />
                        </Field>
                      </div>
                    )}
                    {step === 1 && (
                      <div className="form-grid">
                        <Field
                          label="Requirements"
                          wide
                        >
                          <textarea
                            value={form.requirements}
                            onChange={(e) =>
                              set("requirements", e.target.value)
                            }
                            maxLength={2000}
                            rows={4}
                          />
                        </Field>
                        <Field
                          label="Creator category"
                        >
                          <input
                            value={form.category}
                            onChange={(e) => set("category", e.target.value)}
                            maxLength={80}
                            placeholder="e.g. Food"
                          />
                        </Field>
                        <Field
                          label="Region"
                        >
                          <input
                            value={form.region}
                            onChange={(e) => set("region", e.target.value)}
                            maxLength={80}
                            placeholder="e.g. Addis Ababa"
                          />
                        </Field>
                        <Field
                          label="Minimum verified followers"
                        >
                          <input
                            type="number"
                            min="0"
                            step="1"
                            value={form.minimumVerifiedFollowers}
                            onChange={(e) =>
                              set("minimumVerifiedFollowers", e.target.value)
                            }
                          />
                        </Field>
                        <div className="promotion-platform-field" role="group" aria-label="Social platforms">
                          <strong>Social platforms</strong>
                          <small>Optional. Approved Creators fill these slots.</small>
                          {SOCIAL_PLATFORMS.map((platform) => {
                            const selected = platforms.find((row) => row.platform === platform);
                            return <div className="promotion-platform-choice" key={platform}>
                              <label><input type="checkbox" checked={!!selected} onChange={(e) => togglePlatform(platform, e.target.checked)} />{platform}</label>
                              {selected && <label>Creator slots <input aria-label={`${platform} Creator slots`} type="number" min="1" max="100" required value={selected.capacity} onChange={(e) => setPlatforms((current) => current.map((row) => row.platform === platform ? { ...row, capacity: Number(e.target.value) } : row))} /></label>}
                            </div>;
                          })}
                        </div>
                      </div>
                    )}
                    {step === 2 && (
                      <>
                        <Field
                          label="Promotion budget"
                        >
                          <MoneyInput
                            value={form.campaignBudget}
                            min={Math.max(
                              0.01,
                              price.minimumCampaignBudget ?? 0.01,
                            )}
                            onChange={(e) =>
                              set("campaignBudget", e.target.value)
                            }
                          />
                        </Field>
                        <div className="balance-banner">
                          <strong>{form.title}</strong>
                          <p>
                            {campaignType(type)} ·{" "}
                            {form.category || "All Creator categories"}
                          </p>
                          <p>Creating a draft does not reserve funds.</p>
                        </div>
                      </>
                    )}
                    {action.error && <Notice error>{action.error}</Notice>}
                    <div className="form-actions">
                      {step > 0 && (
                        <Button
                          variant="secondary"
                          onClick={() => setStep(step - 1)}
                        >
                          Back
                        </Button>
                      )}
                      <Button type="submit" icon="arrow" disabled={action.busy}>
                        {action.busy
                          ? "Creating…"
                          : step === 2
                            ? "Create Draft"
                            : "Continue"}
                      </Button>
                    </div>
                  </fieldset>
                </form>
              </Section>
              <aside>
                <Section
                  title="Your activity pricing"
                  description="Saved with your Promotion."
                >
                  <p className="eyebrow">{campaignType(type)}</p>
                  <div className="price-feature">
                    <strong>
                      {amount(price.businessPays)}
                    </strong>
                    <span>per {count(price.views)} verified views</span>
                  </div>
                  {isViewAndSale(type) && (
                    <p>
                      Plus {amount(price.saleCostPercent)}% per verified sale.
                    </p>
                  )}
                  {price.minimumCampaignBudget !== null && (
                    <p>
                      Minimum Promotion budget:{" "}
                      {amount(price.minimumCampaignBudget)}
                    </p>
                  )}
                  <p className="fine-print">
                    Promotion duration: {data.promotionLiveDurationDays} days · Set by Weymela.
                  </p>
                </Section>
              </aside>
            </div>
          );
        }}
      </Resource>
    </>
  );
}
