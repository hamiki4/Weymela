import { useEffect, useState } from "react";
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
type AccountArea = "Customer" | "Creator" | "Business" | "Admin";
const areaTitle: Record<AccountArea, string> = { Customer: "Customers", Creator: "Creators", Business: "Businesses", Admin: "Admins" };
const areaPath: Record<AccountArea, string> = { Customer: "/admin/customers", Creator: "/admin/creators", Business: "/admin/businesses", Admin: "/admin/admins" };

export function AdminAccounts({ area }: { area: AccountArea }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const search = searchParams.get("search") ?? "";
  const status = searchParams.get("status") ?? "";
  const approval = searchParams.get("approval") ?? "";
  const [draftSearch, setDraftSearch] = useState(search);
  const [createOpen, setCreateOpen] = useState(false);
  const [created, setCreated] = useState<AccountPreauthorizationResult | null>(null);
  const path = `/admin/accounts?${new URLSearchParams({ role: area, ...(search ? { search } : {}), ...(status ? { status } : {}), ...(approval ? { approval } : {}) }).toString()}`;
  const resource = useResource<AdminAccountSummary[]>(path);
  const action = useAction();
  const [form, setForm] = useState({ role: area === "Admin" ? "OperationsAdmin" : area, email: "", phone: "", displayName: "", publicId: "", region: "", category: "", reason: "" });
  useEffect(() => { setForm({ role: area === "Admin" ? "OperationsAdmin" : area, email: "", phone: "", displayName: "", publicId: "", region: "", category: "", reason: "" }); setCreated(null); setCreateOpen(false); }, [area]);
  const submitSearch = () => setSearchParams({ ...(status ? { status } : {}), ...(approval ? { approval } : {}), ...(draftSearch ? { search: draftSearch } : {}) });
  return <>
    <PageHeader title={areaTitle[area]} action={<Button onClick={() => setCreateOpen((value) => !value)}>+ Create {area}</Button>} />
    {created && <Notice><strong>Preauthorization created.</strong> Activation instructions were sent to the target's verified email address. The target must complete the existing verification, password, device PIN, and activation flow before the account becomes active.<small>Expires {dateTime(created.expiresAtUtc)}</small></Notice>}
    {createOpen && <Section title={`Create ${area}`} description="Weymela sends activation instructions directly to the recipient. They set their own password and device PIN.">
      <form className="filter-grid three" onSubmit={(event) => { event.preventDefault(); void action.run(async key => { const result = await post<AccountPreauthorizationResult>("/admin/accounts/preauthorize", { ...form, phone: form.phone || null, publicId: form.publicId || null, region: form.region || null, category: form.category || null }, key); setCreated(result); setCreateOpen(false); resource.reload(); }); }}>
        {area === "Admin" && <Field label="Admin type"><select value={form.role} onChange={(event) => setForm({ ...form, role: event.target.value })}><option value="OperationsAdmin">Operations Admin</option><option value="PlatformAdmin">Platform Admin</option></select></Field>}
        <Field label="Email (required for a new identity)" help="Use an existing verified phone only when it already belongs to a Weymela identity with a verified email."><input type="email" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} /></Field>
        <Field label="Phone (optional)"><input type="tel" value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></Field>
        <Field label="Display name"><input required value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} /></Field>
        {(form.role === "Creator" || form.role === "Business") && <Field label="Public identifier"><input required value={form.publicId} onChange={(event) => setForm({ ...form, publicId: event.target.value })} /></Field>}
        {(form.role === "Creator" || form.role === "Business") && <Field label="Region"><input value={form.region} onChange={(event) => setForm({ ...form, region: event.target.value })} /></Field>}
        {(form.role === "Creator" || form.role === "Business") && <Field label="Category"><input value={form.category} onChange={(event) => setForm({ ...form, category: event.target.value })} /></Field>}
        {form.role === "PlatformAdmin" && <Field label="Reason"><textarea required maxLength={500} value={form.reason} onChange={(event) => setForm({ ...form, reason: event.target.value })} placeholder="Required for this administrative action" /></Field>}
        <div className="actions"><Button type="submit" disabled={action.busy}>{action.busy ? "Creating…" : "Create preauthorization"}</Button><Button variant="secondary" onClick={() => setCreateOpen(false)}>Cancel</Button></div>
      </form>{action.error && <Notice error>{action.error}</Notice>}
    </Section>}
    <Section title={`${areaTitle[area]} list`}>
      <div className="filter-grid three"><Field label="Search"><input type="search" value={draftSearch} onChange={(event) => setDraftSearch(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") submitSearch(); }} placeholder="Name or identifier" /></Field><Field label="Status"><select value={status} onChange={(event) => setSearchParams({ ...(search ? { search } : {}), ...(approval ? { approval } : {}), ...(event.target.value ? { status: event.target.value } : {}) })}><option value="">All statuses</option><option value="Pending">Pending</option><option value="Active">Active</option><option value="Suspended">Locked</option><option value="Disabled">Deactivated</option><option value="Closed">Closed</option><option value="Inactive">Inactive</option></select></Field>{(area === "Creator" || area === "Business") && <Field label="Approval"><select value={approval} onChange={(event) => setSearchParams({ ...(search ? { search } : {}), ...(status ? { status } : {}), ...(event.target.value ? { approval: event.target.value } : {}) })}><option value="">All approvals</option><option value="Pending">Pending</option><option value="Approved">Approved</option><option value="Rejected">Rejected</option></select></Field>}<div className="actions"><Button onClick={submitSearch}>Search</Button></div></div>
      <Resource resource={resource}>{(rows) => rows.length === 0 ? <Empty title={`No ${areaTitle[area].toLowerCase()} match`} message="Try another filter or search term." icon="people" /> : <div className="stack-list">{rows.map((row) => <AccountRow key={`${row.id}-${row.role}`} row={row} />)}</div>}</Resource>
    </Section>
  </>;
}

function AccountRow({ row }: { row: AdminAccountSummary }) {
  const detailId = row.status === "Pending" ? row.id : (row.userId ?? row.id);
  return <article className="amount-row account-row"><div><strong>{row.name}</strong><small>{row.role} · {row.safeIdentifier}{row.association ? ` · ${row.association}` : ""}</small></div><div className="account-row-status"><Badge status={displayStatus(row.status)} />{row.approvalState && <small>{row.approvalState}</small>}<Link className="button secondary" to={`/admin/accounts/${detailId}?role=${row.role}&mode=view`}>View</Link>{row.canManage && <Link className="button primary" to={`/admin/accounts/${detailId}?role=${row.role}&mode=manage`}>Manage</Link>}</div></article>;
}

export function AdminAccountDetail() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const requestedRole = searchParams.get("role");
  const resource = useResource<AccountDetail>(`/admin/accounts/${id}${requestedRole ? `?role=${encodeURIComponent(requestedRole)}` : ""}`);
  const action = useAction();
  const [reason, setReason] = useState("");
  const [confirmClose, setConfirmClose] = useState(false);
  const manage = searchParams.get("mode") === "manage";
  return <>
    <Link className="back-link" to={areaPath[resource.data?.account.role === "Customer" ? "Customer" : resource.data?.account.role === "Creator" ? "Creator" : resource.data?.account.role === "Business" ? "Business" : "Admin"]}>← Back to list</Link>
    <Resource resource={resource}>{(detail) => <AccountDetailContent detail={detail} manage={manage} reason={reason} setReason={setReason} confirmClose={confirmClose} setConfirmClose={setConfirmClose} action={action} reload={resource.reload} />}</Resource>
  </>;
}

function AccountDetailContent({ detail, manage, reason, setReason, confirmClose, setConfirmClose, action, reload }: { detail: AccountDetail; manage: boolean; reason: string; setReason: (value: string) => void; confirmClose: boolean; setConfirmClose: (value: boolean) => void; action: ReturnType<typeof useAction>; reload: () => void }) {
  const account = detail.account;
  const roleData = detail.roleData as Record<string, unknown> | null;
  const targetId = account.userId ?? account.id;
  const lifecycle = (next: string) => { if (!reason.trim() || (next === "close" && !confirmClose)) return; void action.run(async key => { if (account.status === "Pending") await post(`/admin/accounts/preauthorizations/${account.id}/cancel`, { action: "cancel", reason: reason.trim() }, key); else await post(`/admin/accounts/${targetId}/lifecycle`, { action: next, reason: reason.trim(), confirmClose: next === "close" }, key); setReason(""); setConfirmClose(false); reload(); }); };
  return <>
    <PageHeader eyebrow={`${account.role} · ${account.safeIdentifier}`} title={account.name} description={`${displayStatus(account.status)}${account.approvalState ? ` · ${account.approvalState}` : ""}`} action={manage && account.canManage ? <Badge status={displayStatus(account.status)} /> : account.canManage ? <Link className="button secondary" to={`/admin/accounts/${account.id}?role=${account.role}&mode=manage`}>Manage</Link> : undefined} />
    <div className="two-column">
      <Section title="Overview"><div className="stack-list">{detail.roles.map((role) => <div className="amount-row" key={role}><strong>{role}</strong><Badge status={displayStatus(account.status)} /></div>)}{detail.profiles.map((profile) => <div className="amount-row" key={profile.subjectId}><div><strong>{profile.displayName}</strong><small>{profile.role} · {profile.publicId}{profile.region ? ` · ${profile.region}` : ""}</small></div><Badge status={profile.active ? "Active" : "Inactive"} /></div>)}</div>{manage && account.canManage && <><Field label="Reason for lifecycle action"><textarea value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} placeholder="Required for this change" /></Field>{account.status !== "Pending" && <Field label="Confirm terminal closure" help="Closing denies future access and keeps financial and security history."><input type="checkbox" checked={confirmClose} onChange={(event) => setConfirmClose(event.target.checked)} /></Field>}<div className="actions">{account.status === "Pending" ? <Button disabled={action.busy || !reason.trim()} onClick={() => lifecycle("cancel")}>Cancel preauthorization</Button> : <>{account.status === "Active" && <Button disabled={action.busy || !reason.trim()} onClick={() => lifecycle("lock")}>Lock</Button>}{account.status === "Suspended" && <Button disabled={action.busy || !reason.trim()} onClick={() => lifecycle("unlock")}>Unlock</Button>}{(account.status === "Active" || account.status === "Suspended") && <Button variant="secondary" disabled={action.busy || !reason.trim()} onClick={() => lifecycle("deactivate")}>Deactivate</Button>}{account.status === "Disabled" && <Button variant="secondary" disabled={action.busy || !reason.trim()} onClick={() => lifecycle("reactivate")}>Reactivate</Button>}{["Active", "Suspended", "Disabled"].includes(account.status) && <Button variant="secondary" disabled={action.busy || !reason.trim() || !confirmClose} onClick={() => lifecycle("close")}>Close Account</Button>}</>}</div>{action.error && <Notice error>{action.error}</Notice>}</>}</Section>
      <Section title="Role-specific information">{roleData ? <div className="stack-list">{Object.entries(roleData).map(([key, value]) => <div className="amount-row" key={key}><span>{label(key)}</span><strong>{displayValue(key, value)}</strong></div>)}</div> : <Empty title="No role projection" message="This account has no role-specific projection available." />}</Section>
    </div>
    {detail.transactions.length > 0 && <Section title="Commerce activity"><div className="stack-list">{detail.transactions.map((transaction) => <div className="amount-row" key={transaction.id}><div><strong>{transaction.purchaseAmount.toLocaleString()} {transaction.currency}</strong><small>{dateTime(transaction.occurredAtUtc)} · {transaction.status}</small></div><span>{transaction.businessId}</span></div>)}</div></Section>}
    {detail.profiles.find(x => x.role === "Business")?.subjectId && <BusinessCashiers businessId={detail.profiles.find(x => x.role === "Business")!.subjectId} />}
  </>;
}

function BusinessCashiers({ businessId }: { businessId: string }) {
  const resource = useResource<AdminAccountSummary[]>(`/admin/accounts/businesses/${businessId}/cashiers`);
  return <Section title="Cashier Management" description="Cashiers are invited and managed by this Business.">
    <Resource resource={resource}>{rows => rows.length ? <div className="stack-list">{rows.map(row => <AccountRow key={row.id} row={row} />)}</div> : <Empty title="No Cashiers" message="This Business has not added Cashiers." />}</Resource>
  </Section>;
}

function label(value: string) { return value.replace(/([A-Z])/g, " $1").replace(/^./, (value) => value.toUpperCase()); }
function displayStatus(status: string) { return status === "Suspended" ? "Locked" : status === "Disabled" ? "Deactivated" : status; }
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
