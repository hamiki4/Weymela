import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import type { BusinessPricing, CampaignTypeCode, Wallet } from "../../api/types";
import { BusinessCreationGate } from "./BusinessCreationGate";
import { PlatformCapacityPicker } from "./PlatformCapacityPicker";
import { Button, Field, MoneyInput, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { amount, count, isViewAndSale, promotionTypeCode } from "../../ui/format";

export function CreateCampaign() {
  const [params] = useSearchParams();
  const selected = params.get("type");

  if (!selected) {
    return (
      <>
        <PageHeader title="Create Promotion" compact />
        <Section title="What do you want to achieve?">
          <div className="promotion-type-grid">
            <Link className="promotion-type-card" to="/business/campaigns/new?type=views">
              <strong>Views</strong>
              <span>Get visibility through Creator content.</span>
            </Link>

            <Link className="promotion-type-card" to="/business/campaigns/new?type=views-sales">
              <strong>Views + Sales</strong>
              <span>Get visibility and attributed sales.</span>
            </Link>

            <Link className="promotion-type-card" to="/business/ugc/new?type=ugc">
              <strong>UGC</strong>
              <span>Pay Creators to create content for your Business.</span>
            </Link>

            <Link className="promotion-type-card" to="/business/ugc/new?type=ugc-sales">
              <strong>UGC + Sales</strong>
              <span>Pay for content and offer customers a discount.</span>
            </Link>
          </div>
        </Section>
      </>
    );
  }

  if (selected !== "views" && selected !== "views-sales") {
    return <Notice error>Choose a valid Promotion type.</Notice>;
  }

  const type: CampaignTypeCode =
    selected === "views-sales" ? "ViewPlusCommission" : "ViewOnly";

  return (
    <BusinessCreationGate>
      {(wallet) => <CreateCampaignForm wallet={wallet} initialType={type} />}
    </BusinessCreationGate>
  );
}

function CreateCampaignForm({
  wallet,
  initialType,
}: {
  wallet: Wallet;
  initialType: CampaignTypeCode;
}) {
  const pricing = useResource<BusinessPricing>("/business/pricing");
  const action = useAction();
  const navigate = useNavigate();
  const [type] = useState<CampaignTypeCode>(initialType);
  const [form, setForm] = useState({ title: "", campaignBudget: "", description: "", region: "", applicationCloses: "", contentDue: "" });
  const [platforms, setPlatforms] = useState<{ platform: string; capacity: number }[]>([]);
  const set = (name: keyof typeof form, value: string) => setForm(current => ({ ...current, [name]: value }));
  return <><PageHeader title="Create Promotion" compact /><Resource resource={pricing}>{data => {
    const price = data.rows.find(row => promotionTypeCode(row.type) === type); if (!price) return <Notice error>Promotion pricing is unavailable.</Notice>;
    const budget = Number(form.campaignBudget) || 0; const shortfall = Math.max(0, budget - wallet.available);
    const validDates = Boolean(
      form.applicationCloses &&
      form.contentDue &&
      form.applicationCloses < form.contentDue
    );
    const endOfLocalDayUtc = (value: string) => {
      const [year, month, day] = value.split("-").map(Number);
      return new Date(year, month - 1, day, 23, 59, 59, 999).toISOString();
    };
    return <div className="content-grid form-layout"><Section title="Promotion details">
      {wallet.available === 0 && <Notice>Available funds: {amount(0)} ETB. <Link to="/business/wallet">Add Funds</Link> before publishing.</Notice>}
      <form className="form-grid" onSubmit={event => { event.preventDefault(); if (!validDates || platforms.length === 0) return; void action.run(async key => {
        const dueUtc = endOfLocalDayUtc(form.contentDue); const result = await post<{ id: string }>("/business/promotions", {
          title: form.title, description: form.description || null, slogan: null, location: null, resources: [], type, campaignBudget: budget,
          requirements: null, category: null, region: form.region || null,
          minimumVerifiedFollowers: null,
          startUtc: new Date().toISOString(), endUtc: new Date(new Date(dueUtc).getTime() + 30 * 86400000).toISOString(),
          applicationClosesAtUtc: endOfLocalDayUtc(form.applicationCloses), contentDueAtUtc: dueUtc, platforms,
        }, key); const latest = await request<Wallet>("/business/wallet");
        await post(`/business/promotions/${result.id}/fund`, { campaignVersion: 0, walletVersion: latest.version }, key);
        await post(`/business/promotions/${result.id}/publish`, { version: 1 }, key); navigate(`/business/campaigns/${result.id}`);
      }); }}>
        <Field label="Promotion title" wide><input value={form.title} onChange={e => set("title", e.target.value)} maxLength={120} required /></Field>
        <div className="field wide promotion-selected-type">
          <span className="field-label">Promotion type</span>
          <strong>{type === "ViewPlusCommission" ? "Views + Sales" : "Views"}</strong>
          <Link className="text-link" to="/business/campaigns/new">Change</Link>
        </div>
        <Field label="Application closes"><input type="date" value={form.applicationCloses} onChange={e => set("applicationCloses", e.target.value)} required /></Field>
        <Field label="Content due"><input type="date" value={form.contentDue} onChange={e => set("contentDue", e.target.value)} required /></Field>
        {!validDates && (form.applicationCloses || form.contentDue) && <Notice error>Application closes must be before content due.</Notice>}
        <Field label="Region"><input value={form.region} onChange={e => set("region", e.target.value)} maxLength={80} /></Field>
        <div className="field wide"><PlatformCapacityPicker value={platforms} onChange={setPlatforms} />{platforms.length === 0 && <Notice error>Choose at least one Creator slot.</Notice>}</div>
        <Field label="Description" wide><textarea value={form.description} onChange={e => set("description", e.target.value)} maxLength={3000} rows={4} /></Field>
        <Field label="Promotion budget"><MoneyInput value={form.campaignBudget} min={Math.max(0.01, price.minimumCampaignBudget ?? 0.01)} onChange={e => set("campaignBudget", e.target.value)} /></Field>
        <dl className="funds-grid wide"><div><dt>Available funds</dt><dd>{amount(wallet.available)} ETB</dd></div><div><dt>Promotion budget</dt><dd>{amount(budget)} ETB</dd></div>{budget > 0 && <div><dt>{shortfall > 0 ? "Need" : "Remaining after funding"}</dt><dd>{amount(shortfall > 0 ? shortfall : wallet.available - budget)} ETB{shortfall > 0 ? " more" : ""}</dd></div>}</dl>
        {shortfall > 0 && <Notice error>Available funds cannot cover this Promotion. <Link to="/business/wallet">Add Funds</Link>.</Notice>}{action.error && <Notice error>{action.error}</Notice>}
        <div className="form-actions wide"><Button type="submit" icon="arrow" disabled={action.busy || !validDates || platforms.length === 0 || shortfall > 0}>{action.busy ? "Publishing…" : "Publish Promotion"}</Button></div>
      </form>
    </Section><aside><Section title="Activity rates"><strong>{amount(price.businessPays)}</strong><span> per {count(price.views)} verified views</span>{isViewAndSale(type) && <p>Plus {amount(price.saleCostPercent)}% per verified sale.</p>}</Section></aside></div>;
  }}</Resource></>;
}
