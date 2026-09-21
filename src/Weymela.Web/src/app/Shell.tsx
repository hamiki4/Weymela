import { type ReactNode, useId, useRef, useState } from "react";
import {
  Link,
  NavLink,
  Outlet,
  useLocation,
  useNavigate,
} from "react-router-dom";
import type { Role, SessionProfile } from "../api/types";
import { Button } from "../ui/components";
import { Icon } from "../ui/Icon";
import { roleHome, useSession } from "./Session";
import { ConnectionStatus } from "./ConnectionStatus";

const navigation: Record<Role, [string, string, string][]> = {
  Business: [
    ["/business", "Home", "home"],
    ["/business/campaigns", "Promotions", "campaign"],
    ["/business/ugc", "UGC", "sparkle"],
    ["/business/wallet", "Wallet", "wallet"],
    ["/onboarding", "Profile", "people"],
  ],
  Creator: [
    ["/creator", "Overview", "home"],
    ["/creator/discover", "Discover Campaigns", "search"],
    ["/creator/campaigns", "Active Campaigns", "campaign"],
    ["/creator/requests", "Campaign Requests", "people"],
    ["/creator/ugc", "UGC", "sparkle"],
    ["/creator/earnings", "Earnings", "wallet"],
    ["/creator/payouts", "Payouts", "money"],
    ["/creator/pricing", "How You Earn", "settings"],
  ],
  PlatformAdmin: [
    ["/admin", "Dashboard", "home"],
    ["/admin/businesses", "Businesses", "wallet"],
    ["/admin/creators", "Creators", "people"],
    ["/admin/role-enrollments", "Profile Requests", "people"],
    ["/admin/campaigns", "Campaigns", "campaign"],
    ["/admin/settings", "Financial Settings", "settings"],
    ["/admin/payouts", "Payouts", "money"],
    ["/admin/platform", "Platform Revenue", "chart"],
    ["/admin/notifications", "Notifications", "bell"],
    ["/admin/audit", "Audit", "document"],
  ],
  OperationsAdmin: [],
  Customer: [
    ["/customer/offers", "Offers for you", "sparkle"],
    ["/customer/history", "Your Cashback", "wallet"],
  ],
  Cashier: [["/checkout", "Checkout", "qr"]],
  Onboarding: [],
};
const roles: Record<Role, string> = {
  Business: "Business",
  Creator: "Creator",
  PlatformAdmin: "Platform Admin",
  OperationsAdmin: "Operations Admin",
  Customer: "Customer",
  Cashier: "Cashier",
  Onboarding: "Account setup",
};
export function Brand() {
  return (
    <span className="brand" role="img" aria-label="Weymela">
      <img
        className="brand-mark"
        src="/brand/weymela-mark.png"
        width="38"
        height="38"
        alt=""
      />
      <img
        className="brand-wordmark"
        src="/brand/weymela-wordmark.png"
        width="154"
        height="36"
        alt=""
      />
    </span>
  );
}
export function Shell({ children }: { children?: ReactNode }) {
  const { user, signOut, switchProfile } = useSession();
  const accountMenu = useRef<HTMLDialogElement>(null);
  const moreMenu = useRef<HTMLDialogElement>(null);
  const navigate = useNavigate();
  const location = useLocation();
  if (!user) return null;
  const baseItems = navigation[user.role];
  const items =
    user.role === "Business" && user.canCheckout
      ? [
          ...baseItems.slice(0, 4),
          ["/checkout", "Checkout", "qr"] as [string, string, string],
          ...baseItems.slice(4),
        ]
      : baseItems;
  const isPublicProfile =
    user.role === "Customer" ||
    user.role === "Creator" ||
    user.role === "Business";
  const mobileItems = items.slice(0, 3);
  const overflowItems = items.slice(3);
  const renderNavigation = (
    entries: [string, string, string][],
    ariaLabel: string,
    onNavigate?: () => void,
  ) => (
    <nav aria-label={ariaLabel}>
      {entries.map(([to, label, icon]) => (
        <NavLink
          to={to}
          end
          key={to}
          className={({ isActive }) =>
            `nav-link ${isActive || (to.endsWith("/campaigns") && location.pathname.startsWith(`${to}/`)) ? "active" : ""}`
          }
          onClick={onNavigate}
        >
          <Icon name={icon} />
          {label}
        </NavLink>
      ))}
    </nav>
  );
  const closeAccountMenu = () => accountMenu.current?.close();
  const openMoreMenu = () => {
    closeAccountMenu();
    moreMenu.current?.showModal();
  };
  const signOutAndClose = () => {
    closeAccountMenu();
    void signOut().then(() => navigate("/sign-in"));
  };
  return (
    <div className={`app-shell role-${user.role.toLowerCase()}`}>
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">
        <Link className="brand-link" to={roleHome[user.role]}>
          <Brand />
        </Link>
        <p className="nav-eyebrow">{roles[user.role]} workspace</p>
        {user.profiles && user.profiles.length > 0 && (
          <ProfileSwitcher
            profiles={user.profiles}
            activeKey={user.activeProfileKey}
            onSwitch={switchProfile}
          />
        )}
        {renderNavigation(items, "Main navigation")}
        {isPublicProfile && user.role !== "Business" && (
          <Link className="nav-link add-profile-link" to="/onboarding">
            <Icon name="people" />
            Add a profile
          </Link>
        )}
        <div className="sidebar-bottom">
          <div className="person">
            <span className="avatar">{user.displayName.slice(0, 1)}</span>
            <div>
              <strong>{user.displayName}</strong>
              <small>{roles[user.role]}</small>
            </div>
          </div>
          <Button variant="quiet" icon="logout" onClick={signOutAndClose}>
            Sign out
          </Button>
        </div>
      </aside>
      <dialog
        ref={accountMenu}
        id="account-menu"
        className="dialog account-sheet"
        aria-labelledby="account-menu-title"
      >
        <div className="dialog-header">
          <h2 id="account-menu-title">Account menu</h2>
          <Button
            variant="quiet"
            aria-label="Close account menu"
            onClick={closeAccountMenu}
          >
            <Icon name="close" />
          </Button>
        </div>
        <div className="account-summary">
          <span className="avatar" aria-hidden="true">
            {user.displayName.slice(0, 1)}
          </span>
          <div>
            <strong>{user.displayName}</strong>
            <small>{roles[user.role]}</small>
          </div>
        </div>
        {user.profiles && user.profiles.length > 0 && (
          <ProfileSwitcher
            profiles={user.profiles}
            activeKey={user.activeProfileKey}
            onSwitch={switchProfile}
          />
        )}
        {isPublicProfile && user.role !== "Business" && (
          <Link
            className="account-menu-link"
            to="/onboarding"
            onClick={closeAccountMenu}
          >
            <Icon name="people" />
            Add a profile
          </Link>
        )}
        <div className="account-menu-actions">
          <Button variant="quiet" icon="logout" onClick={signOutAndClose}>
            Sign out
          </Button>
        </div>
      </dialog>
      {overflowItems.length > 0 && (
        <dialog
          ref={moreMenu}
          id="more-navigation"
          className="dialog more-sheet"
          aria-labelledby="more-navigation-title"
        >
          <div className="dialog-header">
            <h2 id="more-navigation-title">More navigation</h2>
            <Button
              variant="quiet"
              aria-label="Close more navigation"
              onClick={() => moreMenu.current?.close()}
            >
              <Icon name="close" />
            </Button>
          </div>
          {renderNavigation(overflowItems, "More navigation", () =>
            moreMenu.current?.close(),
          )}
        </dialog>
      )}
      <div className="workspace">
        <header className="topbar">
          <span className="workspace-label">
            {roles[user.role]} <span className="muted">/ Weymela</span>
          </span>
          <div className="topbar-right">
            <Link
              className="button button-quiet"
              to="/notifications"
              aria-label="Your notifications"
            >
              <Icon name="bell" />
            </Link>
            {user.developmentMode && (
              <span className="dev-badge">Local development</span>
            )}
            <Button
              variant="quiet"
              className="account-trigger"
              aria-label="Open account menu"
              aria-haspopup="dialog"
              aria-controls="account-menu"
              onClick={() => {
                moreMenu.current?.close();
                accountMenu.current?.showModal();
              }}
            >
              <span className="small-avatar" aria-hidden="true">
                {user.displayName.slice(0, 1)}
              </span>
            </Button>
          </div>
        </header>
        <main id="main-content" className="main-content" tabIndex={-1}>
          <ConnectionStatus />
          {children ?? <Outlet />}
        </main>
        <nav className="mobile-role-nav" aria-label="Mobile navigation">
          {mobileItems.map(([to, label, icon]) => (
            <NavLink
              key={to}
              to={to}
              end
              className={({ isActive }) =>
                `mobile-role-link ${isActive || (to.endsWith("/campaigns") && location.pathname.startsWith(`${to}/`)) ? "active" : ""}`
              }
            >
              <Icon name={icon} />
              <span>{label}</span>
            </NavLink>
          ))}
          {overflowItems.length > 0 && (
            <button
              type="button"
              className="mobile-role-link"
              aria-label="More navigation"
              aria-haspopup="dialog"
              aria-controls="more-navigation"
              onClick={openMoreMenu}
            >
              <Icon name="menu" />
              <span>More</span>
            </button>
          )}
        </nav>
        <footer className="workspace-footer">
          <span>Grow together, with Weymela.</span>
        </footer>
      </div>
    </div>
  );
}

export function ProfileSwitcher({
  profiles,
  activeKey,
  onSwitch,
}: {
  profiles: SessionProfile[];
  activeKey?: string | null;
  onSwitch: (profile: SessionProfile) => Promise<unknown>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const id = useId();
  return (
    <div className="profile-switcher">
      <label htmlFor={id}>Switch profile</label>
      <select
        id={id}
        value={String(
          profiles.findIndex(
            (profile) =>
              `${profile.role}:${profile.subjectId}:${profile.businessId ?? "-"}` ===
              activeKey,
          ),
        )}
        disabled={busy || profiles.length < 2}
        onChange={async (event) => {
          const profile = profiles[Number(event.target.value)];
          if (!profile) return;
          setBusy(true);
          setError(null);
          try {
            await onSwitch(profile);
          } catch {
            setError(
              "That profile is no longer available. Refresh and try again.",
            );
          } finally {
            setBusy(false);
          }
        }}
      >
        {profiles.map((profile, index) => {
          const key = `${profile.role}:${profile.subjectId}:${profile.businessId ?? "-"}`;
          return (
            <option key={key} value={index}>
              {profile.displayName} — {roles[profile.role]}
            </option>
          );
        })}
      </select>
      {error && <small role="alert">{error}</small>}
    </div>
  );
}
