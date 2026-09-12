import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type { BusinessPricing, CampaignType } from "../../api/types";
import {
  Button,
  Field,
  MoneyInput,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, campaignType, count } from "../../ui/format";

export function CreateCampaign() {
  const pricing = useResource<BusinessPricing>("/business/pricing");
  const action = useAction();
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [type, setType] = useState<CampaignType>("ViewOnly");
  const [form, setForm] = useState({
    title: "",
    description: "",
    campaignBudget: "",
    requirements: "",
    category: "",
    region: "",
    minimumVerifiedFollowers: "",
    startUtc: "",
    endUtc: "",
  });
  const set = (name: keyof typeof form, value: string) =>
    setForm((current) => ({ ...current, [name]: value }));
  return (
    <>
      <PageHeader
        eyebrow="Make your next connection"
        title="Create Campaign"
        description="Your idea, your budget. Weymela’s verified activity pricing takes care of the rest."
      />
      <Resource resource={pricing}>
        {(data) => {
          const price = data.rows.find((row) => row.type === type)!;
          return (
            <div className="content-grid form-layout">
              <Section
                title={
                  [
                    "Start with the story",
                    "Find the right Creators",
                    "Set your Campaign Budget",
                  ][step]
                }
              >
                <ol className="stepper" aria-label="Campaign setup progress">
                  {["Campaign", "Creators", "Budget"].map((label, index) => (
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
                        <Field label="Campaign Title" wide>
                          <input
                            value={form.title}
                            onChange={(e) => set("title", e.target.value)}
                            maxLength={120}
                            required
                          />
                        </Field>
                        <Field
                          label="Description"
                          wide
                          help="Tell Creators what makes your Business and this Campaign special."
                        >
                          <textarea
                            value={form.description}
                            onChange={(e) => set("description", e.target.value)}
                            maxLength={3000}
                            required
                            rows={4}
                          />
                        </Field>
                        <Field label="Campaign Type" wide>
                          <select
                            value={type}
                            onChange={(e) =>
                              setType(e.target.value as CampaignType)
                            }
                          >
                            <option value="ViewOnly">View Only</option>
                            <option value="ViewPlusCommission">
                              View + Commission
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
                          help="What would you like your Creators to make?"
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
                          help="Optional. Leave blank for all categories."
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
                          help="Optional. Leave blank for all regions."
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
                          help="Optional. Based on verified social metrics."
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
                      </div>
                    )}
                    {step === 2 && (
                      <>
                        <Field
                          label="Campaign Budget (ETB)"
                          help="The total funds you will commit to this Campaign."
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
                          <p>
                            Next, review your wallet balance and confirm
                            funding. Creating this draft does not reserve funds.
                          </p>
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
                  description="Set by Weymela. Saved with your Campaign."
                >
                  <p className="eyebrow">{campaignType(type)}</p>
                  <div className="price-feature">
                    <strong>
                      {amount(price.businessPays)} <small>ETB</small>
                    </strong>
                    <span>per {count(price.views)} verified views</span>
                  </div>
                  {type === "ViewPlusCommission" && (
                    <p>
                      Plus {amount(price.saleCostPercent)}% per verified sale.
                    </p>
                  )}
                  {price.minimumCampaignBudget !== null && (
                    <p>
                      Minimum Campaign Budget:{" "}
                      {amount(price.minimumCampaignBudget)} ETB
                    </p>
                  )}
                  <p className="fine-print">
                    Your Creator Budgets protect the funds assigned to each
                    Creator. No Creator can spend another Creator’s budget.
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
