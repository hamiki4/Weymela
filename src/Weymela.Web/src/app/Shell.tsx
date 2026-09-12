import { useRef } from "react";
import {
  Link,
  NavLink,
  Outlet,
  useLocation,
  useNavigate,
} from "react-router-dom";
import type { Role } from "../api/types";
import { Button } from "../ui/components";
import { Icon } from "../ui/Icon";
import { roleHome, useSession } from "./Session";
import { ConnectionStatus } from "./ConnectionStatus";

const navigation: Record<Role, [string, string, string][]> = {
  Business: [
    ["/business", "Overview", "home"],
    ["/business/wallet", "Wallet", "wallet"],
    ["/business/campaigns", "Campaigns", "campaign"],
    ["/business/requests", "Creator Requests", "people"],
    ["/business/pricing", "Campaign Pricing", "settings"],
  ],
  Creator: [
    ["/creator", "Overview", "home"],
    ["/creator/discover", "Discover Campaigns", "search"],
    ["/creator/campaigns", "Active Campaigns", "campaign"],
    ["/creator/requests", "Campaign Requests", "people"],
    ["/creator/earnings", "Earnings", "wallet"],
    ["/creator/payouts", "Payouts", "money"],
    ["/creator/pricing", "How You Earn", "settings"],
  ],
  PlatformAdmin: [
    ["/admin", "Dashboard", "home"],
    ["/admin/businesses", "Businesses", "wallet"],
    ["/admin/creators", "Creators", "people"],
    ["/admin/campaigns", "Campaigns", "campaign"],
    ["/admin/settings", "Financial Settings", "settings"],
    ["/admin/payouts", "Payouts", "money"],
    ["/admin/platform", "Platform Revenue", "chart"],
    ["/admin/notifications", "Notifications", "bell"],
    ["/admin/audit", "Audit", "document"],
  ],
  Customer: [
    ["/customer/offers", "Offers for you", "sparkle"],
    ["/customer/history", "Your Cashback", "wallet"],
  ],
  Cashier: [["/checkout", "Checkout", "qr"]],
};
const roles: Record<Role, string> = {
  Business: "Business",
  Creator: "Creator",
  PlatformAdmin: "Platform Admin",
  Customer: "Customer",
  Cashier: "Cashier",
};
export function Brand() {
  return (
    <span className="brand">
      <img src="/icon.svg" width="34" height="34" alt="" />
      <span>
        weymela<span className="brand-dot">.</span>
      </span>
    </span>
  );
}
export function Shell() {
  const { user, signOut } = useSession();
  const menu = useRef<HTMLDialogElement>(null);
  const navigate = useNavigate();
  const location = useLocation();
  if (!user) return null;
  const items = [
    ...navigation[user.role],
    ...(user.role === "Business" && user.canCheckout
      ? [["/checkout", "Checkout", "qr"] as [string, string, string]]
      : []),
  ];
  const nav = (
    <>
      <Link
        className="brand-link"
        to={roleHome[user.role]}
        onClick={() => menu.current?.close()}
      >
        <Brand />
      </Link>
      <p className="nav-eyebrow">{roles[user.role]} workspace</p>
      <nav aria-label="Main navigation">
        {items.map(([to, label, icon]) => (
          <NavLink
            to={to}
            end
            key={to}
            className={({ isActive }) =>
              `nav-link ${isActive || (to.endsWith("/campaigns") && location.pathname.startsWith(`${to}/`)) ? "active" : ""}`
            }
            onClick={() => menu.current?.close()}
          >
            <Icon name={icon} />
            {label}
          </NavLink>
        ))}
      </nav>
      <div className="sidebar-bottom">
        <div className="person">
          <span className="avatar">{user.displayName.slice(0, 1)}</span>
          <div>
            <strong>{user.displayName}</strong>
            <small>{user.publicId}</small>
          </div>
        </div>
        <Button
          variant="quiet"
          icon="logout"
          onClick={() => {
            void signOut().then(() => navigate("/sign-in"));
          }}
        >
          Sign out
        </Button>
      </div>
    </>
  );
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">{nav}</aside>
      <dialog ref={menu} className="nav-drawer" aria-label="Workspace menu">
        <Button
          variant="quiet"
          className="close-menu"
          aria-label="Close menu"
          onClick={() => menu.current?.close()}
        >
          <Icon name="close" />
        </Button>
        {nav}
      </dialog>
      <div className="workspace">
        <header className="topbar">
          <Button
            variant="quiet"
            className="menu-toggle"
            aria-label="Open menu"
            onClick={() => menu.current?.showModal()}
          >
            <Icon name="menu" />
          </Button>
          <span className="workspace-label">
            {roles[user.role]} <span className="muted">/ Weymela</span>
          </span>
          <div className="topbar-right">
            <Link className="button button-quiet" to="/notifications" aria-label="Your notifications"><Icon name="bell" /></Link>
            {user.developmentMode && (
              <span className="dev-badge">Local development</span>
            )}
            <span className="small-avatar" aria-label={user.displayName}>
              {user.displayName.slice(0, 1)}
            </span>
          </div>
        </header>
        <main id="main-content" className="main-content" tabIndex={-1}>
          <ConnectionStatus />
          <Outlet />
        </main>
        <footer className="workspace-footer">
          <span>Grow together, with Weymela.</span>
          <span>Secure, role-specific workspace</span>
        </footer>
      </div>
    </div>
  );
}
