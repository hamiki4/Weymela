import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { RoleGate, roleHome, SessionLoadFailure, SessionTransition, useSession } from "./Session";
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
import { BusinessUgcPage } from "../features/business/UgcPages";
import {
  CreatorEarnings,
  CreatorOpportunity,
} from "../features/creator/CreatorPages";
import { CreatorDashboard, CreatorDiscover, CreatorPromotions } from "../features/creator/CreatorExperience";
import {
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
  OperationsCustomers,
  OperationsDashboard,
  OperationsUgc,
} from "../features/admin/AdminPages";
import { AdminAccountDetail, AdminAccounts } from "../features/admin/AdminAccounts";
import { AdminFinancialSettings } from "../features/admin/FinancialSettings";
import { AdminPayouts, AdminPlatformRevenue } from "../features/admin/Payouts";
import {
  CustomerDiscover,
  CustomerCashback,
  CustomerOfferQr,
  CustomerOffers,
  CustomerTransactions,
} from "../features/commerce/CustomerPages";
import { Checkout } from "../features/commerce/Checkout";
import { Inbox } from "../features/notifications/Inbox";
import { Onboarding } from "./Onboarding";
import { PinSetup } from "./PinSetup";
import { LockScreen } from "./LockScreen";
import { SecuritySetup } from "./SecuritySetup";
import { AccountRedirect, accountEntryPath } from "./AccountEntry";
import { LegalDocumentPage } from "./LegalDocumentPage";
import { CashierActivation } from "./CashierActivation";
import { AccountActivation } from "./AccountActivation";
import { BusinessCashiers } from "../features/business/BusinessCashiers";


function Home() {
  const { user, loading } = useSession();
  return loading ? (
    <div role="status">Opening Weymela…</div>
  ) : (
    <Navigate to={user ? roleHome[user.role] : "/sign-in"} replace />
  );
}
function ProtectedShell() {
  const { user, loading, resolution } = useSession();
  if (!user) return loading || resolution === "resolving" ? <SessionTransition /> : <Navigate to="/sign-in" replace />;
  return <Shell />;
}
export function App() {
  const session = useSession();
  const location = useLocation();
  // A null user is not authoritative while bootstrap is pending. Keep every
  // route behind one stable transition state until auth, profile, device and
  // workspace prerequisites have been resolved.
  if (session.loadFailed || session.resolution === "failed") return <SessionLoadFailure retry={session.refresh} />;
  if (session.deviceAccess
      && ["Locked", "Cooldown", "RecoveryRequired", "FullAuthenticationRequired"].includes(session.deviceAccess.state))
    return <LockScreen />;
  if ((session.loading || session.resolution === "resolving") && !session.user
      && location.pathname !== "/sign-in") return <SessionTransition />;
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
      if (recognized && (location.pathname === "/pin-setup" || location.pathname === "/sign-in"))
        return <AccountRedirect to={roleHome[session.user.role]} />;
    }
  }
  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/sign-in" element={<SignIn />} />
      <Route path="/cashier/activate" element={<CashierActivation />} />
      <Route path="/account/activate" element={<AccountActivation />} />
      <Route path="/pin-setup" element={<PinSetup />} />
      <Route path="/security-setup" element={<SecuritySetup />} />
      <Route path="/onboarding" element={<Onboarding />} />
      <Route path="/legal/terms-of-service" element={<LegalDocumentPage kind="terms" />} />
      <Route path="/legal/privacy-policy" element={<LegalDocumentPage kind="privacy" />} />
      <Route path="/unauthorized" element={
        <div className="offer-page section-kicker-space">
          <Section title="Workspace unavailable">
            <Empty icon="lock" title="This workspace isn’t available to your role"
              message="Return to your own workspace to continue."
              action={<ActionLink to="/">Your workspace</ActionLink>} />
          </Section>
        </div>
      } />
      <Route element={<ProtectedShell />}>
        <Route path="/notifications" element={<RoleGate roles={["Business", "Creator", "Customer", "Cashier", "PlatformAdmin", "OperationsAdmin"]}><Inbox /></RoleGate>} />
        <Route element={<RoleGate roles={["Business"]} />}>
          <Route path="/business" element={<BusinessDashboard />} />
          <Route path="/business/wallet" element={<BusinessWallet />} />
          <Route path="/business/campaigns" element={<BusinessCampaigns />} />
          <Route path="/business/campaigns/new" element={<CreateCampaign />} />
          <Route path="/business/campaigns/:id" element={<BusinessCampaignDetail />} />
          <Route path="/business/requests" element={<BusinessRequests />} />
          <Route path="/business/pricing" element={<BusinessPricingPage />} />
          <Route path="/business/ugc" element={<BusinessUgcPage />} />
          <Route path="/business/cashiers" element={<BusinessCashiers />} />
        </Route>
        <Route element={<RoleGate roles={["Creator"]} />}>
          <Route path="/creator" element={<CreatorDashboard />} />
          <Route path="/creator/discover" element={<CreatorDiscover />} />
          <Route path="/creator/discover/:id" element={<CreatorOpportunity />} />
          <Route path="/creator/promotions" element={<CreatorPromotions />} />
          <Route path="/creator/promotions/:id" element={<CreatorActiveDetail />} />
          <Route path="/creator/campaigns" element={<Navigate to="/creator/promotions" replace />} />
          <Route path="/creator/campaigns/:id" element={<CreatorActiveDetail />} />
          <Route path="/creator/requests" element={<Navigate to="/creator/promotions" replace />} />
          <Route path="/creator/pricing" element={<Navigate to="/creator/earnings#how-you-earn" replace />} />
          <Route path="/creator/ugc" element={<Navigate to="/creator/discover?tab=UGC" replace />} />
          <Route path="/creator/earnings" element={<CreatorEarnings />} />
          <Route path="/creator/payouts" element={<Navigate to="/creator/earnings" replace />} />
        </Route>
        <Route element={<RoleGate roles={["PlatformAdmin"]} />}>
          <Route path="/admin" element={<AdminDashboard />} />
          <Route path="/admin/accounts" element={<Navigate to="/admin/customers" replace />} />
          <Route path="/admin/accounts/:id" element={<AdminAccountDetail />} />
          <Route path="/admin/customers" element={<AdminAccounts area="Customer" />} />
          <Route path="/admin/creators" element={<AdminAccounts area="Creator" />} />
          <Route path="/admin/businesses" element={<AdminAccounts area="Business" />} />
          <Route path="/admin/admins" element={<AdminAccounts area="Admin" />} />
          <Route path="/admin/settings" element={<AdminFinancialSettings />} />
          <Route path="/admin/financial-settings" element={<Navigate to="/admin/settings" replace />} />
          <Route path="/admin/platform" element={<AdminPlatformRevenue />} />
        </Route>
        <Route element={<RoleGate roles={["OperationsAdmin"]} />}>
          <Route path="/admin/operations" element={<OperationsDashboard />} />
          <Route path="/admin/ugc" element={<OperationsUgc />} />
        </Route>
        <Route element={<RoleGate roles={["PlatformAdmin", "OperationsAdmin"]} />}>
          <Route path="/admin/campaigns" element={<AdminCampaigns />} />
          <Route path="/admin/campaigns/:id" element={<AdminCampaignDetail />} />
          <Route path="/admin/operations/businesses" element={<AdminBusinesses />} />
          <Route path="/admin/operations/creators" element={<AdminCreators />} />
          <Route path="/admin/operations/customers" element={<OperationsCustomers />} />
          <Route path="/admin/role-enrollments" element={<AdminRoleEnrollments />} />
          <Route path="/admin/payouts" element={<AdminPayouts />} />
          <Route path="/admin/notifications" element={<AdminActivity />} />
        </Route>
        <Route element={<RoleGate roles={["Customer"]} />}>
          <Route path="/customer/offers" element={<CustomerOffers />} />
          <Route path="/customer/discover" element={<CustomerDiscover />} />
          <Route path="/customer/offers/:id" element={<CustomerOfferQr />} />
          <Route path="/customer/transactions" element={<CustomerTransactions />} />
          <Route path="/customer/history" element={<Navigate to="/customer/transactions" replace />} />
          <Route path="/customer/cashback" element={<CustomerCashback />} />
        </Route>
        <Route element={<RoleGate roles={["Cashier", "Business"]} />}>
          <Route path="/checkout" element={<Checkout />} />
        </Route>
      </Route>
      <Route path="*" element={<Home />} />
    </Routes>
  );
}
