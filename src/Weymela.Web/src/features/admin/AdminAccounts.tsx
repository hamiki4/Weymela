import { useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { post, useAction, useResource } from "../../api/client";
import type {
  AccountPreauthorizationResult,
  AdminAccountDetail as AccountDetail,
  AdminAccountSummary,
} from "../../api/types";
import {
  Badge,
  Button,
  Empty,
  Dialog,
  Field,
  Notice,
  PageHeader,
  Resource,
  DataTable,
  Section,
} from "../../ui/components";
import { date, dateTime } from "../../ui/format";
type AccountArea = "Customer" | "Creator" | "Business" | "Admin";
const areaTitle: Record<AccountArea, string> = { Customer: "Customers", Creator: "Creators", Business: "Businesses", Admin: "Admins" };
const areaPath: Record<AccountArea, string> = { Customer: "/admin/customers", Creator: "/admin/creators", Business: "/admin/businesses", Admin: "/admin/admins" };

export function AdminAccounts({ area }: { area: AccountArea }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const search = searchParams.get("search") ?? "";
  const status = searchParams.get("status") ?? "";
  const role = searchParams.get("adminRole") ?? "";
  const [draftSearch, setDraftSearch] = useState(search);
  const [selected, setSelected] = useState<{ row: AdminAccountSummary; command: string } | null>(null);
  const [reason, setReason] = useState("");
  const [confirmClose, setConfirmClose] = useState(false);
  const action = useAction();
  const path = `/admin/accounts?${new URLSearchParams({ role: area, ...(search ? { search } : {}), ...(status ? { status } : {}) }).toString()}`;
  const resource = useResource<AdminAccountSummary[]>(path);
  useEffect(() => { setDraftSearch(search); }, [search, area]);
  const updateFilters = (next: { search?: string; status?: string; adminRole?: string }) =>
    setSearchParams({ ...(next.search ? { search: next.search } : {}), ...(next.status ? { status: next.status } : {}), ...(next.adminRole ? { adminRole: next.adminRole } : {}) });
  const runLifecycle = () => {
    if (!selected || !reason.trim() || (selected.command === "close" && !confirmClose)) return;
    const { row, command } = selected;
    void action.run(async key => {
      if (row.status === "Pending") await post(`/admin/accounts/preauthorizations/${row.id}/cancel`, { action: "cancel", reason: reason.trim() }, key);
      else await post(`/admin/accounts/${row.userId ?? row.id}/lifecycle`, { action: command, reason: reason.trim(), confirmClose: command === "close" }, key);
      setSelected(null); setReason(""); setConfirmClose(false); resource.reload();
    });
  };
  return <div className="admin-page">
    <PageHeader title={areaTitle[area]} action={<Link className="button primary" to={`${areaPath[area]}/new`}>+ Create {area}</Link>} />
    {searchParams.get("created") && <Notice>Preauthorization created. Activation instructions were sent to the target. The account appears below as Pending until activation.</Notice>}
    <div className="admin-toolbar">
      <form onSubmit={event => { event.preventDefault(); updateFilters({ search: draftSearch, status, adminRole: role }); }}>
        <input aria-label={`Search ${areaTitle[area]}`} type="search" value={draftSearch} onChange={event => setDraftSearch(event.target.value)} placeholder={`Search ${areaTitle[area].toLowerCase()}`} />
        <Button type="submit" variant="secondary">Search</Button>
      </form>
      <label>Status <select aria-label="Status" value={status} onChange={event => updateFilters({ search, status: event.target.value, adminRole: role })}><option value="">All statuses</option><option value="Pending">Pending</option><option value="Active">Active</option><option value="Suspended">Locked</option><option value="Disabled">Deactivated</option><option value="Closed">Closed</option><option value="Inactive">Inactive</option></select></label>
      {area === "Admin" && <label>Role <select aria-label="Admin role" value={role} onChange={event => updateFilters({ search, status, adminRole: event.target.value })}><option value="">All roles</option><option value="PlatformAdmin">Platform Admin</option><option value="OperationsAdmin">Operations Admin</option></select></label>}
    </div>
    <Resource resource={resource}>{rows => {
      const filtered = area === "Admin" && role ? rows.filter(row => row.role === role) : rows;
      return <DataTable rows={filtered} rowKey={row => `${row.id}-${row.role}`} label={areaTitle[area]} columns={[
        { label: area, cell: row => <strong>{row.name}</strong> },
        { label: area === "Admin" ? "Role" : "Contact", cell: row => area === "Admin" ? roleName(row.role) : row.safeIdentifier },
        { label: "Status", cell: row => <Badge status={displayStatus(row.status)} /> },
        { label: "Joined", cell: row => row.joinedAtUtc ? date(row.joinedAtUtc) : "—" },
        { label: "Actions", cell: row => <AccountActions row={row} onChoose={command => setSelected({ row, command })} /> },
      ]} card={row => <div className="admin-mobile-row"><div><strong>{row.name}</strong><small>{area === "Admin" ? roleName(row.role) : row.safeIdentifier}</small></div><div className="admin-mobile-row-meta"><Badge status={displayStatus(row.status)} /><AccountActions row={row} onChoose={command => setSelected({ row, command })} /></div>{row.joinedAtUtc && <small>Joined {date(row.joinedAtUtc)}</small>}</div>} empty={<Empty title={`No ${areaTitle[area].toLowerCase()} match`} message="Try another filter or search term." icon="people" />} />;
    }}</Resource>
    <Dialog title={selected ? `${commandLabel(selected.command)} ${selected.row.name}` : "Account action"} open={!!selected} onClose={() => { if (!action.busy) { setSelected(null); setReason(""); setConfirmClose(false); } }}>
      {selected && <form onSubmit={event => { event.preventDefault(); runLifecycle(); }}><Field label="Reason"><textarea required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></Field>{selected.command === "close" && <label className="check-line"><input type="checkbox" checked={confirmClose} onChange={event => setConfirmClose(event.target.checked)} /> Confirm terminal closure. Financial and security history remains.</label>}{action.error && <Notice error>{action.error}</Notice>}<div className="actions"><Button type="submit" disabled={action.busy || !reason.trim() || (selected.command === "close" && !confirmClose)}>{action.busy ? "Saving…" : commandLabel(selected.command)}</Button><Button variant="secondary" onClick={() => setSelected(null)} disabled={action.busy}>Cancel</Button></div></form>}
    </Dialog>
  </div>;
}

function commandLabel(command: string) { return command === "close" ? "Close Account" : command === "cancel" ? "Cancel preauthorization" : command[0].toUpperCase() + command.slice(1); }
function roleName(role: string) { return role === "PlatformAdmin" ? "Platform Admin" : role === "OperationsAdmin" ? "Operations Admin" : role; }
function AccountActions({ row, onChoose }: { row: AdminAccountSummary; onChoose: (command: string) => void }) {
  if (!row.canManage) return <span className="muted">—</span>;
  const commands = row.status === "Pending" ? ["cancel"] : row.status === "Active" ? ["lock", "deactivate", "close"] : row.status === "Suspended" ? ["unlock", "deactivate", "close"] : row.status === "Disabled" ? ["reactivate", "close"] : [];
  return commands.length ? <details className="admin-action-menu"><summary aria-label={`Actions for ${row.name}`}>•••</summary><div role="menu">{commands.map(command => <button key={command} type="button" role="menuitem" onClick={event => { event.currentTarget.closest("details")?.removeAttribute("open"); onChoose(command); }}>{commandLabel(command)}</button>)}</div></details> : <span className="muted">—</span>;
}

export function AdminAccountCreate({ area }: { area: AccountArea }) {
  const navigate = useNavigate();
  const action = useAction();
  const [form, setForm] = useState({ role: area === "Admin" ? "OperationsAdmin" : area, email: "", phone: "", displayName: "", publicId: "", region: "", category: "", reason: "" });
  useEffect(() => { setForm({ role: area === "Admin" ? "OperationsAdmin" : area, email: "", phone: "", displayName: "", publicId: "", region: "", category: "", reason: "" }); }, [area]);
  return <div className="admin-page admin-create"><Link className="back-link" to={areaPath[area]}>← {areaTitle[area]}</Link><PageHeader title={`Create ${area}`} />
    <Section title="Account invitation" description="The recipient completes verification, password setup, and device PIN before activation.">
      <form className="filter-grid three" onSubmit={event => { event.preventDefault(); void action.run(async key => { const result = await post<AccountPreauthorizationResult>("/admin/accounts/preauthorize", { ...form, phone: form.phone || null, publicId: form.publicId || null, region: form.region || null, category: form.category || null }, key); navigate(`${areaPath[area]}?created=${result.preauthorizationId}`); }); }}>
        {area === "Admin" && <Field label="Admin type"><select value={form.role} onChange={event => setForm({ ...form, role: event.target.value })}><option value="OperationsAdmin">Operations Admin</option><option value="PlatformAdmin">Platform Admin</option></select></Field>}
        <Field label="Email" help="A new identity requires an email. Existing verified phone identities may use their verified email."><input type="email" value={form.email} onChange={event => setForm({ ...form, email: event.target.value })} /></Field>
        <Field label="Phone (optional)"><input type="tel" value={form.phone} onChange={event => setForm({ ...form, phone: event.target.value })} /></Field>
        <Field label="Display name"><input required value={form.displayName} onChange={event => setForm({ ...form, displayName: event.target.value })} /></Field>
        {(form.role === "Creator" || form.role === "Business") && <Field label="Public identifier"><input required value={form.publicId} onChange={event => setForm({ ...form, publicId: event.target.value })} /></Field>}
        {(form.role === "Creator" || form.role === "Business") && <Field label="Region"><input value={form.region} onChange={event => setForm({ ...form, region: event.target.value })} /></Field>}
        {(form.role === "Creator" || form.role === "Business") && <Field label="Category"><input value={form.category} onChange={event => setForm({ ...form, category: event.target.value })} /></Field>}
        {form.role === "PlatformAdmin" && <Field label="Reason"><textarea required maxLength={500} value={form.reason} onChange={event => setForm({ ...form, reason: event.target.value })} /></Field>}
        {action.error && <Notice error>{action.error}</Notice>}
        <div className="actions"><Button type="submit" disabled={action.busy}>{action.busy ? "Creating…" : "Create invitation"}</Button><Link className="button secondary" to={areaPath[area]}>Cancel</Link></div>
      </form>
    </Section>
  </div>;
}

export function AdminAccountDetail() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const requestedRole = searchParams.get("role");
  const resource = useResource<AccountDetail>(`/admin/accounts/${id}${requestedRole ? `?role=${encodeURIComponent(requestedRole)}` : ""}`);
  const action = useAction();
  const [reason, setReason] = useState("");
  const [confirmClose, setConfirmClose] = useState(false);
  const manage = true;
  return <>
    <Link className="back-link" to={areaPath[resource.data?.account.role === "Customer" ? "Customer" : resource.data?.account.role === "Creator" ? "Creator" : resource.data?.account.role === "Business" ? "Business" : "Admin"]}>← Back to list</Link>
    <Resource resource={resource}>{(detail) => <AccountDetailContent detail={detail} manage={manage} reason={reason} setReason={setReason} confirmClose={confirmClose} setConfirmClose={setConfirmClose} action={action} reload={resource.reload} />}</Resource>
  </>;
}

function AccountDetailContent({ detail, manage, reason, setReason, confirmClose, setConfirmClose, action, reload }: { detail: AccountDetail; manage: boolean; reason: string; setReason: (value: string) => void; confirmClose: boolean; setConfirmClose: (value: boolean) => void; action: ReturnType<typeof useAction>; reload: () => void }) {
  const account = detail.account;
  const roleData = detail.roleData as Record<string, unknown> | null;
  const targetId = account.userId ?? account.id;
  const businessId = detail.profiles.find(x => x.role === "Business")?.subjectId
    ?? (account.role === "Business" ? account.association : null);
  const lifecycle = (next: string) => { if (!reason.trim() || (next === "close" && !confirmClose)) return; void action.run(async key => { if (account.status === "Pending") await post(`/admin/accounts/preauthorizations/${account.id}/cancel`, { action: "cancel", reason: reason.trim() }, key); else await post(`/admin/accounts/${targetId}/lifecycle`, { action: next, reason: reason.trim(), confirmClose: next === "close" }, key); setReason(""); setConfirmClose(false); reload(); }); };
  return <>
    <PageHeader eyebrow={`${account.role} · ${account.safeIdentifier}`} title={account.name} description={`${displayStatus(account.status)}${account.approvalState ? ` · ${account.approvalState}` : ""}`} action={manage && account.canManage ? <Badge status={displayStatus(account.status)} /> : account.canManage ? <Link className="button secondary" to={`/admin/accounts/${account.id}?role=${account.role}&mode=manage`}>Manage</Link> : undefined} />
    <div className="two-column">
      <Section title="Overview"><div className="stack-list">{detail.roles.map((role) => <div className="amount-row" key={role}><strong>{role}</strong><Badge status={displayStatus(account.status)} /></div>)}{detail.profiles.map((profile) => <div className="amount-row" key={profile.subjectId}><div><strong>{profile.displayName}</strong><small>{profile.role} · {profile.publicId}{profile.region ? ` · ${profile.region}` : ""}</small></div><Badge status={profile.active ? "Active" : "Inactive"} /></div>)}</div>{manage && account.canManage && <><Field label="Reason for lifecycle action"><textarea value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} placeholder="Required for this change" /></Field>{account.status !== "Pending" && <Field label="Confirm terminal closure" help="Closing denies future access and keeps financial and security history."><input type="checkbox" checked={confirmClose} onChange={(event) => setConfirmClose(event.target.checked)} /></Field>}<div className="actions">{account.status === "Pending" ? <Button disabled={action.busy || !reason.trim()} onClick={() => lifecycle("cancel")}>Cancel preauthorization</Button> : <>{account.status === "Active" && <Button disabled={action.busy || !reason.trim()} onClick={() => lifecycle("lock")}>Lock</Button>}{account.status === "Suspended" && <Button disabled={action.busy || !reason.trim()} onClick={() => lifecycle("unlock")}>Unlock</Button>}{(account.status === "Active" || account.status === "Suspended") && <Button variant="secondary" disabled={action.busy || !reason.trim()} onClick={() => lifecycle("deactivate")}>Deactivate</Button>}{account.status === "Disabled" && <Button variant="secondary" disabled={action.busy || !reason.trim()} onClick={() => lifecycle("reactivate")}>Reactivate</Button>}{["Active", "Suspended", "Disabled"].includes(account.status) && <Button variant="secondary" disabled={action.busy || !reason.trim() || !confirmClose} onClick={() => lifecycle("close")}>Close Account</Button>}</>}</div>{action.error && <Notice error>{action.error}</Notice>}</>}</Section>
      <Section title="Role-specific information">{roleData ? <div className="stack-list">{Object.entries(roleData).map(([key, value]) => <div className="amount-row" key={key}><span>{label(key)}</span><strong>{displayValue(key, value)}</strong></div>)}</div> : <Empty title="No role projection" message="This account has no role-specific projection available." />}</Section>
    </div>
    {detail.transactions.length > 0 && <Section title="Commerce activity"><div className="stack-list">{detail.transactions.map((transaction) => <div className="amount-row" key={transaction.id}><div><strong>{transaction.purchaseAmount.toLocaleString()} {transaction.currency}</strong><small>{dateTime(transaction.occurredAtUtc)} · {transaction.status}</small></div><span>{transaction.businessId}</span></div>)}</div></Section>}
    {businessId && <BusinessCashiers businessId={businessId} />}
  </>;
}

function BusinessCashiers({ businessId }: { businessId: string }) {
  const resource = useResource<AdminAccountSummary[]>(`/admin/accounts/businesses/${businessId}/cashiers`);
  return <Section title="Cashier Management" description="Cashiers are invited and managed by this Business.">
    <Resource resource={resource}>{rows => rows.length ? <div className="stack-list">{rows.map(row => <div className="amount-row" key={row.id}><strong>{row.name}</strong><Badge status={displayStatus(row.status)} /></div>)}</div> : <Empty title="No Cashiers" message="This Business has not added Cashiers." />}</Resource>
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
