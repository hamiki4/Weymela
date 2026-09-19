import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { RoleGate, roleHome, useSession } from "./Session";
import { Shell } from "./Shell";
import { SignIn } from "./SignIn";
import { ActionLink, Empty, Section } from "../ui/components";
import {
  BusinessCampaigns,
  BusinessDashboard,
  BusinessPricingPage,
  BusinessRequests,
  BusinessWallet,
} from "../features/business/BusinessPages";
import { CreateCampaign } from "../features/business/CreateCampaign";
import { BusinessCampaignDetail } from "../features/business/CampaignDetail";
import { BusinessUgcPage, CreatorUgcPage } from "../features/business/UgcPages";
import {
  CreatorDashboard,
  CreatorDiscovery,
  CreatorEarnings,
  CreatorHowYouEarn,
  CreatorOpportunity,
  CreatorRequests,
} from "../features/creator/CreatorPages";
import {
  CreatorActiveCampaigns,
  CreatorActiveDetail,
} from "../features/creator/ActiveCampaigns";
import {
  AdminActivity,
  AdminBusinesses,
  AdminCampaignDetail,
  AdminCampaigns,
  AdminCreators,
  AdminDashboard,
  AdminRoleEnrollments,
} from "../features/admin/AdminPages";
import { AdminFinancialSettings } from "../features/admin/FinancialSettings";
import { AdminPayouts, AdminPlatformRevenue } from "../features/admin/Payouts";
import {
  CustomerHistory,
  CustomerOfferQr,
  CustomerOffers,
} from "../features/commerce/CustomerPages";
import { Checkout } from "../features/commerce/Checkout";
import { Inbox } from "../features/notifications/Inbox";
import { Onboarding } from "./Onboarding";
import { PinSetup } from "./PinSetup";
import { LockScreen } from "./LockScreen";
import { SecuritySetup } from "./SecuritySetup";
import { AccountRedirect, accountEntryPath } from "./AccountEntry";
import { LegalDocumentPage } from "./LegalDocumentPage";
import { ProductHandoffCallback, ProductSignOut, ProductWorkspaceEntry } from "./ProductIntegration";

function Home() {
  const { user, loading } = useSession();
  return loading ? (
    <div role="status">Opening Weymela…</div>
  ) : (
    <Navigate to={user ? roleHome[user.role] : "/sign-in"} replace />
  );
}
export function App() {
  const session = useSession();
  const location = useLocation();
  if (!session.loading && session.deviceAccess
      && ["Locked", "Cooldown", "RecoveryRequired", "FullAuthenticationRequired"].includes(session.deviceAccess.state))
    return <LockScreen />;
  if (!session.loading && session.user) {
    if (session.accountSecurity && !session.accountSecurity.passwordEnrolled) {
      if (location.pathname !== "/security-setup")
        return <AccountRedirect to="/security-setup" />;
    } else if (session.accountSecurity?.passwordEnrolled) {
      if (location.pathname === "/security-setup")
        return <AccountRedirect to={accountEntryPath(session.user, session.accountSecurity, session.deviceEnrollment)} />;
      const state = session.deviceEnrollment?.state ?? "Unavailable";
      const recognized = state === "Enrolled" || state === "NotRequired";
      if (!recognized && location.pathname !== "/pin-setup")
        return <AccountRedirect to="/pin-setup" />;
      if (recognized && location.pathname === "/pin-setup")
        return <AccountRedirect to={roleHome[session.user.role]} />;
    }
  }
  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/sign-in" element={<SignIn />} />
      <Route path="/pin-setup" element={<PinSetup />} />
      <Route path="/security-setup" element={<SecuritySetup />} />
      <Route path="/onboarding" element={<Onboarding />} />
      <Route path="/product-handoff" element={<ProductHandoffCallback />} />
      <Route path="/integration/sign-out" element={<ProductSignOut />} />
      <Route path="/business" element={
        <ProductWorkspaceEntry role="Business" fallback={<Shell><BusinessDashboard /></Shell>} />
      } />
      <Route path="/creator" element={
        <ProductWorkspaceEntry role="Creator" fallback={<Shell><CreatorDashboard /></Shell>} />
      } />
      <Route path="/admin" element={<RoleGate roles={["PlatformAdmin"]}>
        <ProductWorkspaceEntry role="PlatformAdmin" fallback={<Shell><AdminDashboard /></Shell>} />
      </RoleGate>} />
      <Route path="/customer/offers" element={<RoleGate roles={["Customer"]}>
        <ProductWorkspaceEntry role="Customer" fallback={<Shell><CustomerOffers /></Shell>} />
      </RoleGate>} />
      <Route
        path="/legal/terms-of-service"
        element={<LegalDocumentPage kind="terms" />}
      />
      <Route
        path="/legal/privacy-policy"
        element={<LegalDocumentPage kind="privacy" />}
      />
      <Route element={<RoleGate roles={["Business", "Creator", "Customer", "Cashier", "PlatformAdmin"]}><Shell /></RoleGate>}>
        <Route path="/notifications" element={<Inbox />} />
      </Route>
      <Route
        path="/unauthorized"
        element={
          <div className="offer-page section-kicker-space">
            <Section title="Workspace unavailable">
              <Empty
                icon="lock"
                title="This workspace isn’t available to your role"
                message="Return to your own workspace to continue."
                action={<ActionLink to="/">Your workspace</ActionLink>}
              />
            </Section>
          </div>
        }
      />
      <Route
        element={
          <RoleGate roles={["Business"]}>
            <Shell />
          </RoleGate>
        }
      >
        <Route path="/business/wallet" element={<BusinessWallet />} />
        <Route path="/business/campaigns" element={<BusinessCampaigns />} />
        <Route path="/business/campaigns/new" element={<CreateCampaign />} />
        <Route
          path="/business/campaigns/:id"
          element={<BusinessCampaignDetail />}
        />
        <Route path="/business/requests" element={<BusinessRequests />} />
        <Route path="/business/pricing" element={<BusinessPricingPage />} />
        <Route path="/business/ugc" element={<BusinessUgcPage />} />
      </Route>
      <Route
        element={
          <RoleGate roles={["Creator"]}>
            <Shell />
          </RoleGate>
        }
      >
        <Route path="/creator/discover" element={<CreatorDiscovery />} />
        <Route path="/creator/discover/:id" element={<CreatorOpportunity />} />
        <Route path="/creator/campaigns" element={<CreatorActiveCampaigns />} />
        <Route
          path="/creator/campaigns/:id"
          element={<CreatorActiveDetail />}
        />
        <Route path="/creator/requests" element={<CreatorRequests />} />
        <Route path="/creator/pricing" element={<CreatorHowYouEarn />} />
        <Route path="/creator/ugc" element={<CreatorUgcPage />} />
        <Route path="/creator/earnings" element={<CreatorEarnings />} />
        <Route path="/creator/payouts" element={<CreatorEarnings payout />} />
      </Route>
      <Route
        element={
          <RoleGate roles={["PlatformAdmin"]}>
            <Shell />
          </RoleGate>
        }
      >
        <Route path="/admin/campaigns" element={<AdminCampaigns />} />
        <Route path="/admin/campaigns/:id" element={<AdminCampaignDetail />} />
        <Route path="/admin/businesses" element={<AdminBusinesses />} />
        <Route path="/admin/creators" element={<AdminCreators />} />
        <Route path="/admin/role-enrollments" element={<AdminRoleEnrollments />} />
        <Route path="/admin/settings" element={<AdminFinancialSettings />} />
        <Route
          path="/admin/financial-settings"
          element={<Navigate to="/admin/settings" replace />}
        />
        <Route path="/admin/payouts" element={<AdminPayouts />} />
        <Route path="/admin/platform" element={<AdminPlatformRevenue />} />
        <Route
          path="/admin/notifications"
          element={<AdminActivity notifications />}
        />
        <Route path="/admin/audit" element={<AdminActivity />} />
      </Route>
      <Route
        element={
          <RoleGate roles={["Customer"]}>
            <Shell />
          </RoleGate>
        }
      >
        <Route path="/customer/offers/:id" element={<CustomerOfferQr />} />
        <Route path="/customer/history" element={<CustomerHistory />} />
      </Route>
      <Route
        element={
          <RoleGate roles={["Cashier", "Business"]}>
            <Shell />
          </RoleGate>
        }
      >
        <Route path="/checkout" element={<Checkout />} />
      </Route>
      <Route path="*" element={<Home />} />
    </Routes>
  );
}
