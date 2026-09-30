import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import type { BusinessPricing, CampaignTypeCode, Wallet } from "../../api/types";
import { BusinessCreationGate } from "./BusinessCreationGate";
import { PlatformCapacityPicker } from "./PlatformCapacityPicker";
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

export function CreateCampaign() {
  return <BusinessCreationGate>{wallet => <CreateCampaignForm wallet={wallet} />}</BusinessCreationGate>;
}

function CreateCampaignForm({ wallet }: { wallet: Wallet }) {
  const pricing = useResource<BusinessPricing>("/business/pricing");
  const action = useAction();
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [type, setType] = useState<CampaignTypeCode>("ViewOnly");
  const [form, setForm] = useState({
    title: "",
    campaignBudget: "",
    requirements: "",
    category: "",
    region: "",
    minimumVerifiedFollowers: "",
    endUtc: "",
  });
  const [platforms, setPlatforms] = useState<{ platform: string; capacity: number }[]>([]);
  const [budgetWallet, setBudgetWallet] = useState<Wallet | null>(null);
  const [balanceBusy, setBalanceBusy] = useState(false);
  const [balanceError, setBalanceError] = useState<string | null>(null);
  const refreshBalance = async () => {
    setBalanceBusy(true);
    setBalanceError(null);
    try { setBudgetWallet(await request<Wallet>("/business/wallet")); return true; }
    catch (error) { setBalanceError(error instanceof Error ? error.message : "Available funds could not be checked."); return false; }
    finally { setBalanceBusy(false); }
  };
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
          const available = budgetWallet?.available ?? wallet.available;
          const promotionBudget = Number(form.campaignBudget) || 0;
          const shortfall = Math.max(0, Math.round((promotionBudget - available) * 100) / 100);
          const remaining = Math.max(0, Math.round((available - promotionBudget) * 100) / 100);
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
                {step === 0 && wallet.available === 0 && <Notice>
                  Available funds: {amount(0)} ETB. You can save a draft now. <Link to="/business/wallet">Add Funds</Link> before publishing.
                </Notice>}
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
                    if (step === 1) {
                      if (platforms.length === 0) return;
                      void refreshBalance().then(ok => { if (ok) setStep(2); });
                      return;
                    }
                    if (step === 0) {
                      setStep(step + 1);
                      return;
                    }
                    void action.run(async (key) => {
                      const result = await post<{ id: string }>(
                        "/business/campaigns",
                        {
                          ...form,
                          slogan: null,
                          description: "",
                          platforms,
                          type,
                          campaignBudget: Number(form.campaignBudget),
                          minimumVerifiedFollowers:
                            form.minimumVerifiedFollowers
                              ? Number(form.minimumVerifiedFollowers)
                              : null,
                          endUtc: new Date(`${form.endUtc}T23:59:59`).toISOString(),
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
                        <Field label="Promotion ends">
                          <input
                            type="date"
                            value={form.endUtc}
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
                        <PlatformCapacityPicker value={platforms} onChange={setPlatforms} />
                        {platforms.length === 0 && <Notice error>Choose at least one Creator slot.</Notice>}
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
                        <dl className="funds-grid">
                          <div><dt>Available funds</dt><dd>{amount(available)} ETB</dd></div>
                          <div><dt>Promotion budget</dt><dd>{amount(promotionBudget)} ETB</dd></div>
                          {promotionBudget > 0 && <div><dt>{shortfall > 0 ? "Need" : "Remaining after funding"}</dt><dd>{amount(shortfall > 0 ? shortfall : remaining)} ETB{shortfall > 0 ? " more" : ""}</dd></div>}
                        </dl>
                        {shortfall > 0 && <Notice error>Available funds cannot cover this Promotion budget. <Link to="/business/wallet">Add Funds</Link> before funding or publishing.</Notice>}
                        <p className="fine-print">Saving this draft reserves zero funds. Creators cannot find or request it until you fund and publish it.</p>
                        <Button variant="secondary" onClick={() => void refreshBalance()} disabled={balanceBusy}>{balanceBusy ? "Checking…" : "Refresh funds"}</Button>
                      </>
                    )}
                    {balanceError && <Notice error>{balanceError}</Notice>}
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
                      <Button type="submit" icon="arrow" disabled={action.busy || balanceBusy || (step === 1 && platforms.length === 0)}>
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
                  title="Activity rates"
                >
                  <p className="eyebrow">{campaignType(type)}</p>
                  <div className="price-feature business-activity-rate">
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
                </Section>
              </aside>
            </div>
          );
        }}
      </Resource>
    </>
  );
}
