import { useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { post, request, useAction, useResource } from "../../api/client";
import type { UgcAssignment, UgcCard, UgcDetail, UgcPricing, Wallet } from "../../api/types";
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
import { amount, count, date, promotionStatusLabel } from "../../ui/format";

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
  const manageOpen = false;
  return (
    <article className="data-card business-promotion-card" data-status={item.status}>
      <div className="card-head"><div><small>{item.customerOfferEnabled ? "UGC + Sales" : "UGC"}</small><h3>{item.title}</h3></div><Badge status={item.status} label={promotionStatusLabel(item.status)} /></div>
      <dl className="promotion-summary-list">
        <div><dt>Creator payment</dt><dd>{amount(item.creatorPayment)} ETB</dd></div>
        <div><dt>Content due</dt><dd>{date(item.dueDateUtc)}</dd></div>
        {item.creatorsNeeded > 0 && <div><dt>Creators</dt><dd>{item.approvedCreators}/{item.creatorsNeeded}</dd></div>}
      </dl>
      <Link className="button secondary business-promotion-manage" to={`/business/ugc/${item.id}`}>Manage</Link>
      {manageOpen && <section className="business-promotion-management" aria-label={`${item.title} management`}>
        <ProductArrangement provided={item.productProvided} purchase={item.creatorMustPurchase} />
        {item.customerOfferEnabled && item.customerDiscountPercent !== undefined && <p className="fine-print">Customer earns {amount(item.customerDiscountPercent)}% cashback.</p>}
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
              <label className="field wide"><span><input type="checkbox" checked={editOffer} onChange={event => setEditOffer(event.target.checked)} /> Customer cashback sale</span></label>
              {editOffer && <><Field label="Customer Cashback %"><input type="number" min="0.01" max="100" step="0.0001" value={editDiscount} onChange={event => setEditDiscount(event.target.value)} required /></Field><Field label="Cashback funding"><input type="number" min="0.01" step="0.01" value={editOfferBudget} onChange={event => setEditOfferBudget(event.target.value)} required /></Field></>}
            </>}
            <Field label="Slogan"><input value={editSlogan} onChange={event => setEditSlogan(event.target.value)} maxLength={160} /></Field>
            <Field label="Location"><input value={editLocation} onChange={event => setEditLocation(event.target.value)} maxLength={160} /></Field>
            <Field label="Instructions" wide><textarea value={editInstructions} onChange={event => setEditInstructions(event.target.value)} maxLength={4000} rows={5} required /></Field>
            {editAction.error && <Notice error>{editAction.error}</Notice>}
            <div className="form-actions wide"><Button type="submit" disabled={editAction.busy || !editInstructions.trim()}>{editAction.busy ? "Saving…" : "Save changes"}</Button></div>
          </form>
        </Dialog>
        </>}
      </section>}
      {action.error && <Notice error>{action.error}</Notice>}
    </article>
  );
}

function UgcAssignmentReview({ row, reload }: { row: UgcAssignment; reload: () => void }) {
  const action = useAction();
  const [feedback, setFeedback] = useState("");
  const review = (decision: "approve" | "changes" | "reject") => void action.run(async (key) => {
    await post(`/business/ugc/assignments/${row.id}/review/${decision}`, { reason: feedback.trim() || null }, key);
    setFeedback(""); reload();
  });
  return <article className="business-collaboration-card">
    <div className="card-head"><div className="business-ugc-creator-row"><CreatorAvatar name={row.creator} path={`/business/creator-photos/${row.creatorId}`} /><div><strong>{row.creator}</strong><small>Revision {row.contentRevisionNumber ?? 1}</small></div></div><Badge status={row.status} /></div>
    {row.selectedPlatform && <p className="fine-print"><strong>{row.selectedPlatform}</strong>{row.selectedSocialProfileUrl && <> · <a href={row.selectedSocialProfileUrl} target="_blank" rel="noopener noreferrer">View selected profile</a></>}</p>}
    {row.reviewMediaUrl && (row.reviewMediaContentType?.startsWith("image/")
      ? <img className="private-review-media" src={row.reviewMediaUrl} alt={`Revision ${row.contentRevisionNumber ?? 1} review`} />
      : <video className="private-review-media" controls preload="metadata" src={row.reviewMediaUrl}>Private review video</video>)}
    {row.feedback && <Notice>{row.feedback}</Notice>}
    {row.status === "Submitted" && <div className="business-review-actions">
      <Field label="Feedback (required for changes)"><textarea value={feedback} onChange={(event) => setFeedback(event.target.value)} maxLength={2000} rows={3} /></Field>
      <div className="actions"><Button disabled={action.busy} onClick={() => review("approve")}>Approve Video</Button><Button variant="secondary" disabled={action.busy || !feedback.trim()} onClick={() => review("changes")}>Request Changes</Button><Button variant="quiet" disabled={action.busy} onClick={() => review("reject")}>Reject</Button></div>
    </div>}
    {row.status === "Approved" && !row.selectedPlatform && <Notice>Content approved. Delivery-only work is complete and has no Customer offer.</Notice>}
    {row.status === "Approved" && row.selectedPlatform && !row.publication && <Notice>Content approved. Waiting for the Creator to publish.</Notice>}
    {row.publication && <p className="fine-print">Publication: {row.publication.wentLiveAtUtc ? "Live" : row.publication.verificationLabel}</p>}
    {action.error && <Notice error>{action.error}</Notice>}
    <ol className="collaboration-timeline" aria-label="Activity">
      <li>Business approved Creator</li>
      {row.contentRevisionNumber && <li>Creator submitted revision {row.contentRevisionNumber}</li>}
      {row.status === "ChangesRequested" && <li>Business requested changes</li>}
      {row.status === "Approved" && <li>Business approved revision {row.contentRevisionNumber ?? 1}</li>}
      {row.publication && <li>Creator submitted the public post for verification</li>}
      {row.publication?.wentLiveAtUtc && <li>Creator went Live</li>}
    </ol>
  </article>;
}

export function BusinessUgcDetail() {
  const { id } = useParams();
  const detail = useResource<UgcDetail>(`/business/ugc/${id}`);
  const wallet = useResource<Wallet>("/business/wallet");
  const action = useAction();
  const reload = () => { detail.reload(); wallet.reload(); };
  return <Resource resource={detail}>{data => {
    const item = data.opportunity;
    const required = item.requiredFunding ?? 0;
    const shortfall = Math.max(0, required - (wallet.data?.available ?? 0));
    return <>
      <Link className="back-link" to="/business/campaigns">← Promotions</Link>
      <PageHeader eyebrow={item.customerOfferEnabled ? "UGC + Sales" : "UGC"} title={item.title}
        description={`Content due ${date(item.dueDateUtc)}`}
        action={<Badge status={item.status} label={promotionStatusLabel(item.status)} />} />
      {item.status === "Draft" && <div className="callout">
        <div><h2>Draft</h2><p>Post this Promotion when confirmed funds are available.</p></div>
        <Button disabled={action.busy || !wallet.data || shortfall > 0} onClick={() => void action.run(async key => {
          await post(`/business/ugc/${item.id}/publish`, { version: item.version }, key); reload();
        })}>{action.busy ? "Posting…" : "Post to Creators"}</Button>
        {wallet.data && shortfall > 0 && <Notice><Link to="/business/wallet">Add Funds</Link> before posting. You need {amount(shortfall)} ETB more.</Notice>}
      </div>}
      {action.error && <Notice error>{action.error}</Notice>}
      <div className="two-column business-collaboration-workspace">
        <Section title="Brief">
          <p className="preserve-lines">{data.instructions}</p>
          <dl className="detail-list">
            <div><dt>Creator payment</dt><dd>{amount(item.creatorPayment)} ETB</dd></div>
            <div><dt>Content due</dt><dd>{date(item.dueDateUtc)}</dd></div>
            {item.location && <div><dt>Location</dt><dd>{item.location}</dd></div>}
            <div><dt>Product</dt><dd>{data.productProvided ? "Provided by Business" : data.creatorMustPurchase ? "Creator purchases" : "Not selected"}</dd></div>
            {!!item.platformRequirements.length && <div><dt>Publish on</dt><dd>{item.platformRequirements.map(row => row.platform).join(", ")}</dd></div>}
          </dl>
        </Section>
        <Section title="Creator requests">
          {data.requests.length ? <div className="card-stack">{data.requests.map(row => <article className="business-collaboration-card" key={row.id}>
            <div className="card-head"><div className="business-ugc-creator-row"><CreatorAvatar name={row.creator} path={`/business/creator-photos/${row.creatorId}`} /><strong>{row.creator}</strong></div><Badge status={row.status} /></div>
            {row.socialProfile ? <div className="creator-social-review"><a href={row.socialProfile.profileUrl} target="_blank" rel="noopener noreferrer">{row.socialProfile.platform} profile</a>
              <small>{row.socialProfile.verificationStatus === "Verified" && row.socialProfile.verifiedAudience != null
                ? `${count(row.socialProfile.verifiedAudience)} Admin-verified audience`
                : `${count(row.socialProfile.selfReportedAudience)} self-reported audience · Not independently verified`}</small></div>
              : <p className="fine-print">Delivery-only request · no social profile required</p>}
            {row.status === "Pending" && <div className="actions"><Button disabled={action.busy} onClick={() => void action.run(async key => {
              await post(`/business/ugc/requests/${row.id}/approve`, { reason: null }, key); reload();
            })}>Approve</Button><Button variant="secondary" disabled={action.busy} onClick={() => void action.run(async key => {
              await post(`/business/ugc/requests/${row.id}/reject`, { reason: null }, key); reload();
            })}>Reject</Button></div>}
          </article>)}</div> : <Empty title="No Creator requests yet." />}
        </Section>
      </div>
      <Section title="Creator work">
        {data.assignments.length ? <div className="card-stack">{data.assignments.map(row => <UgcAssignmentReview key={row.id} row={row} reload={reload} />)}</div>
          : <Empty title="No approved Creators yet." />}
      </Section>
    </>;
  }}</Resource>;
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
                    navigate(`/business/ugc/${created.id}`);
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
                          Add Customer cashback sale
                        </span>
                      </label>
                    )}
                  {form.customerOffer && <><Field label="Customer cashback %"><input type="number" min="0.01" max="100" step="0.0001" value={form.customerDiscount} onChange={(e) => set("customerDiscount", e.target.value)} required /></Field><Field label="Customer cashback budget"><input type="number" min="0.01" step="0.01" value={form.customerRewardBudget} onChange={(e) => set("customerRewardBudget", e.target.value)} required /></Field></>}
                  {form.mustPost && form.platforms.length === 0 && <Notice error>Choose at least one social platform.</Notice>}
                  {form.customerOffer && config.customerOfferPlatformSalePercent === null && <Notice error>Customer cashback is unavailable for this Promotion right now.</Notice>}
                  {form.customerOffer && discount > 100 && <Notice error>Cashback must be between 0 and 100%.</Notice>}
                  {!minimumBudgetMet && <Notice error>The Promotion budget is below the minimum required to publish.</Notice>}
                  {action.error && <Notice error>{action.error}</Notice>}
                  <div className="form-actions wide"><Button type="submit" disabled={action.busy || !valid}>{action.busy ? "Saving…" : "Save Draft"}</Button></div>
                </form>
              </Section>
              {shortfall > 0 && <Notice>You can save the Draft. <Link to="/business/wallet">Add Funds</Link> before posting it to Creators. You need {amount(shortfall)} ETB more.</Notice>}
            </div>
          );
        }}
      </Resource>}</BusinessCreationGate>
    </>
  );
}
