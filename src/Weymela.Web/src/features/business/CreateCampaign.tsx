import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import type { BusinessPricing, CampaignTypeCode, Wallet } from "../../api/types";
import { BusinessCreationGate } from "./BusinessCreationGate";
import { PlatformCapacityPicker } from "./PlatformCapacityPicker";
import { Button, Field, MoneyInput, Notice, PageHeader, Resource, Section } from "../../ui/components";
import { amount, count, isViewAndSale, promotionTypeCode } from "../../ui/format";

export function CreateCampaign() { return <BusinessCreationGate>{wallet => <CreateCampaignForm wallet={wallet} />}</BusinessCreationGate>; }

function CreateCampaignForm({ wallet }: { wallet: Wallet }) {
  const pricing = useResource<BusinessPricing>("/business/pricing"); const action = useAction(); const navigate = useNavigate();
  const [type, setType] = useState<CampaignTypeCode>("ViewOnly");
  const [form, setForm] = useState({ title: "", campaignBudget: "", requirements: "", category: "", region: "", minimumVerifiedFollowers: "", applicationCloses: "", contentDue: "" });
  const [platforms, setPlatforms] = useState<{ platform: string; capacity: number }[]>([]);
  const set = (name: keyof typeof form, value: string) => setForm(current => ({ ...current, [name]: value }));
  return <><PageHeader title="Create Promotion" compact /><Resource resource={pricing}>{data => {
    const price = data.rows.find(row => promotionTypeCode(row.type) === type); if (!price) return <Notice error>Promotion pricing is unavailable.</Notice>;
    const budget = Number(form.campaignBudget) || 0; const shortfall = Math.max(0, budget - wallet.available);
    const validDates = Boolean(form.applicationCloses && form.contentDue && new Date(form.applicationCloses) < new Date(form.contentDue));
    return <div className="content-grid form-layout"><Section title="Promotion details">
      {wallet.available === 0 && <Notice>Available funds: {amount(0)} ETB. <Link to="/business/wallet">Add Funds</Link> before publishing.</Notice>}
      <form className="form-grid" onSubmit={event => { event.preventDefault(); if (!validDates || platforms.length === 0) return; void action.run(async key => {
        const due = new Date(form.contentDue); const result = await post<{ id: string }>("/business/promotions", {
          title: form.title, description: form.requirements, slogan: null, location: null, resources: [], type, campaignBudget: budget,
          requirements: form.requirements, category: form.category || null, region: form.region || null,
          minimumVerifiedFollowers: form.minimumVerifiedFollowers ? Number(form.minimumVerifiedFollowers) : null,
          startUtc: new Date().toISOString(), endUtc: new Date(due.getTime() + 30 * 86400000).toISOString(),
          applicationClosesAtUtc: new Date(form.applicationCloses).toISOString(), contentDueAtUtc: due.toISOString(), platforms,
        }, key); const latest = await request<Wallet>("/business/wallet");
        await post(`/business/promotions/${result.id}/fund`, { campaignVersion: 0, walletVersion: latest.version }, key);
        await post(`/business/promotions/${result.id}/publish`, { version: 1 }, key); navigate(`/business/campaigns/${result.id}`);
      }); }}>
        <Field label="Promotion title" wide><input value={form.title} onChange={e => set("title", e.target.value)} maxLength={120} required /></Field>
        <Field label="Promotion type" wide><select value={type} onChange={e => setType(e.target.value as CampaignTypeCode)}><option value="ViewOnly">View Only</option><option value="ViewPlusCommission">View &amp; Sale</option></select></Field>
        <Field label="Application closes"><input type="datetime-local" value={form.applicationCloses} onChange={e => set("applicationCloses", e.target.value)} required /></Field>
        <Field label="Content due"><input type="datetime-local" value={form.contentDue} onChange={e => set("contentDue", e.target.value)} required /></Field>
        {!validDates && (form.applicationCloses || form.contentDue) && <Notice error>Application closes must be before content due.</Notice>}
        <Field label="Requirements" wide><textarea value={form.requirements} onChange={e => set("requirements", e.target.value)} maxLength={2000} rows={3} /></Field>
        <Field label="Creator category"><input value={form.category} onChange={e => set("category", e.target.value)} maxLength={80} /></Field><Field label="Region"><input value={form.region} onChange={e => set("region", e.target.value)} maxLength={80} /></Field>
        <Field label="Minimum verified followers"><input type="number" min="0" step="1" value={form.minimumVerifiedFollowers} onChange={e => set("minimumVerifiedFollowers", e.target.value)} /></Field>
        <div className="field wide"><span className="field-label">Creators needed</span><PlatformCapacityPicker value={platforms} onChange={setPlatforms} />{platforms.length === 0 && <Notice error>Choose at least one Creator slot.</Notice>}</div>
        <Field label="Promotion budget"><MoneyInput value={form.campaignBudget} min={Math.max(0.01, price.minimumCampaignBudget ?? 0.01)} onChange={e => set("campaignBudget", e.target.value)} /></Field>
        <dl className="funds-grid wide"><div><dt>Available funds</dt><dd>{amount(wallet.available)} ETB</dd></div><div><dt>Promotion budget</dt><dd>{amount(budget)} ETB</dd></div>{budget > 0 && <div><dt>{shortfall > 0 ? "Need" : "Remaining after funding"}</dt><dd>{amount(shortfall > 0 ? shortfall : wallet.available - budget)} ETB{shortfall > 0 ? " more" : ""}</dd></div>}</dl>
        {shortfall > 0 && <Notice error>Available funds cannot cover this Promotion. <Link to="/business/wallet">Add Funds</Link>.</Notice>}{action.error && <Notice error>{action.error}</Notice>}
        <div className="form-actions wide"><Button type="submit" icon="arrow" disabled={action.busy || !validDates || platforms.length === 0 || shortfall > 0}>{action.busy ? "Publishing…" : "Publish Promotion"}</Button></div>
      </form>
    </Section><aside><Section title="Activity rates"><strong>{amount(price.businessPays)}</strong><span> per {count(price.views)} verified views</span>{isViewAndSale(type) && <p>Plus {amount(price.saleCostPercent)}% per verified sale.</p>}</Section></aside></div>;
  }}</Resource></>;
}
