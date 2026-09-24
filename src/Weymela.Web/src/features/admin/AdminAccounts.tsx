import { useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  AccountPreauthorizationResult,
  AdminAccountDetail as AccountDetail,
  AdminAccountSummary,
} from "../../api/types";
import {
  ActionLink,
  Badge,
  Button,
  Empty,
  Field,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { dateTime } from "../../ui/format";

const tabs = [
  ["", "All"],
  ["Customer", "Customers"],
  ["Creator", "Creators"],
  ["Business", "Businesses"],
  ["Cashier", "Cashiers"],
  ["OperationsAdmin", "Operations Admins"],
  ["PlatformAdmin", "Platform Admins"],
] as const;

export function AdminAccounts() {
  const [searchParams, setSearchParams] = useSearchParams();
  const role = searchParams.get("role") ?? "";
  const search = searchParams.get("search") ?? "";
  const [draftSearch, setDraftSearch] = useState(search);
  const [createOpen, setCreateOpen] = useState(false);
  const [created, setCreated] = useState<AccountPreauthorizationResult | null>(null);
  const path = `/admin/accounts?${new URLSearchParams({ ...(role ? { role } : {}), ...(search ? { search } : {}) }).toString()}`;
  const resource = useResource<AdminAccountSummary[]>(path);
  const action = useAction();
  const [form, setForm] = useState({ role: "Customer", email: "", phone: "", displayName: "", publicId: "", region: "", category: "" });
  const changeFilter = (nextRole: string) => setSearchParams(nextRole ? { role: nextRole, ...(search ? { search } : {}) } : search ? { search } : {});
  const submitSearch = () => setSearchParams({ ...(role ? { role } : {}), ...(draftSearch ? { search: draftSearch } : {}) });
  return <>
    <PageHeader eyebrow="Platform control · identity and access" title="Accounts" description="Global safe visibility and account preauthorization. Credentials and security material are never shown here." action={<Button onClick={() => setCreateOpen((value) => !value)}>+ Create Account</Button>} />
    {created && <Notice><strong>Preauthorization created.</strong> Give the target user the one-time activation secret below. It is shown once and is not stored in Weymela.<div className="activation-secret"><code>{created.oneTimeActivationSecret ?? "Unavailable — create a new request."}</code><small>Expires {dateTime(created.expiresAtUtc)}</small></div></Notice>}
    {createOpen && <Section title="Create / preauthorize account" description="The target establishes their own password and device PIN using the existing security flow.">
      <form className="filter-grid three" onSubmit={(event) => { event.preventDefault(); void action.run(async key => { const result = await post<AccountPreauthorizationResult>("/admin/accounts/preauthorize", { ...form, phone: form.phone || null, publicId: form.publicId || null, region: form.region || null, category: form.category || null }, key); setCreated(result); setCreateOpen(false); resource.reload(); }); }}>
        <Field label="Account type"><select value={form.role} onChange={(event) => setForm({ ...form, role: event.target.value })}><option value="Customer">Customer</option><option value="Creator">Creator</option><option value="Business">Business</option><option value="OperationsAdmin">Operations Admin</option></select></Field>
        <Field label="Email (required for a new identity)" help="Use an existing verified phone only when it already belongs to a Weymela identity with a verified email."><input type="email" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} /></Field>
        <Field label="Phone (optional)"><input type="tel" value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></Field>
        <Field label="Display name"><input required value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} /></Field>
        {form.role !== "Customer" && form.role !== "OperationsAdmin" && <Field label="Public identifier"><input required value={form.publicId} onChange={(event) => setForm({ ...form, publicId: event.target.value })} /></Field>}
        {form.role !== "Customer" && form.role !== "OperationsAdmin" && <Field label="Region"><input value={form.region} onChange={(event) => setForm({ ...form, region: event.target.value })} /></Field>}
        {form.role !== "Customer" && form.role !== "OperationsAdmin" && <Field label="Category"><input value={form.category} onChange={(event) => setForm({ ...form, category: event.target.value })} /></Field>}
        <div className="actions"><Button type="submit" disabled={action.busy}>{action.busy ? "Creating…" : "Create preauthorization"}</Button><Button variant="secondary" onClick={() => setCreateOpen(false)}>Cancel</Button></div>
      </form>{action.error && <Notice error>{action.error}</Notice>}
    </Section>}
    <Section title="Account directory" description="Search is applied by the server to normalized identity and safe profile fields.">
      <div className="tabs" role="tablist" aria-label="Account role filter">{tabs.map(([value, label]) => <button key={value} type="button" role="tab" aria-selected={role === value} onClick={() => changeFilter(value)}>{label}</button>)}</div>
      <div className="filter-grid two"><Field label="Search"><input type="search" value={draftSearch} onChange={(event) => setDraftSearch(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") submitSearch(); }} placeholder="Name, safe identifier, association" /></Field><div className="actions"><Button onClick={submitSearch}>Search</Button></div></div>
      <Resource resource={resource}>{(rows) => rows.length === 0 ? <Empty title="No accounts match" message="Try another role or search term." icon="people" /> : <div className="stack-list">{rows.map((row) => <AccountRow key={`${row.id}-${row.role}`} row={row} />)}</div>}</Resource>
    </Section>
  </>;
}

function AccountRow({ row }: { row: AdminAccountSummary }) {
  const detailId = row.status === "Pending" ? row.id : (row.userId ?? row.id);
  return <article className="amount-row account-row"><div><strong>{row.name}</strong><small>{row.role} · {row.safeIdentifier}{row.association ? ` · ${row.association}` : ""}</small></div><div className="account-row-status"><Badge status={row.status} />{row.approvalState && <small>{row.approvalState}</small>}<Link className="button secondary" to={`/admin/accounts/${detailId}?mode=view`}>View</Link>{row.canManage && <Link className="button primary" to={`/admin/accounts/${detailId}?mode=manage`}>Manage</Link>}</div></article>;
}

export function AdminAccountDetail() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const resource = useResource<AccountDetail>(`/admin/accounts/${id}`);
  const action = useAction();
  const [reason, setReason] = useState("");
  const manage = searchParams.get("mode") === "manage";
  return <>
    <Link className="back-link" to="/admin/accounts">← Accounts</Link>
    <Resource resource={resource}>{(detail) => <AccountDetailContent detail={detail} manage={manage} reason={reason} setReason={setReason} action={action} reload={resource.reload} />}</Resource>
  </>;
}

function AccountDetailContent({ detail, manage, reason, setReason, action, reload }: { detail: AccountDetail; manage: boolean; reason: string; setReason: (value: string) => void; action: ReturnType<typeof useAction>; reload: () => void }) {
  const account = detail.account;
  const roleData = detail.roleData as Record<string, unknown> | null;
  const targetId = account.userId ?? account.id;
  const lifecycle = (next: string) => { if (!reason.trim()) return; void action.run(async key => { if (account.status === "Pending") await post(`/admin/accounts/preauthorizations/${account.id}/cancel`, { action: "cancel", reason: reason.trim() }, key); else await post(`/admin/accounts/${targetId}/lifecycle`, { action: next, reason: reason.trim() }, key); setReason(""); reload(); }); };
  return <>
    <PageHeader eyebrow={`${account.role} · ${account.safeIdentifier}`} title={account.name} description={`${account.status}${account.approvalState ? ` · ${account.approvalState}` : ""}`} action={manage && account.canManage ? <Badge status={account.status} /> : account.canManage ? <Link className="button secondary" to={`/admin/accounts/${account.id}?mode=manage`}>Manage</Link> : undefined} />
    <div className="two-column">
      <Section title="Overview"><div className="stack-list">{detail.roles.map((role) => <div className="amount-row" key={role}><strong>{role}</strong><Badge status={account.status} /></div>)}{detail.profiles.map((profile) => <div className="amount-row" key={profile.subjectId}><div><strong>{profile.displayName}</strong><small>{profile.role} · {profile.publicId}{profile.region ? ` · ${profile.region}` : ""}</small></div><Badge status={profile.active ? "Active" : "Inactive"} /></div>)}</div>{manage && account.canManage && <><Field label="Reason for lifecycle action"><textarea value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} placeholder="Required and recorded in the audit history" /></Field><div className="actions">{account.status === "Pending" ? <Button disabled={action.busy} onClick={() => lifecycle("cancel")}>Cancel preauthorization</Button> : <><Button disabled={action.busy || account.status === "Suspended"} onClick={() => lifecycle("suspend")}>Suspend</Button><Button variant="secondary" disabled={action.busy || account.status === "Disabled"} onClick={() => lifecycle("disable")}>Disable</Button><Button variant="secondary" disabled={action.busy || account.status === "Active"} onClick={() => lifecycle("reactivate")}>Reactivate</Button></>}</div>{action.error && <Notice error>{action.error}</Notice>}</>}</Section>
      <Section title="Role-specific information">{roleData ? <div className="stack-list">{Object.entries(roleData).map(([key, value]) => <div className="amount-row" key={key}><span>{label(key)}</span><strong>{displayValue(key, value)}</strong></div>)}</div> : <Empty title="No role projection" message="This account has no role-specific projection available." />}</Section>
    </div>
    {detail.transactions.length > 0 && <Section title="Commerce activity"><div className="stack-list">{detail.transactions.map((transaction) => <div className="amount-row" key={transaction.id}><div><strong>{transaction.purchaseAmount.toLocaleString()} {transaction.currency}</strong><small>{dateTime(transaction.occurredAtUtc)} · {transaction.status}</small></div><span>{transaction.businessId}</span></div>)}</div></Section>}
    <Section title="Audit history" description="Safe structured activity only. Credential and token material is excluded.">{detail.audit.length === 0 ? <Empty title="No recorded audit activity" message="Administrative activity will appear here when it exists." icon="document" /> : <div className="stack-list">{detail.audit.map((item) => <div className="amount-row" key={item.id}><div><strong>{item.action}</strong><small>{item.operation} · {dateTime(item.occurredAtUtc)}</small></div><small>{item.targetRole ?? "Account"}</small></div>)}</div>}</Section>
  </>;
}

function label(value: string) { return value.replace(/([A-Z])/g, " $1").replace(/^./, (value) => value.toUpperCase()); }
function displayValue(key: string, value: unknown) {
  if (Array.isArray(value)) {
    if (value.length === 0) return "None recorded";
    return value.map((entry) => typeof entry === "object" && entry !== null
      ? Object.entries(entry as Record<string, unknown>).filter(([field]) => field !== "id").map(([field, item]) => `${label(field)}: ${String(item ?? "—")}`).join(", ")
      : String(entry)).join(" · ");
  }
  return typeof value === "number" && key.toLowerCase().includes("wallet")
    ? value.toLocaleString(undefined, { maximumFractionDigits: 2 })
    : String(value ?? "—");
}
