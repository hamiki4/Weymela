import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { post, useResource } from "../api/client";
import type { AccountClosureOption, AccountClosureOverview, AccountClosureResult, Role } from "../api/types";
import { LanguageChoice } from "../localization/Language";
import { Badge, Button, Dialog, Notice, PageHeader, Resource } from "../ui/components";
import { Icon } from "../ui/Icon";
import { roleHome, useSession } from "./Session";
import { ProfileSwitcher } from "./Shell";

const roleNames: Record<Role, string> = {
  PlatformAdmin: "Platform Admin",
  OperationsAdmin: "Operations Admin",
  Business: "Business",
  Creator: "Creator",
  Customer: "Customer",
  Cashier: "Cashier",
  Onboarding: "Account setup",
};

function SettingsLink({ to, icon, title, note }: { to: string; icon: string; title: string; note?: string }) {
  return <Link className="settings-page-row" to={to}>
    <span className="settings-page-icon"><Icon name={icon} /></span>
    <span><strong>{title}</strong>{note && <small>{note}</small>}</span>
    <Icon name="arrow" size={18} />
  </Link>;
}

export function SettingsPage() {
  const { user, signOut, switchProfile } = useSession();
  const navigate = useNavigate();
  if (!user) return null;
  return <div className="settings-page">
    <PageHeader title="Settings" compact />
    <section className="settings-card" aria-labelledby="language-settings-title">
      <h2 id="language-settings-title">Language</h2>
      <LanguageChoice />
    </section>
    {user.profiles && user.profiles.length > 0 && <section className="settings-card settings-profile-card" aria-label="Account profiles">
      <ProfileSwitcher profiles={user.profiles} activeKey={user.activeProfileKey} onSwitch={switchProfile} />
    </section>}
    <section className="settings-card settings-page-list" aria-label="Settings options">
      {(user.role === "Customer" || user.role === "Creator" || user.role === "Business") &&
        <SettingsLink to="/profile" icon="people" title="Profile" />}
      {user.role === "Business" && <SettingsLink to="/profile#business-location" icon="location" title="Business Location" note="Add directions for Customers" />}
      {user.role === "Creator" && <SettingsLink to="/profile#social-profiles" icon="globe" title="Social Profiles" />}
      {user.role === "Business" && <SettingsLink to="/business/cashiers" icon="people" title="Cashier Management" />}
      {(user.role === "Customer" || user.role === "Creator" || user.role === "Business") &&
        <SettingsLink to="/onboarding" icon="plus" title="Add Profile" />}
      <SettingsLink to="/settings/help" icon="info" title="Help" />
      <SettingsLink to="/settings/contact" icon="document" title="Contact Us" />
      <SettingsLink to="/settings/delete-account" icon="lock" title="Delete Account" note="Close one account role safely" />
    </section>
    <Button className="settings-signout-button" variant="quiet" icon="logout" onClick={() => void signOut().then(() => navigate("/sign-in"))}>
      Sign Out
    </Button>
  </div>;
}

type HelpItem = [string, string];
const helpByRole: Partial<Record<Role, HelpItem[]>> = {
  Customer: [
    ["Discover promotions", "Open Discover to find active offers from real Businesses and Creators."],
    ["Watch promotions", "Open a Promotion card and choose Watch Promotion."],
    ["Get an offer QR", "Choose Get Offer QR on an eligible sale offer and show it at checkout."],
    ["How cashback works", "You pay the full purchase amount. Eligible cashback is added separately to your Cashback wallet."],
    ["Set a payout destination", "Open Cashback and save your verified Telebirr phone or bank account."],
    ["View transactions", "Transactions shows the Business, full purchase amount, cashback, and date."],
    ["Delete an account", "Open Settings, choose Delete Account, then select only the role you want to close."],
  ],
  Creator: [
    ["Find opportunities", "Open Discover and choose an eligible Promotion."],
    ["Apply", "Apply before the application deadline and wait for the Business decision."],
    ["Business approval", "An approval lets you continue to the requested video or publication step."],
    ["Submit a TikTok link", "Open the approved Promotion and paste a public TikTok video link that the Business can open before the deadline."],
    ["Respond to changes", "Read the Business comment, update the video, and submit a new revision."],
    ["Go Live", "After approval and any required publication check, open the Promotion and choose Go Live."],
    ["Earnings and payouts", "Verified earnings appear in Earnings. Eligible balances enter the Admin payout queue automatically."],
    ["Delete an account", "Open Settings, choose Delete Account, then select only the role you want to close."],
  ],
  Business: [
    ["Add your Business location", "Open Settings, choose Business Location, and save an address, Google Maps link, or coordinates."],
    ["Add funds", "Open Wallet, choose a receiving bank, enter the amount, and upload the receipt."],
    ["Deposit receipt", "The deposit remains Pending until an authorized Admin approves the receipt."],
    ["Create and publish", "Create a Promotion, save changes, then Publish after funding and deadlines are valid."],
    ["Approve Creators", "Open Requests to approve or reject Creator applications."],
    ["Review TikTok links", "Open each submitted TikTok link, then Approve, Request Changes, or Reject."],
    ["Manage checkout", "Use Checkout for eligible customer purchases and keep Cashier access limited to this Business."],
    ["View transactions", "Transactions shows Promotion, total purchase, customer cashback, and date."],
    ["Delete an account", "Open Settings, choose Delete Account, then select only the role you want to close."],
  ],
  PlatformAdmin: [
    ["Review deposits", "Open Wallets and approve only receipts verified against the receiving account."],
    ["Manage payouts", "Open Payouts, choose an eligible account, and enter an amount within the allowed balance."],
    ["Review Promotions and UGC", "Use Campaigns and UGC to review milestones, exceptions, and approved deliverables."],
    ["Manage account roles", "Use Accounts to create or close roles while preserving succession and ownership rules."],
    ["Review financial activity", "Use Reports and audit views to confirm balanced journals and authorized activity."],
  ],
  OperationsAdmin: [
    ["Review deposits", "Open Wallets and review only the deposits authorized for Operations."],
    ["Manage payouts", "Open Payouts to review eligible accounts and authorized payment activity."],
    ["Review Promotions and UGC", "Use Campaigns and UGC to review assigned milestones and exceptions."],
    ["Manage account roles", "Review profile requests within Operations authority. Platform Admin controls privileged roles."],
    ["Review financial activity", "Use the authorized reports and audit details for operational checks."],
  ],
  Cashier: [
    ["Complete a purchase", "Open Purchase, scan the eligible offer QR, enter the full purchase amount, and confirm."],
    ["View transactions", "Transactions shows purchases completed for your Business."],
    ["Delete an account", "Open Settings and choose Delete Account. Business ownership and financial records remain protected."],
  ],
};

function SettingsSubpage({ title, children }: { title: string; children: ReactNode }) {
  return <div className="settings-page settings-subpage">
    <PageHeader title={title} compact action={<Link className="button secondary" to="/settings"><Icon name="back" />Back</Link>} />
    {children}
  </div>;
}

export function HelpPage() {
  const { user } = useSession();
  const items = user ? helpByRole[user.role] ?? [] : [];
  return <SettingsSubpage title="Help">
    <section className="settings-card help-list" aria-labelledby="role-help-title">
      <h2 id="role-help-title">{user ? `${roleNames[user.role]} Help` : "Help"}</h2>
      {items.map(([title, answer], index) => <details key={title} open={index === 0}>
        <summary>{title}</summary>
        <p>{answer}</p>
      </details>)}
    </section>
  </SettingsSubpage>;
}

export function ContactPage() {
  return <SettingsSubpage title="Contact Us">
    <section className="settings-card contact-card" aria-labelledby="contact-title">
      <h2 id="contact-title">Weymela Support</h2>
      <a href="mailto:support@weymela.com"><Icon name="document" /><span><strong>Email</strong><small data-no-translate>support@weymela.com</small></span></a>
      <a href="tel:+251911111111"><Icon name="people" /><span><strong>Phone</strong><small data-no-translate>+251911111111</small></span></a>
    </section>
  </SettingsSubpage>;
}

function statusLabel(status: AccountClosureOption["status"]) {
  if (status === "ActionRequired") return "Action Required";
  if (status === "PendingClosure") return "Pending Closure";
  return "Eligible";
}

export function DeleteAccountPage() {
  const { user, refresh, signOut } = useSession();
  const navigate = useNavigate();
  const resource = useResource<AccountClosureOverview>("/account/closure");
  const [selectedKey, setSelectedKey] = useState("");
  const [confirming, setConfirming] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const key = useRef(crypto.randomUUID());
  const groupId = useId();
  const options = resource.data?.roles ?? [];
  useEffect(() => {
    if (!options.length) return;
    if (!options.some(option => `${option.role}:${option.subjectId}` === selectedKey)) {
      const current = options.find(option => option.role === user?.role) ?? options[0];
      setSelectedKey(`${current.role}:${current.subjectId}`);
    }
  }, [options, selectedKey, user?.role]);
  const selected = options.find(option => `${option.role}:${option.subjectId}` === selectedKey) ?? null;

  const closeRole = async () => {
    if (!selected || !confirmed || busy) return;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      const result = await post<AccountClosureResult>("/account/closure", {
        role: selected.role,
        subjectId: selected.subjectId,
        confirmation: "DELETE",
      }, key.current);
      key.current = crypto.randomUUID();
      setConfirming(false);
      setConfirmed(false);
      if (result.status === "PendingIdentityDeletion") {
        await signOut();
        navigate("/sign-in", { replace: true });
        return;
      }
      if (result.status === "Closed") {
        setMessage(`${roleNames[result.role]} role closed. Your other account roles are unchanged.`);
        await refresh(true);
        navigate(result.nextRole ? roleHome[result.nextRole] : "/settings", { replace: true });
        return;
      }
      setMessage("Closure is pending. Resolve the items shown below; your financial and audit records remain protected.");
      resource.reload();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "We could not complete that request.");
    } finally {
      setBusy(false);
    }
  };

  return <SettingsSubpage title="Delete Account">
    <Resource resource={resource}>{overview => <>
      <section className="settings-card account-closure-card" aria-labelledby={`${groupId}-title`}>
        <h2 id={`${groupId}-title`}>Choose an account role</h2>
        <p>Only the selected role will close. Your other roles, money, and required records are not deleted.</p>
        <div className="account-role-options" role={overview.roles.length > 1 ? "radiogroup" : undefined} aria-label="Account roles">
          {overview.roles.map(option => <label key={`${option.role}:${option.subjectId}`} className={`account-role-option ${selectedKey === `${option.role}:${option.subjectId}` ? "selected" : ""}`}>
            {overview.roles.length > 1 && <input type="radio" name="closure-role" value={`${option.role}:${option.subjectId}`} checked={selectedKey === `${option.role}:${option.subjectId}`} onChange={() => setSelectedKey(`${option.role}:${option.subjectId}`)} />}
            <span className="settings-page-icon"><Icon name={option.role === "Business" ? "wallet" : option.role === "Creator" ? "sparkle" : "people"} /></span>
            <span className="account-role-copy">
              <strong>{roleNames[option.role]}</strong>
              <small data-no-translate>{option.displayName}</small>
            </span>
            <Badge status={option.status} label={statusLabel(option.status)} />
          </label>)}
        </div>
        {selected?.blockers.length ? <div className="closure-blockers" role="status">
          <strong>Before this role can close</strong>
          <ul>{selected.blockers.map(blocker => <li key={blocker}>{blocker}</li>)}</ul>
        </div> : <Notice>This role can close without changing your other account roles.</Notice>}
        {message && <Notice>{message}</Notice>}
        {error && <Notice error>{error}</Notice>}
        <Button className="danger-button" onClick={() => { setConfirmed(false); setConfirming(true); }} disabled={!selected || selected.status === "PendingClosure"}>
          {selected?.status === "PendingClosure" ? "Closure Pending" : selected?.status === "ActionRequired" ? "Request Closure" : "Delete Account"}
        </Button>
      </section>
      <Dialog title="Confirm account closure" open={confirming} onClose={() => { if (!busy) setConfirming(false); }} className="closure-dialog">
        <p>You are closing only the <strong>{selected ? roleNames[selected.role] : "selected"}</strong> role.</p>
        <p>Required financial, security, tax, and audit records will be preserved. If obligations remain, the role stays restricted or active until an authorized review completes.</p>
        <label className="explicit-confirmation">
          <input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
          I understand and want to close this role.
        </label>
        <div className="dialog-actions">
          <Button variant="quiet" onClick={() => setConfirming(false)} disabled={busy}>Cancel</Button>
          <Button className="danger-button" onClick={() => void closeRole()} disabled={!confirmed || busy}>{busy ? "Closing…" : "Confirm"}</Button>
        </div>
      </Dialog>
    </>}</Resource>
  </SettingsSubpage>;
}
