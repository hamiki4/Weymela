import { type ReactNode, useEffect, useId, useRef, useState } from "react";
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
import { notificationsChangedEvent, primeResources, useResource } from "../api/client";
import { roleHome, useSession } from "./Session";
import { ConnectionStatus } from "./ConnectionStatus";
import { routeResources } from "./routeResources";

const navigation: Record<Role, [string, string, string][]> = {
  Business: [
    ["/business", "Home", "home"],
    ["/business/campaigns", "Promotions", "campaign"],
    ["/business/requests", "Requests", "people"],
    ["/business/wallet", "Wallet", "wallet"],
    ["/business/transactions", "Transactions", "document"],
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
    ["/admin/social-profiles", "Social Review", "people"],
    ["/notifications", "Notifications", "bell"],
  ],
  OperationsAdmin: [
    ["/admin/operations", "Home", "home"],
    ["/admin/wallets", "Wallets", "wallet"],
    ["/admin/role-enrollments", "Profile Requests", "people"],
    ["/admin/operations/businesses", "Businesses", "business"],
    ["/admin/operations/creators", "Creators", "people"],
    ["/admin/operations/customers", "Customers", "people"],
    ["/admin/campaigns", "Campaigns", "campaign"],
    ["/admin/ugc", "UGC", "sparkle"],
    ["/admin/payouts", "Payouts", "money"],
    ["/admin/notifications", "Notifications", "bell"],
    ["/admin/social-profiles", "Social Review", "people"],
  ],
  Customer: [
    ["/customer/offers", "Home", "home"],
    ["/customer/discover", "Discover", "search"],
    ["/customer/cashback", "Cashback", "wallet"],
    ["/customer/transactions", "Transactions", "document"],
    ["/profile", "Profile", "people"],
  ],
  Cashier: [["/checkout", "Purchase", "qr"], ["/checkout/transactions", "Transactions", "document"], ["/settings", "Settings", "settings"]],
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
  const notifications = useResource<{ unreadCount: number }>("/notifications");
  useEffect(() => {
    const refresh = () => notifications.reload();
    window.addEventListener(notificationsChangedEvent, refresh);
    window.addEventListener("focus", refresh);
    return () => {
      window.removeEventListener(notificationsChangedEvent, refresh);
      window.removeEventListener("focus", refresh);
    };
  }, [notifications.reload]);
  const navigationSequence = useRef(0);
  const navigate = useNavigate();
  const location = useLocation();
  if (!user) return null;
  const baseItems = navigation[user.role];
  const items = baseItems;
  const isProductRole = user.role === "Customer" || user.role === "Creator" || user.role === "Business";
  const mobileItems = user.role === "PlatformAdmin"
    ? items.filter(([to]) => ["/admin", "/admin/wallets", "/admin/payouts"].includes(to))
    : user.role === "Business" ? items.filter(([to]) => ["/business", "/business/campaigns", "/business/wallet", "/profile"].includes(to))
    : items.slice(0, user.role === "OperationsAdmin" ? 3 : 5);
  const overflowItems = user.role === "PlatformAdmin"
    ? items.filter(([to]) => !mobileItems.some(([mobileTo]) => mobileTo === to))
    : user.role === "OperationsAdmin" ? items.slice(3) : [];
  const itemIsActive = (to: string, isActive: boolean) =>
    isActive ||
    (!exactNavigationRoots.has(to)
      && location.pathname.startsWith(`${to}/`));
  const overflowIsActive = location.pathname === "/admin/more" || overflowItems.some(([to]) => itemIsActive(to, location.pathname === to));
  const renderNavigation = (
    entries: [string, string, string][],
    ariaLabel: string,
    onNavigate?: () => void,
  ) => (
    <nav aria-label={ariaLabel}>
      {entries.map(([to, label, icon]) => (
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
      ))}
    </nav>
  );
  const signOutAndClose = () => {
    void signOut().then(() => navigate("/sign-in"));
  };
  const prepareNavigation = (event: React.MouseEvent<HTMLDivElement>) => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    const target = event.target;
    const anchor = target instanceof Element ? target.closest<HTMLAnchorElement>("a[href]") : null;
    if (!anchor || anchor.target || anchor.hasAttribute("download")) return;
    const destination = new URL(anchor.href, window.location.href);
    if (destination.origin !== window.location.origin || destination.pathname === location.pathname) return;
    const resources = routeResources(destination.pathname);
    if (!resources.length) return;
    event.preventDefault();
    const sequence = ++navigationSequence.current;
    const profileKey = user.activeProfileKey ?? null;
    void primeResources(resources).catch(() => {
      // The destination owns its ordinary error presentation if a request fails.
    }).then(() => {
      if (sequence === navigationSequence.current
          && (window.sessionStorage.getItem("weymela.profile-key") ?? null) === profileKey)
        navigate(`${destination.pathname}${destination.search}${destination.hash}`);
    });
  };
  return (
    <div onClickCapture={prepareNavigation} className={`app-shell role-${user.role.toLowerCase()}${isProductRole || user.role === "Cashier" ? " product-shell" : ""}`}>
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
        {user.role === "Customer" && (
          <Link className="nav-link add-profile-link" to="/onboarding">
            <Icon name="people" />
            Add a profile
          </Link>
        )}
        <div className="sidebar-bottom">
          <div className="person">
            <span className="avatar">{user.displayName.slice(0, 1)}</span>
            <div>
              <strong data-no-translate>{user.displayName}</strong>
              <small>{roles[user.role]}</small>
            </div>
          </div>
          <Link className="nav-link settings-nav-link" to="/settings">
            <Icon name="settings" />
            Settings
          </Link>
          <Button variant="quiet" icon="logout" onClick={signOutAndClose}>
            Sign out
          </Button>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          {(user.role === "Customer" || user.role === "Creator" || user.role === "Business") && (
            <Link className="topbar-brand" to={roleHome[user.role]} aria-label={`Weymela ${roles[user.role]} home`}>
              <Brand />
            </Link>
          )}
          <span className="workspace-label">
            {roles[user.role]} {!isProductRole && <span className="muted">/ Weymela</span>}
          </span>
          {user.role === "PlatformAdmin" && <strong className="topbar-identity" data-no-translate>{user.displayName}</strong>}
          <div className="topbar-right">
            <Link
              className="button button-quiet"
              to="/notifications"
              aria-label="Your notifications"
            >
              <Icon name="bell" />
              {notifications.data && notifications.data.unreadCount > 0 && <span className="notification-count" aria-label={`${notifications.data.unreadCount} unread notifications`}>{notifications.data.unreadCount > 99 ? "99+" : notifications.data.unreadCount}</span>}
            </Link>
            {user.developmentMode && (
              <span className="dev-badge">Local development</span>
            )}
            <Link className="button button-quiet settings-trigger" to="/settings" aria-label="Open Settings">
              <Icon name="settings" />
            </Link>
          </div>
        </header>
        {isProductRole && <div className="product-desktop-nav">{renderNavigation(items, `${roles[user.role]} navigation`)}</div>}
        <main id="main-content" className="main-content" tabIndex={-1}>
          <ConnectionStatus />
          {children ?? <Outlet />}
        </main>
        <nav className="mobile-role-nav" aria-label="Mobile navigation">
          {mobileItems.map(([to, label, icon]) => (
              <NavLink
                key={to}
                to={to}
                end={exactNavigationRoots.has(to)}
                className={({ isActive }) =>
                  `mobile-role-link ${label.length >= 9 ? "mobile-role-link-wide" : ""} ${itemIsActive(to, isActive) ? "active" : ""}`
                }
              >
                <Icon name={icon} />
                <span>{label}</span>
              </NavLink>
          ))}
          {overflowItems.length > 0 && (
            <NavLink
              to="/admin/more"
              state={{ from: location.pathname }}
              className={`mobile-role-link ${overflowIsActive ? "active" : ""}`}
              aria-label="More navigation"
            >
              <Icon name="menu" />
              <span>More</span>
            </NavLink>
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
            <option key={key} value={index} data-no-translate>
              {profile.displayName} — {roles[profile.role]}
            </option>
          );
        })}
      </select>
      {error && <small role="alert">{error}</small>}
    </div>
  );
}
