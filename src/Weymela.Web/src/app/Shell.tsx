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
    ["/business/requests", "Requests", "people"],
    ["/business/ugc", "UGC", "sparkle"],
    ["/business/wallet", "Wallet", "wallet"],
    ["/checkout", "Checkout", "qr"],
    ["/profile", "Profile", "people"],
  ],
  Creator: [
    ["/creator", "Home", "home"],
    ["/creator/discover", "Discover", "search"],
    ["/creator/promotions", "Promotions", "campaign"],
    ["/creator/earnings", "Earnings", "wallet"],
    ["/profile", "Profile", "people"],
  ],
  PlatformAdmin: [
    ["/admin", "Dashboard", "home"],
    ["/admin/customers", "Customers", "people"],
    ["/admin/creators", "Creators", "people"],
    ["/admin/businesses", "Businesses", "wallet"],
    ["/admin/admins", "Admins", "people"],
    ["/admin/campaigns", "Campaigns", "campaign"],
    ["/admin/wallets", "Wallets", "wallet"],
    ["/admin/ugc", "UGC", "sparkle"],
    ["/admin/payouts", "Payouts", "money"],
    ["/admin/reports", "Reports", "chart"],
    ["/admin/settings", "Financial Settings", "settings"],
    ["/notifications", "Notifications", "bell"],
  ],
  OperationsAdmin: [
    ["/admin/operations", "Home", "home"],
    ["/admin/role-enrollments", "Profile Requests", "people"],
    ["/admin/operations/businesses", "Businesses", "business"],
    ["/admin/operations/creators", "Creators", "people"],
    ["/admin/operations/customers", "Customers", "people"],
    ["/admin/campaigns", "Campaigns", "campaign"],
    ["/admin/ugc", "UGC", "sparkle"],
    ["/admin/payouts", "Payouts", "money"],
    ["/admin/notifications", "Notifications", "bell"],
  ],
  Customer: [
    ["/customer/offers", "Home", "home"],
    ["/customer/discover", "Discover", "search"],
    ["/customer/cashback", "Cashback", "wallet"],
    ["/customer/transactions", "Transactions", "document"],
    ["/profile", "Profile", "people"],
  ],
  Cashier: [["/checkout", "Purchase", "qr"], ["/checkout/transactions", "Transactions", "document"], ["/profile", "Profile", "people"]],
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
const exactNavigationRoots = new Set(["/admin", "/admin/operations", "/business", "/creator", "/checkout"]);
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
  const items = baseItems;
  const isPublicProfile =
    user.role === "Customer" ||
    user.role === "Creator" ||
    user.role === "Business";
  const hasAccountMenuProfile = isPublicProfile || user.role === "Cashier";
  const isProductRole = user.role === "Customer" || user.role === "Creator" || user.role === "Business";
  const mobileItems = user.role === "PlatformAdmin"
    ? items.filter(([to]) => ["/admin", "/admin/wallets", "/admin/payouts"].includes(to))
    : user.role === "Business" ? items.filter(([to]) => ["/business", "/business/campaigns", "/business/ugc", "/business/wallet", "/profile"].includes(to))
    : items.slice(0, user.role === "OperationsAdmin" ? 3 : 5);
  const overflowItems = user.role === "PlatformAdmin"
    ? items.filter(([to]) => !mobileItems.some(([mobileTo]) => mobileTo === to))
    : user.role === "OperationsAdmin" ? items.slice(3) : [];
  const itemIsActive = (to: string, isActive: boolean) =>
    isActive ||
    (!exactNavigationRoots.has(to)
      && location.pathname.startsWith(`${to}/`));
  const overflowIsActive = overflowItems.some(([to]) => itemIsActive(to, location.pathname === to));
  const closeAccountMenu = () => accountMenu.current?.close();
  const openAccountMenu = () => {
    moreMenu.current?.close();
    accountMenu.current?.showModal();
  };
  const renderNavigation = (
    entries: [string, string, string][],
    ariaLabel: string,
    onNavigate?: () => void,
  ) => (
    <nav aria-label={ariaLabel}>
      {entries.map(([to, label, icon]) => (
        label === "Profile" && hasAccountMenuProfile ? (
          <button key={to} type="button" className="nav-link" onClick={openAccountMenu}>
            <Icon name={icon} />
            {label}
          </button>
        ) : (
          <NavLink
            to={to}
            end={exactNavigationRoots.has(to)}
            key={to}
            className={({ isActive }) =>
              `nav-link ${itemIsActive(to, isActive) ? "active" : ""}`
            }
            onClick={onNavigate}
          >
            <Icon name={icon} />
            {label}
          </NavLink>
        )
      ))}
    </nav>
  );
  const openMoreMenu = () => {
    closeAccountMenu();
    moreMenu.current?.showModal();
  };
  const signOutAndClose = () => {
    closeAccountMenu();
    void signOut().then(() => navigate("/sign-in"));
  };
  return (
    <div className={`app-shell role-${user.role.toLowerCase()}${isProductRole || user.role === "Cashier" ? " product-shell" : ""}`}>
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">
        <Link className="brand-link" to={roleHome[user.role]}>
          <Brand />
        </Link>
        <p className="nav-eyebrow">{roles[user.role]} workspace</p>
        {user.profiles && user.profiles.length > 0 && (user.role !== "PlatformAdmin" || user.profiles.length > 1) && (
          <ProfileSwitcher
            profiles={user.profiles}
            activeKey={user.activeProfileKey}
            onSwitch={switchProfile}
          />
        )}
        {renderNavigation(items, "Main navigation")}
        {isPublicProfile && user.role !== "Business" && user.role !== "Creator" && (
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
        {user.profiles && user.profiles.length > 0 && (user.role !== "PlatformAdmin" || user.profiles.length > 1) && (
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
        <Link className="account-menu-link" to="/notifications" onClick={closeAccountMenu}>
          <Icon name="bell" />
          Notifications
        </Link>
        {user.role === "Business" && <div className="account-menu-secondary">
          <Link className="account-menu-link" to="/business/requests" onClick={closeAccountMenu}><Icon name="people" />Creator Requests</Link>
          <Link className="account-menu-link" to="/checkout" onClick={closeAccountMenu}><Icon name="qr" />Checkout</Link>
          <Link className="account-menu-link" to="/business/cashiers" onClick={closeAccountMenu}><Icon name="people" />Cashier Management</Link>
          <Link className="account-menu-link" to="/business/pricing" onClick={closeAccountMenu}><Icon name="settings" />Pricing</Link>
        </div>}
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
          {(user.role === "Customer" || user.role === "Creator" || user.role === "Business") && (
            <Link className="topbar-brand" to={roleHome[user.role]} aria-label={`Weymela ${roles[user.role]} home`}>
              <Brand />
            </Link>
          )}
          <span className="workspace-label">
            {roles[user.role]} <span className="muted">/ Weymela</span>
          </span>
          {user.role === "PlatformAdmin" && <strong className="topbar-identity">{user.displayName}</strong>}
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
              onClick={openAccountMenu}
            >
              {user.role === "PlatformAdmin" && <Icon name="settings" />}
              <span className="small-avatar" aria-hidden="true">
                {user.displayName.slice(0, 1)}
              </span>
            </Button>
          </div>
        </header>
        {isProductRole && <div className="product-desktop-nav">{renderNavigation(items, `${roles[user.role]} navigation`)}</div>}
        <main id="main-content" className="main-content" tabIndex={-1}>
          <ConnectionStatus />
          {children ?? <Outlet />}
        </main>
        <nav className="mobile-role-nav" aria-label="Mobile navigation">
          {mobileItems.map(([to, label, icon]) =>
            hasAccountMenuProfile && label === "Profile" ? (
              <button
                key={to}
                type="button"
                className="mobile-role-link"
                aria-label="Profile"
                aria-haspopup="dialog"
                aria-controls="account-menu"
                onClick={openAccountMenu}
              >
                <Icon name={icon} />
                <span>{label}</span>
              </button>
            ) : (
              <NavLink
                key={to}
                to={to}
                end={exactNavigationRoots.has(to)}
                className={({ isActive }) =>
                  `mobile-role-link ${itemIsActive(to, isActive) ? "active" : ""}`
                }
              >
                <Icon name={icon} />
                <span>{label}</span>
              </NavLink>
            ),
          )}
          {overflowItems.length > 0 && (
            <button
              type="button"
              className={`mobile-role-link ${overflowIsActive ? "active" : ""}`}
              aria-label="More navigation"
              aria-pressed={overflowIsActive}
              aria-haspopup="dialog"
              aria-controls="more-navigation"
              onClick={openMoreMenu}
            >
              <Icon name="menu" />
              <span>More</span>
            </button>
          )}
        </nav>
        <footer className="workspace-footer"><span>Weymela</span></footer>
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
