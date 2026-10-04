import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import type { UgcCard, UgcDetail, UgcPricing } from "../../api/types";
import { BusinessCreationGate } from "./BusinessCreationGate";
import { PlatformCapacityPicker, type PlatformCapacity } from "./PlatformCapacityPicker";
import {
  ActionLink,
  Badge,
  Button,
  CreatorAvatar,
  Dialog,
  Empty,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, date, promotionStatusLabel } from "../../ui/format";

const roundMoney = (value: number) => Math.round(value * 100) / 100;

type BusinessUgcForm = {
  title: string;
  contentType: "Video" | "Photos";
  instructions: string;
  location: string;
  dueDate: string;
  applicationCloses: string;
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
  applicationCloses: "",
  creatorPayment: "",
  creatorsNeeded: "1",
  productArrangement: "",
  mustPost: false,
  platforms: [],
  customerOffer: false,
  customerDiscount: "",
  customerRewardBudget: "",
};

function ProductArrangement({ provided, purchase }: { provided: boolean; purchase: boolean }) {
  return <p className="ugc-arrangement"><strong>Product arrangement:</strong> {provided && !purchase ? "Product provided by Business" : purchase && !provided ? "Creator purchases product" : "Not selected"}</p>;
}

function ProductArrangementChoice({ value, onChange, name = "product-arrangement" }: { value: BusinessUgcForm["productArrangement"]; onChange: (value: BusinessUgcForm["productArrangement"]) => void; name?: string }) {
  return <fieldset className="field wide ugc-product-choice"><legend>Product arrangement</legend>
    <label><input type="radio" name={name} value="Provided" checked={value === "Provided"} onChange={() => onChange("Provided")} required /><span><strong>Product provided by Business</strong><small>No cost to Creator</small></span></label>
    <label><input type="radio" name={name} value="Purchase" checked={value === "Purchase"} onChange={() => onChange("Purchase")} required /><span><strong>Creator purchases product</strong><small>Creator buys before creating content</small></span></label>
  </fieldset>;
}

export function BusinessUgcCard({ item, onChanged }: { item: UgcCard; onChanged: () => void }) {
  const action = useAction();
  const editAction = useAction();
  const [creatorDetail, setCreatorDetail] = useState<UgcDetail | null>(null);
  const [editDetail, setEditDetail] = useState<UgcDetail | null>(null);
  const [editInstructions, setEditInstructions] = useState("");
  const [editLocation, setEditLocation] = useState("");
  const [editSlogan, setEditSlogan] = useState("");
  const [editTitle, setEditTitle] = useState("");
  const [editContentType, setEditContentType] = useState<"Video" | "Photos">("Video");
  const [editDue, setEditDue] = useState("");
  const [editCloses, setEditCloses] = useState("");
  const [editPayment, setEditPayment] = useState("");
  const [editCreators, setEditCreators] = useState("");
  const [editArrangement, setEditArrangement] = useState<BusinessUgcForm["productArrangement"]>("");
  const [editMustPost, setEditMustPost] = useState(false);
  const [editPlatforms, setEditPlatforms] = useState<PlatformCapacity[]>([]);
  const [editOffer, setEditOffer] = useState(false);
  const [editDiscount, setEditDiscount] = useState("");
  const [editOfferBudget, setEditOfferBudget] = useState("");
  const [creatorError, setCreatorError] = useState("");
  return (
    <article className="data-card business-promotion-card">
      <div className="card-head"><div><small>{item.customerOfferEnabled ? "UGC + Sales" : "UGC"}</small><h3>{item.title}</h3></div><Badge status={item.status} label={promotionStatusLabel(item.status)} /></div>
      <dl className="promotion-summary-list">
        <div><dt>Creator payment</dt><dd>{amount(item.creatorPayment)} ETB</dd></div>
        <div><dt>Content due</dt><dd>{date(item.dueDateUtc)}</dd></div>
        <div><dt>Creators</dt><dd>{item.approvedCreators}/{item.creatorsNeeded}</dd></div>
        {item.location && <div><dt>Location</dt><dd>{item.location}</dd></div>}
      </dl>
      <p className="promotion-next-action"><strong>Next:</strong> {item.approvedCreators < item.creatorsNeeded ? "Review Creator requests" : "Review Promotion"}</p>
      <ProductArrangement provided={item.productProvided} purchase={item.creatorMustPurchase} />
      {item.customerOfferEnabled && item.customerDiscountPercent !== undefined && <p className="fine-print">Customer gets {amount(item.customerDiscountPercent)}% off.</p>}
      {item.status !== "Draft" && <>
        <Button variant="secondary" onClick={() => void editAction.run(async () => {
          const detail = await request<UgcDetail>(`/business/ugc/${item.id}`);
          const local = (value?: string | null) => value ? new Date(value).toISOString().slice(0, 16) : "";
          setEditDetail(detail); setEditInstructions(detail.instructions); setEditLocation(detail.opportunity.location ?? ""); setEditSlogan(detail.opportunity.slogan ?? "");
          setEditTitle(detail.opportunity.title); setEditContentType(detail.opportunity.contentType as "Video" | "Photos");
          setEditDue(local(detail.opportunity.dueDateUtc)); setEditCloses(local(detail.opportunity.applicationClosesAtUtc));
          setEditPayment(String(detail.opportunity.creatorPayment)); setEditCreators(String(detail.opportunity.creatorsNeeded));
          setEditArrangement(detail.productProvided ? "Provided" : detail.creatorMustPurchase ? "Purchase" : "");
          setEditMustPost(!!detail.opportunity.platformCapacities?.length);
          setEditPlatforms((detail.opportunity.platformCapacities ?? []).map(slot => ({ platform: slot.platform, capacity: slot.capacity, minimumAudience: detail.opportunity.platformRequirements.find(row => row.platform === slot.platform)?.minimumAudience ?? null })));
          setEditOffer(!!detail.opportunity.customerOfferEnabled); setEditDiscount(String(detail.opportunity.customerDiscountPercent ?? "")); setEditOfferBudget(String(detail.opportunity.customerOfferFundedAllocation ?? ""));
        })}>Edit</Button>
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
        <Dialog title="Edit Promotion details" open={!!editDetail} onClose={() => { if (!editAction.busy) setEditDetail(null); }}>
          <p className="muted">{editDetail && editDetail.opportunity.approvedCreators > 0 ? "Some terms are locked because a Creator has been approved." : "You can update the full UGC opportunity while no Creator is approved. Pending applications do not lock editing."}</p>
          <form className="form-grid" onSubmit={(event) => { event.preventDefault(); if (!editDetail) return; void editAction.run(async key => {
            const full = editDetail.opportunity.approvedCreators === 0;
            const iso = (value: string) => value ? new Date(value).toISOString() : null;
            await post(`/business/ugc/${item.id}/update?expectedVersion=${editDetail.opportunity.version}`, full ? {
              title: editTitle, contentType: editContentType, slogan: editSlogan || null, instructions: editInstructions, resources: editDetail.resources,
              location: editLocation || null, usageRights: editDetail.usageRights, isMaterial: true, dueDateUtc: iso(editDue), applicationClosesAtUtc: iso(editCloses),
              productProvided: editArrangement === "Provided", creatorMustPurchase: editArrangement === "Purchase", creatorPayment: Number(editPayment), creatorsNeeded: editMustPost ? editPlatforms.reduce((sum, row) => sum + row.capacity, 0) : Number(editCreators),
              platformRequirements: editMustPost ? editPlatforms.map(row => ({ platform: row.platform, format: "Social post", minimumAudience: row.minimumAudience ?? null })) : [],
              platformCapacities: editMustPost ? editPlatforms : [], customerOfferEnabled: editOffer,
              customerDiscountPercent: editOffer ? Number(editDiscount) : null, customerOfferFundedAllocation: editOffer ? Number(editOfferBudget) : null,
              customerFacingSlogan: null, customerOfferStartsAtUtc: editOffer ? new Date().toISOString() : null, customerOfferEndsAtUtc: editOffer ? iso(editDue) : null,
            } : { slogan: editSlogan || null, instructions: editInstructions, resources: editDetail.resources, location: editLocation || null, usageRights: editDetail.usageRights, isMaterial: false }, key);
            setEditDetail(null); onChanged();
          }); }}>
            {editDetail && editDetail.opportunity.approvedCreators === 0 && <>
              <Field label="Promotion title" wide><input value={editTitle} onChange={event => setEditTitle(event.target.value)} maxLength={120} required /></Field>
              <Field label="Content type"><select value={editContentType} onChange={event => setEditContentType(event.target.value as "Video" | "Photos")}><option value="Video">Video</option><option value="Photos">Photos</option></select></Field>
              <Field label="Creator payment"><input type="number" min="0.01" step="0.01" value={editPayment} onChange={event => setEditPayment(event.target.value)} required /></Field>
              <Field label="Creators needed"><input type="number" min="1" max="100" step="1" value={editCreators} onChange={event => setEditCreators(event.target.value)} required /></Field>
              <Field label="Application closes"><input type="datetime-local" value={editCloses} onChange={event => setEditCloses(event.target.value)} required /></Field>
              <Field label="Content due"><input type="datetime-local" value={editDue} onChange={event => setEditDue(event.target.value)} required /></Field>
              <ProductArrangementChoice value={editArrangement} onChange={setEditArrangement} name="edit-product-arrangement" />
              <fieldset className="field wide ugc-posting-choice"><legend>Posting</legend><label><input type="radio" name="edit-ugc-posting" checked={!editMustPost} onChange={() => setEditMustPost(false)} /> Deliver content only</label><label><input type="radio" name="edit-ugc-posting" checked={editMustPost} onChange={() => setEditMustPost(true)} /> Creator must post</label></fieldset>
              {editMustPost && <PlatformCapacityPicker value={editPlatforms} onChange={setEditPlatforms} />}
              <label className="field wide"><span><input type="checkbox" checked={editOffer} onChange={event => setEditOffer(event.target.checked)} /> Customer discount sale</span></label>
              {editOffer && <><Field label="Customer Discount %"><input type="number" min="0.01" max="100" step="0.0001" value={editDiscount} onChange={event => setEditDiscount(event.target.value)} required /></Field><Field label="Discount funding"><input type="number" min="0.01" step="0.01" value={editOfferBudget} onChange={event => setEditOfferBudget(event.target.value)} required /></Field></>}
            </>}
            <Field label="Slogan"><input value={editSlogan} onChange={event => setEditSlogan(event.target.value)} maxLength={160} /></Field>
            <Field label="Location"><input value={editLocation} onChange={event => setEditLocation(event.target.value)} maxLength={160} /></Field>
            <Field label="Instructions" wide><textarea value={editInstructions} onChange={event => setEditInstructions(event.target.value)} maxLength={4000} rows={5} required /></Field>
            {editAction.error && <Notice error>{editAction.error}</Notice>}
            <div className="form-actions wide"><Button type="submit" disabled={editAction.busy || !editInstructions.trim()}>{editAction.busy ? "Saving…" : "Save changes"}</Button></div>
          </form>
        </Dialog>
      </>}
      {action.error && <Notice error>{action.error}</Notice>}
    </article>
  );
}

export function BusinessUgcPage() {
  const [params] = useSearchParams();
  const openOnly = params.get("filter") === "Open";
  const opportunities = useResource<UgcCard[]>("/business/ugc");
  return <>
    <PageHeader title="Promotions" compact action={<span className="business-create-action"><ActionLink to="/business/campaigns/new" icon="plus">Create Promotion</ActionLink></span>} />
    <Section title={openOnly ? "Active Promotions" : "Promotions"} action={openOnly ? <Link className="text-link" to="/business/ugc">Show all</Link> : undefined}><Resource resource={opportunities}>{(rows) => {
      const visible = rows.filter((item) => item.status !== "Draft" && (!openOnly || item.status === "Open"));
      return visible.length ? <div className="card-stack">{visible.map((item) => <BusinessUgcCard key={item.id} item={item} onChanged={() => opportunities.reload()} />)}</div> : <Empty title={openOnly ? "No active Promotions." : "No Promotions yet."} />;
    }}</Resource></Section>
  </>;
}

export function CreateBusinessUgcPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const selectedType = params.get("type");
  const pricing = useResource<UgcPricing>("/business/ugc-pricing");
  const action = useAction();
  const [form, setForm] = useState<BusinessUgcForm>(() => ({
    ...emptyForm,
    customerOffer: selectedType === "ugc-sales",
  }));
  const set = <K extends keyof BusinessUgcForm>(key: K, value: BusinessUgcForm[K]) => setForm((current) => ({ ...current, [key]: value }));

  return (
    <>
      <PageHeader title="Create Promotion" compact />
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
          const valid = Boolean(form.title.trim() && form.instructions.trim() && form.dueDate && form.applicationCloses && new Date(form.applicationCloses) < new Date(form.dueDate) && form.productArrangement && netCreatorPayment >= config.minimumCreatorPayment && creators >= 1 && minimumBudgetMet && (!form.mustPost || form.platforms.length > 0) && offerValid);
          return (
            <div className="content-grid form-layout">
              <Section title="Promotion details">
                <form className="form-grid" onSubmit={(event) => {
                  event.preventDefault();
                  if (!valid) return;
                  void action.run(async (key) => {
                    const created = await post<{ id: string }>("/business/ugc", {
                      title: form.title, slogan: null, contentType: form.contentType, instructions: form.instructions,
                      resources: [], location: form.location || null, dueDateUtc: new Date(form.dueDate).toISOString(),
                      productProvided: form.productArrangement === "Provided", creatorMustPurchase: form.productArrangement === "Purchase", usageRights: null, creatorPayment: payment, creatorsNeeded: creators,
                      platformRequirements: form.mustPost ? form.platforms.map(({ platform, minimumAudience }) => ({ platform, format: "Social post", minimumAudience: minimumAudience ?? null })) : [],
                      platformCapacities: form.mustPost ? form.platforms : [],
                      customerOfferEnabled: form.customerOffer, customerDiscountPercent: form.customerOffer ? discount : null,
                      customerOfferFundedAllocation: form.customerOffer ? Number(form.customerRewardBudget) : null,
                      customerFacingSlogan: null, customerOfferStartsAtUtc: form.customerOffer ? new Date().toISOString() : null,
                      customerOfferEndsAtUtc: form.customerOffer ? new Date(form.dueDate).toISOString() : null,
                      applicationClosesAtUtc: new Date(form.applicationCloses).toISOString(),
                    }, key);
                    await post(`/business/ugc/${created.id}/publish`, { version: 0 }, key);
                    navigate("/business/campaigns");
                  });
                }}>
                  <Field label="Promotion title" wide><input value={form.title} onChange={(e) => set("title", e.target.value)} required /></Field>
                  <Field label="Content type"><select value={form.contentType} onChange={(e) => set("contentType", e.target.value as BusinessUgcForm["contentType"])}><option value="Video">Video</option><option value="Photos">Photos</option></select></Field>
                  <Field label="Application closes"><input type="datetime-local" value={form.applicationCloses} onChange={(e) => set("applicationCloses", e.target.value)} required /></Field>
                  <Field label="Content due"><input type="datetime-local" value={form.dueDate} onChange={(e) => set("dueDate", e.target.value)} required /></Field>
                  <Field label="Creator payment (ETB)"><input type="number" min="0.01" step="0.01" value={form.creatorPayment} onChange={(e) => set("creatorPayment", e.target.value)} required /></Field>
                  <Field label="Location"><input value={form.location} onChange={(e) => set("location", e.target.value)} /></Field>
                  <Field label="Instructions" wide><textarea value={form.instructions} onChange={(e) => set("instructions", e.target.value)} required /></Field>
                  <ProductArrangementChoice value={form.productArrangement} onChange={(value) => set("productArrangement", value)} />
                  <fieldset className="field wide ugc-posting-choice"><legend>Posting</legend>
                    <label><input type="radio" name="ugc-posting" checked={!form.mustPost} onChange={() => set("mustPost", false)} /> Deliver content only</label>
                    <label><input type="radio" name="ugc-posting" checked={form.mustPost} onChange={() => set("mustPost", true)} /> Creator must post</label>
                  </fieldset>
                  {form.mustPost ? <PlatformCapacityPicker value={form.platforms} onChange={(value) => set("platforms", value)} />
                    : <Field label="Creators needed"><input type="number" min="1" max="100" step="1" value={form.creatorsNeeded} onChange={(e) => set("creatorsNeeded", e.target.value)} required /></Field>}
                  {selectedType ? (
                      <div className="field wide promotion-selected-type">
                        <span className="field-label">Promotion type</span>
                        <strong>{selectedType === "ugc-sales" ? "UGC + Sales" : "UGC"}</strong>
                        <Link className="text-link" to="/business/campaigns/new">Change</Link>
                      </div>
                    ) : (
                      <label className="field wide">
                        <span>
                          <input
                            type="checkbox"
                            checked={form.customerOffer}
                            onChange={(e) => set("customerOffer", e.target.checked)}
                          />{" "}
                          Add Customer discount sale
                        </span>
                      </label>
                    )}
                  {form.customerOffer && <><Field label="Customer discount %"><input type="number" min="0.01" max="100" step="0.0001" value={form.customerDiscount} onChange={(e) => set("customerDiscount", e.target.value)} required /></Field><Field label="Customer offer budget"><input type="number" min="0.01" step="0.01" value={form.customerRewardBudget} onChange={(e) => set("customerRewardBudget", e.target.value)} required /></Field></>}
                  {form.mustPost && form.platforms.length === 0 && <Notice error>Choose at least one social platform.</Notice>}
                  {form.customerOffer && config.customerOfferPlatformSalePercent === null && <Notice error>Customer discounts are unavailable for this Promotion right now.</Notice>}
                  {form.customerOffer && discount > 100 && <Notice error>Discount must be between 0 and 100%.</Notice>}
                  {!minimumBudgetMet && <Notice error>The Promotion budget is below the minimum required to publish.</Notice>}
                  {action.error && <Notice error>{action.error}</Notice>}
                  <div className="form-actions wide"><Button type="submit" disabled={action.busy || !valid}>{action.busy ? "Publishing…" : "Publish Promotion"}</Button></div>
                </form>
              </Section>
              {shortfall > 0 && <Notice error><Link to="/business/wallet">Add Funds</Link> before publishing. You need {amount(shortfall)} ETB more.</Notice>}
            </div>
          );
        }}
      </Resource>}</BusinessCreationGate>
    </>
  );
}
