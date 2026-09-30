import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import type { UgcAssignment, UgcCard, UgcDetail, UgcPricing, Wallet } from "../../api/types";
import { BusinessCreationGate } from "./BusinessCreationGate";
import { PlatformCapacityPicker, type PlatformCapacity } from "./PlatformCapacityPicker";
import { PlatformOccupancy } from "../creator/CreatorPlatformIcon";
import { UgcJoinControls } from "../creator/UgcJoinControls";
import {
  ActionLink,
  Badge,
  Button,
  CreatorAvatar,
  Empty,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, date } from "../../ui/format";

const roundMoney = (value: number) => Math.round(value * 100) / 100;

type BusinessUgcForm = {
  title: string;
  contentType: "Video" | "Photos";
  instructions: string;
  location: string;
  dueDate: string;
  creatorPayment: string;
  creatorsNeeded: string;
  productArrangement: "" | "Provided" | "Purchase";
  mustPost: boolean;
  platforms: PlatformCapacity[];
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
  productArrangement: "",
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

function ProductArrangement({ provided, purchase }: { provided: boolean; purchase: boolean }) {
  return <p className="ugc-arrangement"><strong>Product arrangement:</strong> {provided && !purchase ? "Product provided by Business" : purchase && !provided ? "Creator purchases product" : "Not selected"}</p>;
}

function ProductArrangementChoice({ value, onChange, name = "product-arrangement" }: { value: BusinessUgcForm["productArrangement"]; onChange: (value: BusinessUgcForm["productArrangement"]) => void; name?: string }) {
  return <fieldset className="field wide ugc-product-choice"><legend>Product arrangement</legend>
    <label><input type="radio" name={name} value="Provided" checked={value === "Provided"} onChange={() => onChange("Provided")} required /><span><strong>Product provided by Business</strong><small>No cost to Creator</small></span></label>
    <label><input type="radio" name={name} value="Purchase" checked={value === "Purchase"} onChange={() => onChange("Purchase")} required /><span><strong>Creator purchases product</strong><small>Creator buys before creating content</small></span></label>
  </fieldset>;
}

function BusinessUgcCard({ item, wallet, onChanged }: { item: UgcCard; wallet: Wallet | null; onChanged: () => void }) {
  const action = useAction();
  const [creatorDetail, setCreatorDetail] = useState<UgcDetail | null>(null);
  const [creatorError, setCreatorError] = useState("");
  const [arrangement, setArrangement] = useState<BusinessUgcForm["productArrangement"]>("");
  const arrangementReady = item.productProvided !== item.creatorMustPurchase;
  const required = (item.requiredFunding ?? 0) + (item.customerOfferFundedAllocation ?? 0);
  const shortfall = wallet && required > 0 ? roundMoney(Math.max(0, required - wallet.available)) : 0;
  return (
    <article className="data-card">
      <div className="card-head"><div><small>{item.customerOfferEnabled ? "UGC + Discount Sale" : "UGC Only"} · {item.contentType}</small><h3>{item.title}</h3></div><Badge status={item.status} /></div>
      <dl className="funds-grid">
        <div><dt>UGC commitment</dt><dd>{item.requiredFunding === undefined ? "—" : amount(item.requiredFunding)} ETB</dd></div>
        <div><dt>Creator capacity</dt><dd>{item.creatorsNeeded}</dd></div>
        <div><dt>Due</dt><dd>{date(item.dueDateUtc)}</dd></div>
      </dl>
      <ProductArrangement provided={item.productProvided} purchase={item.creatorMustPurchase} />
      <p className="fine-print">{item.platformRequirements.length ? "Creator must post on:" : "Creator delivers content to the Business."}</p>
      {item.platformCapacities?.length ? <PlatformOccupancy slots={item.platformCapacities} />
        : <PlatformList platforms={item.platformRequirements} />}
      {item.customerOfferEnabled && item.customerDiscountPercent !== undefined && item.customerOfferFundedAllocation !== undefined && <p className="fine-print">Customer discount: {amount(item.customerDiscountPercent)}% · Discount funding: {amount(item.customerOfferFundedAllocation)}.</p>}
      {item.status !== "Draft" && <>
        <Button variant="secondary" onClick={() => {
          if (creatorDetail) { setCreatorDetail(null); return; }
          setCreatorError("");
          void request<UgcDetail>(`/business/ugc/${item.id}`).then(setCreatorDetail)
            .catch(error => setCreatorError(error instanceof Error ? error.message : "Creator requests are unavailable."));
        }}>{creatorDetail ? "Hide Creators" : "Creator requests & assignments"}</Button>
        {creatorError && <Notice error>{creatorError}</Notice>}
        {creatorDetail && <div className="business-ugc-creator-list">
          <h4>Creator requests</h4>
          {creatorDetail.requests.length ? creatorDetail.requests.map(row => <div className="business-ugc-creator-row" key={row.id}>
            <CreatorAvatar name={row.creator} path={`/business/creator-photos/${row.creatorId}`} />
            <div><strong>{row.creator}</strong><small>{row.creatorNumber != null ? `Creator #${row.creatorNumber}` : "Creator"}</small></div>
            <Badge status={row.status} />
            {row.status === "Pending" && <div className="actions"><Button disabled={action.busy} onClick={() => void action.run(async key => {
              await post(`/business/ugc/requests/${row.id}/approve`, { reason: null }, key);
              setCreatorDetail(await request<UgcDetail>(`/business/ugc/${item.id}`)); onChanged();
            })}>Approve</Button><Button variant="quiet" disabled={action.busy} onClick={() => void action.run(async key => {
              await post(`/business/ugc/requests/${row.id}/reject`, { reason: null }, key);
              setCreatorDetail(await request<UgcDetail>(`/business/ugc/${item.id}`)); onChanged();
            })}>Reject</Button></div>}
          </div>) : <p className="muted">No Creator requests yet.</p>}
          {!!creatorDetail.assignments.length && <><h4>Approved Creators</h4>{creatorDetail.assignments.map(row => <div className="business-ugc-creator-row" key={row.id}>
            <CreatorAvatar name={row.creator} path={`/business/creator-photos/${row.creatorId}`} />
            <div><strong>{row.creator}</strong><small>{row.creatorNumber != null ? `Creator #${row.creatorNumber}` : "Creator"}</small></div><Badge status={row.status} />
          </div>)}</>}
        </div>}
      </>}
      {item.status === "Draft" && <>
        {!arrangementReady && <div className="ugc-draft-arrangement"><ProductArrangementChoice name={`product-arrangement-${item.id}`} value={arrangement} onChange={setArrangement} /><Button variant="secondary" disabled={!arrangement || action.busy} onClick={() => void action.run(async (key) => {
          const detail = await request<UgcDetail>(`/business/ugc/${item.id}`);
          await post(`/business/ugc/${item.id}/update?expectedVersion=${detail.opportunity.version}`, {
            slogan: detail.opportunity.slogan, instructions: detail.instructions, resources: detail.resources,
            location: detail.opportunity.location, usageRights: detail.usageRights, isMaterial: false,
            productProvided: arrangement === "Provided", creatorMustPurchase: arrangement === "Purchase",
          }, key);
          onChanged();
        })}>Save arrangement</Button></div>}
        <dl className="funds-grid"><div><dt>Available funds</dt><dd>{wallet ? `${amount(wallet.available)} ETB` : "Checking…"}</dd></div><div><dt>UGC + discount funding</dt><dd>{amount(required)} ETB</dd></div>{shortfall > 0 && <div><dt>Need</dt><dd>{amount(shortfall)} ETB more</dd></div>}</dl>
        {shortfall > 0 && <Notice error>Add funds before publishing this UGC. <Link to="/business/wallet">Add Funds</Link>.</Notice>}
        <Button onClick={() => void action.run(async (key) => {
          const latest = await request<Wallet>("/business/wallet");
          if (latest.available < required) throw new Error(`Need ${amount(required - latest.available)} more before publishing this UGC.`);
          await post(`/business/ugc/${item.id}/publish`, { version: item.version }, key);
          onChanged();
        })} disabled={action.busy || !wallet || required <= 0 || shortfall > 0 || !arrangementReady}>{action.busy ? "Publishing…" : "Publish UGC"}</Button>
      </>}
      {action.error && <Notice error>{action.error}</Notice>}
    </article>
  );
}

export function BusinessUgcPage() {
  const [params] = useSearchParams();
  const openOnly = params.get("filter") === "Open";
  const opportunities = useResource<UgcCard[]>("/business/ugc");
  const wallet = useResource<Wallet>("/business/wallet");
  return <>
    <PageHeader title="UGC" compact action={<span className="business-create-action"><ActionLink to="/business/ugc/new" icon="plus">Create UGC</ActionLink></span>} />
    <Section title={openOnly ? "Open UGC" : "Your UGC"} action={openOnly ? <Link className="text-link" to="/business/ugc">Show all</Link> : undefined}><Resource resource={opportunities}>{(rows) => {
      const visible = openOnly ? rows.filter((item) => item.status === "Open") : rows;
      return visible.length ? <div className="card-stack">{visible.map((item) => <BusinessUgcCard key={item.id} item={item} wallet={wallet.data} onChanged={() => { opportunities.reload(); wallet.reload(); }} />)}</div> : <Empty title={openOnly ? "No open UGC." : "No UGC yet."} />;
    }}</Resource></Section>
  </>;
}

export function CreateBusinessUgcPage() {
  const navigate = useNavigate();
  const pricing = useResource<UgcPricing>("/business/ugc-pricing");
  const action = useAction();
  const [form, setForm] = useState<BusinessUgcForm>(emptyForm);
  const set = <K extends keyof BusinessUgcForm>(key: K, value: BusinessUgcForm[K]) => setForm((current) => ({ ...current, [key]: value }));

  return (
    <>
      <PageHeader title="Create UGC" />
      <BusinessCreationGate>{creationWallet => <Resource resource={pricing}>
        {(config) => {
          const payment = Number(form.creatorPayment) || 0;
          const creators = form.mustPost ? form.platforms.reduce((total, row) => total + row.capacity, 0) : Number(form.creatorsNeeded) || 0;
          const feePerCreator = roundMoney(payment * config.platformFeePercent / 100);
          const netCreatorPayment = roundMoney(payment - feePerCreator);
          const requiredFunding = roundMoney(payment * creators);
          const discountFunding = form.customerOffer ? Number(form.customerRewardBudget) || 0 : 0;
          const totalFunding = roundMoney(requiredFunding + discountFunding);
          const shortfall = roundMoney(Math.max(0, totalFunding - creationWallet.available));
          const minimumBudgetMet = config.minimumUgcBudget === null || requiredFunding >= config.minimumUgcBudget;
          const discount = Number(form.customerDiscount) || 0;
          const offerValid = !form.customerOffer || (config.customerOfferPlatformSalePercent !== null && discount > 0 && discount <= 100 && Number(form.customerRewardBudget) > 0);
          const valid = Boolean(form.title.trim() && form.instructions.trim() && form.dueDate && form.productArrangement && netCreatorPayment >= config.minimumCreatorPayment && creators >= 1 && minimumBudgetMet && (!form.mustPost || form.platforms.length > 0) && offerValid);
          return (
            <div className="content-grid form-layout">
              <Section title="UGC details">
                <form className="form-grid" onSubmit={(event) => {
                  event.preventDefault();
                  if (!valid) return;
                  void action.run(async (key) => {
                    await post("/business/ugc", {
                      title: form.title, slogan: null, contentType: form.contentType, instructions: form.instructions,
                      resources: [], location: form.location || null, dueDateUtc: new Date(form.dueDate).toISOString(),
                      productProvided: form.productArrangement === "Provided", creatorMustPurchase: form.productArrangement === "Purchase", usageRights: null, creatorPayment: payment, creatorsNeeded: creators,
                      platformRequirements: form.mustPost ? form.platforms.map(({ platform }) => ({ platform, format: "Social post", minimumAudience: null })) : [],
                      platformCapacities: form.mustPost ? form.platforms : [],
                      customerOfferEnabled: form.customerOffer, customerDiscountPercent: form.customerOffer ? discount : null,
                      customerOfferFundedAllocation: form.customerOffer ? Number(form.customerRewardBudget) : null,
                      customerFacingSlogan: null, customerOfferStartsAtUtc: form.customerOffer ? new Date().toISOString() : null,
                      customerOfferEndsAtUtc: form.customerOffer ? new Date(form.dueDate).toISOString() : null,
                    }, key);
                    navigate("/business/ugc");
                  });
                }}>
                  <Field label="UGC title" wide><input value={form.title} onChange={(e) => set("title", e.target.value)} required /></Field>
                  <Field label="Content type"><select value={form.contentType} onChange={(e) => set("contentType", e.target.value as BusinessUgcForm["contentType"])}><option value="Video">Video</option><option value="Photos">Photos</option></select></Field>
                  <Field label="Due date"><input type="datetime-local" value={form.dueDate} onChange={(e) => set("dueDate", e.target.value)} required /></Field>
                  <Field label="Creator payment"><input type="number" min="0.01" step="0.01" value={form.creatorPayment} onChange={(e) => set("creatorPayment", e.target.value)} required /></Field>
                  <Field label="Location"><input value={form.location} onChange={(e) => set("location", e.target.value)} /></Field>
                  <Field label="Instructions" wide><textarea value={form.instructions} onChange={(e) => set("instructions", e.target.value)} required /></Field>
                  <ProductArrangementChoice value={form.productArrangement} onChange={(value) => set("productArrangement", value)} />
                  <fieldset className="field wide ugc-posting-choice"><legend>Posting</legend>
                    <label><input type="radio" name="ugc-posting" checked={!form.mustPost} onChange={() => set("mustPost", false)} /> Deliver content only</label>
                    <label><input type="radio" name="ugc-posting" checked={form.mustPost} onChange={() => set("mustPost", true)} /> Creator must post</label>
                  </fieldset>
                  {form.mustPost ? <PlatformCapacityPicker value={form.platforms} onChange={(value) => set("platforms", value)} />
                    : <Field label="Creator capacity"><input type="number" min="1" max="100" step="1" value={form.creatorsNeeded} onChange={(e) => set("creatorsNeeded", e.target.value)} required /></Field>}
                  <label className="field wide"><span><input type="checkbox" checked={form.customerOffer} onChange={(e) => set("customerOffer", e.target.checked)} /> Add Customer discount sale</span></label>
                  {form.customerOffer && <><Field label="Customer Discount %"><input type="number" min="0.01" max="100" step="0.0001" value={form.customerDiscount} onChange={(e) => set("customerDiscount", e.target.value)} required /></Field><Field label="Customer Reward Budget"><input type="number" min="0.01" step="0.01" value={form.customerRewardBudget} onChange={(e) => set("customerRewardBudget", e.target.value)} required /></Field></>}
                  {form.mustPost && form.platforms.length === 0 && <Notice error>Choose at least one social platform.</Notice>}
                  {form.customerOffer && config.customerOfferPlatformSalePercent === null && <Notice error>Customer Offers require an Admin platform sale fee configuration.</Notice>}
                  {form.customerOffer && discount > 100 && <Notice error>Discount must be between 0 and 100%.</Notice>}
                  {!minimumBudgetMet && <Notice error>Required UGC funding is below the configured minimum budget.</Notice>}
                  {action.error && <Notice error>{action.error}</Notice>}
                  <div className="form-actions wide"><Button type="submit" disabled={action.busy || !valid}>{action.busy ? "Saving…" : "Save UGC Draft"}</Button></div>
                </form>
              </Section>
              <Section title="Funding Summary">
                <dl className="funds-grid"><div><dt>Available funds</dt><dd>{amount(creationWallet.available)} ETB</dd></div><div><dt>UGC commitment</dt><dd>{amount(requiredFunding)} ETB</dd></div>{form.customerOffer && <div><dt>Customer Discount</dt><dd>{amount(discount)}%</dd></div>}{form.customerOffer && <div><dt>Customer discount funding</dt><dd>{amount(discountFunding)} ETB</dd></div>}<div><dt>Total funding to publish</dt><dd>{amount(totalFunding)} ETB</dd></div>{totalFunding > 0 && <div><dt>{shortfall > 0 ? "Need" : "Remaining after funding"}</dt><dd>{amount(shortfall > 0 ? shortfall : creationWallet.available - totalFunding)} ETB{shortfall > 0 ? " more" : ""}</dd></div>}</dl>
                {shortfall > 0 && <Notice error><Link to="/business/wallet">Add Funds</Link> before publishing. This draft reserves zero funds.</Notice>}
              </Section>
            </div>
          );
        }}
      </Resource>}</BusinessCreationGate>
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
    <ProductArrangement provided={assignment.productProvided} purchase={assignment.creatorMustPurchase} />
    <p>{assignment.instructions}</p><p className="fine-print">{socialRequired ? "Post on:" : "Deliver the content directly to the Business."}</p><PlatformList platforms={assignment.platformRequirements} />
    {assignment.feedback && <Notice>{assignment.feedback}</Notice>}
    {(assignment.status === "InProgress" || assignment.status === "ChangesRequested") && <form className="actions" onSubmit={(event) => { event.preventDefault(); if (!url.trim()) return; void action.run(async (key) => { await post(`/creator/ugc/assignments/${assignment.id}/submit`, { submissionUrl: url }, key); onSubmitted(); }); }}><Field label={socialRequired ? "Social post link" : "Content delivery link"}><input type="url" value={url} onChange={(e) => setUrl(e.target.value)} required /></Field>{action.error && <Notice error>{action.error}</Notice>}<Button type="submit" disabled={action.busy || !url.trim()}>{action.busy ? "Submitting…" : "Submit Content"}</Button></form>}
  </article>;
}

export function CreatorUgcPage() {
  const opportunities = useResource<UgcCard[]>("/creator/ugc");
  const assignments = useResource<UgcAssignment[]>("/creator/ugc/assignments");
  const refresh = () => { opportunities.reload(); assignments.reload(); };
  return <>
    <PageHeader eyebrow="Creator content" title="UGC" description="Find UGC opportunities and deliver content for your agreed one-time payment." />
    <Section title="Available UGC"><Resource resource={opportunities}>{(rows) => rows.length ? <div className="card-stack">{rows.map((item) => <article className="data-card" key={item.id}>
        <div className="card-head"><div><small>{item.business}</small><h3>{item.title}</h3></div><Badge status={item.requestStatus ?? "Ready"} /></div>
        <dl className="funds-grid"><div><dt>Your Payment</dt><dd>{amount(item.creatorPayment)}</dd></div><div><dt>Due</dt><dd>{date(item.dueDateUtc)}</dd></div></dl>
        <ProductArrangement provided={item.productProvided} purchase={item.creatorMustPurchase} />
        <p className="fine-print">{item.platformRequirements.length ? "Creator must post on:" : "Creator delivers content to the Business."}</p>
        {!item.platformCapacities?.length && <PlatformList platforms={item.platformRequirements} />}
        <UgcJoinControls item={item} onChanged={refresh} />
      </article>)}</div> : <Empty title="No available UGC right now" message="New opportunities will appear here when they are open." />}</Resource></Section>
    <Section title="Your UGC assignments" description="Only your agreed Creator Payment is shown here."><Resource resource={assignments}>{(rows) => rows.length ? <div className="card-stack">{rows.map((assignment) => <CreatorAssignmentCard key={assignment.id} assignment={assignment} onSubmitted={refresh} />)}</div> : <Empty title="No UGC assignments yet" message="Approved UGC requests will appear here." />}</Resource></Section>
  </>;
}
