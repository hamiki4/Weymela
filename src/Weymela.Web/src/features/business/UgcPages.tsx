import { useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type { UgcAssignment, UgcCard, UgcPricing } from "../../api/types";
import {
  Badge,
  Button,
  Empty,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, date } from "../../ui/format";

const SOCIAL_PLATFORMS = ["TikTok", "Instagram", "YouTube", "Facebook"] as const;
const roundMoney = (value: number) => Math.round(value * 100) / 100;

type BusinessUgcForm = {
  title: string;
  contentType: "Video" | "Photos";
  instructions: string;
  location: string;
  dueDate: string;
  creatorPayment: string;
  creatorsNeeded: string;
  mustPost: boolean;
  platforms: string[];
  customerOffer: boolean;
  customerDiscount: string;
  customerRewardBudget: string;
};

const emptyForm: BusinessUgcForm = {
  title: "",
  contentType: "Video",
  instructions: "",
  location: "",
  dueDate: "",
  creatorPayment: "",
  creatorsNeeded: "1",
  mustPost: false,
  platforms: [],
  customerOffer: false,
  customerDiscount: "",
  customerRewardBudget: "",
};

function PlatformList({ platforms }: { platforms: { platform: string }[] }) {
  return platforms.length ? (
    <div className="tag-row">
      {platforms.map((item) => <Badge key={item.platform} status={item.platform} />)}
    </div>
  ) : <span className="muted">Deliver directly to the Business</span>;
}

function BusinessUgcCard({ item, onChanged }: { item: UgcCard; onChanged: () => void }) {
  const action = useAction();
  return (
    <article className="data-card">
      <div className="card-head"><div><small>{item.customerOfferEnabled ? "UGC + Discount Sale" : "UGC Only"} · {item.contentType}</small><h3>{item.title}</h3></div><Badge status={item.status} /></div>
      <dl className="funds-grid">
        <div><dt>Creator Payment</dt><dd>{amount(item.creatorPayment)}</dd></div>
        <div><dt>Platform Fee</dt><dd>{item.platformFee === undefined ? "—" : amount(item.platformFee)} {item.platformFeePercent === undefined ? "" : `(${amount(item.platformFeePercent)}%)`}</dd></div>
        <div><dt>Total Required Funding</dt><dd>{item.requiredFunding === undefined ? "—" : amount(item.requiredFunding)}</dd></div>
        <div><dt>Due</dt><dd>{date(item.dueDateUtc)}</dd></div>
      </dl>
      <p className="fine-print">{item.platformRequirements.length ? "Creator must post on:" : "Creator delivers content to the Business."}</p>
      <PlatformList platforms={item.platformRequirements} />
      {item.customerOfferEnabled && item.customerDiscountPercent !== undefined && item.customerOfferFundedAllocation !== undefined && <p className="fine-print">Customer discount: {amount(item.customerDiscountPercent)}% · Discount funding: {amount(item.customerOfferFundedAllocation)}.</p>}
      {item.status === "Draft" && <Button onClick={() => void action.run(async (key) => { await post(`/business/ugc/${item.id}/publish`, { version: item.version }, key); onChanged(); })} disabled={action.busy}>{action.busy ? "Publishing…" : "Publish UGC"}</Button>}
      {action.error && <Notice error>{action.error}</Notice>}
    </article>
  );
}

export function BusinessUgcPage() {
  const pricing = useResource<UgcPricing>("/business/ugc-pricing");
  const opportunities = useResource<UgcCard[]>("/business/ugc");
  const action = useAction();
  const [form, setForm] = useState<BusinessUgcForm>(emptyForm);
  const set = <K extends keyof BusinessUgcForm>(key: K, value: BusinessUgcForm[K]) => setForm((current) => ({ ...current, [key]: value }));

  return (
    <>
      <PageHeader title="UGC" description="Fixed Creator payment and optional Customer discount." action={<a className="button primary" href="#ugc-create">Create UGC</a>} />
      <Section title="UGC Promotions"><Resource resource={opportunities}>{(rows) => rows.length ? <div className="card-stack">{rows.map((item) => <BusinessUgcCard key={item.id} item={item} onChanged={opportunities.reload} />)}</div> : <Empty title="No UGC yet" message="Create a UGC Promotion below." />}</Resource></Section>
      <div id="ugc-create"><Resource resource={pricing}>
        {(config) => {
          const payment = Number(form.creatorPayment) || 0;
          const creators = Number(form.creatorsNeeded) || 0;
          const feePerCreator = roundMoney(payment * config.platformFeePercent / 100);
          const platformFee = roundMoney(feePerCreator * creators);
          const creatorTotal = roundMoney(payment * creators);
          const requiredFunding = roundMoney(creatorTotal + platformFee);
          const minimumBudgetMet = config.minimumUgcBudget === null || requiredFunding >= config.minimumUgcBudget;
          const discount = Number(form.customerDiscount) || 0;
          const offerValid = !form.customerOffer || (config.customerOfferPlatformSalePercent !== null && discount > 0 && discount <= 100 && Number(form.customerRewardBudget) > 0);
          const valid = Boolean(form.title.trim() && form.instructions.trim() && form.dueDate && payment >= config.minimumCreatorPayment && creators >= 1 && minimumBudgetMet && (!form.mustPost || form.platforms.length > 0) && offerValid);
          return (
            <div className="content-grid form-layout">
              <Section title="Create UGC" description="Creator payment and funding use the current effective Admin UGC settings.">
                <form className="form-grid" onSubmit={(event) => {
                  event.preventDefault();
                  if (!valid) return;
                  void action.run(async (key) => {
                    await post("/business/ugc", {
                      title: form.title, slogan: null, contentType: form.contentType, instructions: form.instructions,
                      resources: [], location: form.location || null, dueDateUtc: new Date(form.dueDate).toISOString(),
                      productProvided: false, creatorMustPurchase: false, usageRights: null, creatorPayment: payment, creatorsNeeded: creators,
                      platformRequirements: form.mustPost ? form.platforms.map((platform) => ({ platform, format: "Social post", minimumAudience: null })) : [],
                      customerOfferEnabled: form.customerOffer, customerDiscountPercent: form.customerOffer ? discount : null,
                      customerOfferFundedAllocation: form.customerOffer ? Number(form.customerRewardBudget) : null,
                      customerFacingSlogan: null, customerOfferStartsAtUtc: form.customerOffer ? new Date().toISOString() : null,
                      customerOfferEndsAtUtc: form.customerOffer ? new Date(form.dueDate).toISOString() : null,
                    }, key);
                    setForm(emptyForm);
                    opportunities.reload();
                  });
                }}>
                  <Field label="UGC title" wide><input value={form.title} onChange={(e) => set("title", e.target.value)} required /></Field>
                  <Field label="Content type"><select value={form.contentType} onChange={(e) => set("contentType", e.target.value as BusinessUgcForm["contentType"])}><option value="Video">Video</option><option value="Photos">Photos</option></select></Field>
                  <Field label="Due date"><input type="datetime-local" value={form.dueDate} onChange={(e) => set("dueDate", e.target.value)} required /></Field>
                  <Field label="Creator Payment"><input type="number" min={config.minimumCreatorPayment} step="0.01" value={form.creatorPayment} onChange={(e) => set("creatorPayment", e.target.value)} required /></Field>
                  <Field label="Creators needed"><input type="number" min="1" step="1" value={form.creatorsNeeded} onChange={(e) => set("creatorsNeeded", e.target.value)} required /></Field>
                  <Field label="Location"><input value={form.location} onChange={(e) => set("location", e.target.value)} /></Field>
                  <Field label="Instructions" wide><textarea value={form.instructions} onChange={(e) => set("instructions", e.target.value)} required /></Field>
                  <label className="field wide"><span><input type="checkbox" checked={form.mustPost} onChange={(e) => set("mustPost", e.target.checked)} /> Creator must post on social media</span><small>OFF means the Creator delivers the video/content directly to the Business.</small></label>
                  {form.mustPost && <fieldset className="field wide"><legend>Required social platform</legend><div className="tag-row">{SOCIAL_PLATFORMS.map((platform) => <label key={platform}><input type="checkbox" checked={form.platforms.includes(platform)} onChange={(e) => set("platforms", e.target.checked ? [...form.platforms, platform] : form.platforms.filter((item) => item !== platform))} /> {platform}</label>)}</div></fieldset>}
                  <label className="field wide"><span><input type="checkbox" checked={form.customerOffer} onChange={(e) => set("customerOffer", e.target.checked)} /> Add Customer discount sale</span></label>
                  {form.customerOffer && <><Field label="Customer Discount %"><input type="number" min="0.01" max="100" step="0.0001" value={form.customerDiscount} onChange={(e) => set("customerDiscount", e.target.value)} required /></Field><Field label="Customer Reward Budget"><input type="number" min="0.01" step="0.01" value={form.customerRewardBudget} onChange={(e) => set("customerRewardBudget", e.target.value)} required /></Field></>}
                  {form.mustPost && form.platforms.length === 0 && <Notice error>Choose at least one social platform.</Notice>}
                  {form.customerOffer && config.customerOfferPlatformSalePercent === null && <Notice error>Customer Offers require an Admin platform sale fee configuration.</Notice>}
                  {form.customerOffer && discount > 100 && <Notice error>Discount must be between 0 and 100%.</Notice>}
                  {!minimumBudgetMet && <Notice error>Required UGC funding is below the configured minimum budget.</Notice>}
                  {action.error && <Notice error>{action.error}</Notice>}
                  <div className="form-actions wide"><Button type="submit" disabled={action.busy || !valid}>{action.busy ? "Creating…" : "Create UGC"}</Button></div>
                </form>
              </Section>
              <Section title="Funding Summary" description="The effective Admin UGC Platform Fee is applied to each Creator Payment.">
                <dl className="funds-grid"><div><dt>Creator Payment</dt><dd>{amount(creatorTotal)}</dd></div><div><dt>Platform Fee</dt><dd>{amount(platformFee)}</dd></div><div><dt>Total Required Funding</dt><dd>{amount(requiredFunding)}</dd></div>{form.customerOffer && <div><dt>Customer Discount</dt><dd>{amount(discount)}%</dd></div>}{form.customerOffer && <div><dt>Customer Reward Budget</dt><dd>{amount(Number(form.customerRewardBudget) || 0)}</dd></div>}</dl>
                <p className="fine-print">Current Admin UGC fee: {amount(config.platformFeePercent)}%.</p>
              </Section>
            </div>
          );
        }}
      </Resource></div>
    </>
  );
}

export function CreatorAssignmentCard({ assignment, onSubmitted }: { assignment: UgcAssignment; onSubmitted: () => void }) {
  const action = useAction();
  const [url, setUrl] = useState(assignment.submissionUrl ?? "");
  const socialRequired = assignment.platformRequirements.length > 0;
  return <article className="data-card">
    <div className="card-head"><div><small>UGC assignment</small><h3>{assignment.opportunity}</h3></div><Badge status={assignment.status} /></div>
    <dl className="funds-grid"><div><dt>Your Payment</dt><dd>{amount(assignment.creatorPayment)}</dd></div><div><dt>Due</dt><dd>{date(assignment.dueDateUtc)}</dd></div></dl>
    <p>{assignment.instructions}</p><p className="fine-print">{socialRequired ? "Post on:" : "Deliver the content directly to the Business."}</p><PlatformList platforms={assignment.platformRequirements} />
    {assignment.feedback && <Notice>{assignment.feedback}</Notice>}
    {(assignment.status === "InProgress" || assignment.status === "ChangesRequested") && <form className="actions" onSubmit={(event) => { event.preventDefault(); if (!url.trim()) return; void action.run(async (key) => { await post(`/creator/ugc/assignments/${assignment.id}/submit`, { submissionUrl: url }, key); onSubmitted(); }); }}><Field label={socialRequired ? "Social post link" : "Content delivery link"}><input type="url" value={url} onChange={(e) => setUrl(e.target.value)} required /></Field>{action.error && <Notice error>{action.error}</Notice>}<Button type="submit" disabled={action.busy || !url.trim()}>{action.busy ? "Submitting…" : "Submit Content"}</Button></form>}
  </article>;
}

export function CreatorUgcPage() {
  const opportunities = useResource<UgcCard[]>("/creator/ugc");
  const assignments = useResource<UgcAssignment[]>("/creator/ugc/assignments");
  const action = useAction();
  const refresh = () => { opportunities.reload(); assignments.reload(); };
  return <>
    <PageHeader eyebrow="Creator content" title="UGC" description="Find UGC opportunities and deliver content for your agreed one-time payment." />
    <Section title="Available UGC"><Resource resource={opportunities}>{(rows) => rows.length ? <div className="card-stack">{rows.map((item) => <article className="data-card" key={item.id}><div className="card-head"><div><small>{item.business}</small><h3>{item.title}</h3></div><Badge status={item.requestStatus ?? "Ready"} /></div><dl className="funds-grid"><div><dt>Your Payment</dt><dd>{amount(item.creatorPayment)}</dd></div><div><dt>Due</dt><dd>{date(item.dueDateUtc)}</dd></div></dl><p className="fine-print">{item.platformRequirements.length ? "Creator must post on:" : "Creator delivers content to the Business."}</p><PlatformList platforms={item.platformRequirements} />{!item.requestStatus && <Button onClick={() => void action.run(async (key) => { await post(`/creator/ugc/${item.id}/request`, undefined, key); refresh(); })} disabled={action.busy}>Request to Join</Button>}</article>)}</div> : <Empty title="No available UGC right now" message="New opportunities will appear here when they are open." />}</Resource>{action.error && <Notice error>{action.error}</Notice>}</Section>
    <Section title="Your UGC assignments" description="Only your agreed Creator Payment is shown here."><Resource resource={assignments}>{(rows) => rows.length ? <div className="card-stack">{rows.map((assignment) => <CreatorAssignmentCard key={assignment.id} assignment={assignment} onSubmitted={refresh} />)}</div> : <Empty title="No UGC assignments yet" message="Approved UGC requests will appear here." />}</Resource></Section>
  </>;
}
