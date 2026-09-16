export type Role =
  | "PlatformAdmin"
  | "Business"
  | "Creator"
  | "Customer"
  | "Cashier"
  | "Onboarding";
export type CampaignType = "ViewOnly" | "ViewPlusCommission";
export interface SessionUser {
  role: Role;
  displayName: string;
  publicId: string;
  developmentMode: boolean;
  canCheckout: boolean;
  profiles?: SessionProfile[];
  activeProfileKey?: string | null;
}
export interface SessionProfile {
  role: Role;
  subjectId: string;
  businessId: string | null;
  displayName: string;
  publicId: string;
  canCheckout: boolean;
}
export type DeviceEnrollmentState =
  | "EnrollmentRequired"
  | "Enrolled"
  | "Expired"
  | "Revoked"
  | "RecoveryRequired"
  | "NotRequired"
  | "Unavailable";
export interface DeviceEnrollmentStatus {
  state: DeviceEnrollmentState;
  expiresAtUtc: string | null;
}

export interface AccountSecurityStatus {
  passwordEnrolled: boolean;
  phoneEnrolled: boolean;
}
export type DeviceAccessState =
  | "Unlocked"
  | "Locked"
  | "Cooldown"
  | "RecoveryRequired"
  | "EnrollmentRequired"
  | "FullAuthenticationRequired";
export interface DeviceAccessStatus {
  state: DeviceAccessState;
  idleExpiresAtUtc: string | null;
  sessionExpiresAtUtc: string | null;
  retryAfterSeconds: number | null;
}
export interface EmailCodeStartStatus {
  accepted: true;
  expiresAtUtc: string;
  resendAfterSeconds: number;
}
export interface AccountLegalDocument {
  documentId: string;
  kind: "TermsOfService" | "PrivacyPolicy";
  title: string;
  version: string;
  contentHash: string;
  effectiveFromUtc: string;
  viewPath: string;
  accepted: boolean;
}
export interface AccountLegalStatus {
  available: boolean;
  current: boolean;
  documents: AccountLegalDocument[];
}
export interface BusinessCard {
  id: string;
  displayName: string;
  region: string;
  directionsUrl: string | null;
}
export interface CreatorCard {
  id: string;
  displayName: string;
  publicId: string;
  region: string;
  category: string;
  verifiedFollowers: number;
  verifiedViews: number;
  socialVerified: boolean;
  portfolioUrl: string | null;
}
export interface Activity {
  id: string;
  title: string;
  atUtc: string;
  reference: string;
}
export interface Wallet {
  totalBalance: number;
  available: number;
  reserved: number;
  version: number;
  history: {
    id: string;
    label: string;
    amount: number;
    atUtc: string;
    reference: string;
  }[];
}
export interface BusinessHome {
  business: BusinessCard;
  wallet: Wallet;
  activeCampaigns: number;
  creatorRequests: number;
  confirmedSales: number;
}
export interface BusinessPrice {
  type: CampaignType;
  views: number;
  businessPays: number;
  saleCostPercent: number;
  minimumCampaignBudget: number | null;
}
export interface CreatorPrice {
  type: CampaignType;
  views: number;
  youEarn: number;
  saleCommissionPercent: number;
}
export interface BusinessPricing {
  rows: BusinessPrice[];
  effectiveFromUtc: string;
}
export interface CreatorPricing {
  rows: CreatorPrice[];
  minimumToCashOut: number;
  effectiveFromUtc: string;
}
export interface CampaignRow {
  id: string;
  publicId: string;
  businessId: string;
  business: string;
  title: string;
  type: CampaignType;
  campaignBudget: number;
  assignedToCreators: number;
  availableCampaignBudget: number;
  used: number;
  remaining: number;
  creatorCount: number;
  startUtc: string;
  endUtc: string;
  status: string;
  version: number;
}
export interface Applicant {
  id: string;
  creator: CreatorCard;
  message: string;
  contentConcept: string | null;
  status: string;
  appliedAtUtc: string;
}
export interface CreatorBudget {
  id: string;
  creator: CreatorCard;
  creatorBudget: number;
  used: number;
  budgetRemaining: number;
  views: number;
  sales: number;
  status: string;
  version: number;
  canIncrease: boolean;
}
export interface BusinessCampaign {
  campaign: CampaignRow;
  description: string;
  requirements: string | null;
  category: string | null;
  region: string | null;
  minimumVerifiedFollowers: number | null;
  pricing: BusinessPrice;
  applicants: Applicant[];
  creators: CreatorBudget[];
  history: Activity[];
}
export interface Opportunity {
  id: string;
  publicId: string;
  business: BusinessCard;
  title: string;
  description: string;
  type: CampaignType;
  requirements: string | null;
  category: string | null;
  region: string | null;
  minimumVerifiedFollowers: number | null;
  startUtc: string;
  endUtc: string;
  earnings: CreatorPrice;
  requestStatus: string | null;
  eligibility: string;
}
export interface CreatorCampaign {
  id: string;
  budgetId: string;
  participationId: string | null;
  title: string;
  business: BusinessCard;
  type: CampaignType;
  yourBudget: number;
  budgetRemaining: number;
  verifiedViews: number;
  rewardedViews: number;
  viewEarnings: number;
  saleCommissionEarnings: number;
  status: string;
  contentStatus: string;
  provider: string | null;
  externalContentId: string | null;
  startUtc: string;
  endUtc: string;
}
export interface Payout {
  id: string;
  kind: string;
  name: string;
  amount: number;
  threshold: number;
  status: string;
  eligibleAtUtc: string;
  paidAtUtc: string | null;
  reference: string | null;
}
export interface Earnings {
  availableEarnings: number;
  minimumToCashOut: number;
  amountNeeded: number;
  eligibleAmount: number;
  history: {
    id: string;
    campaign: string;
    source: string;
    amount: number;
    atUtc: string;
  }[];
  payoutHistory: Payout[];
}
export interface CreatorHome {
  creator: CreatorCard;
  requests: number;
  activeCampaigns: number;
  earnings: Earnings;
}
export interface AdminHome {
  businesses: number;
  creators: number;
  activeCampaigns: number;
  campaignSpend: number;
  creatorEarnings: number;
  customerCashback: number;
  platformRevenue: number;
  activity: Activity[];
}
export interface BusinessOversight {
  business: BusinessCard;
  status: string;
  totalBalance: number;
  available: number;
  reserved: number;
  activeCampaigns: number;
  lastDepositUtc: string | null;
}
export interface CreatorOversight {
  creator: CreatorCard;
  status: string;
  activeCampaigns: number;
  availableEarnings: number;
  payoutEligible: boolean;
}
export interface AdminCreator {
  creator: CreatorCard;
  creatorBudget: number;
  used: number;
  budgetRemaining: number;
  baselineViews: number;
  latestVerifiedViews: number;
  verifiedViews: number;
  rewardedViews: number;
  viewEarnings: number;
  verifiedSales: number;
  saleCommission: number;
  customerCashback: number;
  platformRevenue: number;
  status: string;
}
export interface AdminCampaign {
  campaign: CampaignRow;
  creators: AdminCreator[];
  creatorEarnings: number;
  customerCashback: number;
  platformRevenue: number;
  history: Activity[];
}
export interface ViewPriceInput {
  viewsPerReward: number;
  businessPays: number;
  creatorEarns: number;
  platformKeeps: number;
  minimumCampaignBudget: number | null;
}
export interface FinancialSettings {
  viewOnly: ViewPriceInput;
  viewPlusCommission: ViewPriceInput;
  creatorCommissionPercent: number;
  customerCashbackPercent: number;
  platformPercent: number;
  creatorThreshold: number;
  customerThreshold: number;
  effectiveFromUtc: string | null;
}
export interface SettingsWorkspace {
  current: FinancialSettings;
  version: number;
  versions: {
    id: string;
    version: number;
    effectiveFromUtc: string;
    changedBy: string;
    settings: FinancialSettings;
  }[];
}
export interface QueueRow {
  subjectId: string;
  payoutId: string | null;
  name: string;
  available: number;
  threshold: number;
  payAmount: number;
  eligibleSinceUtc: string | null;
  status: string;
}
export interface PayoutWorkspace {
  creators: QueueRow[];
  customers: QueueRow[];
  platformAccrued: number;
  platformSettled: number;
  platformUnsettled: number;
  history: Payout[];
}
export interface Money {
  amount: number;
  currency: string;
}
export interface PlatformSummary {
  accrued: Money;
  settled: Money;
  unsettled: Money;
  history: {
    id: string;
    amount: Money;
    reference: string;
    settledAtUtc: string;
    settledBy: string | null;
  }[];
}
export interface Offer {
  id: string;
  campaignId: string;
  campaign: string;
  business: BusinessCard;
  creator: CreatorCard;
  cashbackPercent: number;
  watchUrl: string | null;
}
export interface Qr {
  id: string;
  token: string | null;
  expiresAtUtc: string;
  replayed: boolean;
}
export interface CheckoutOffer {
  sessionId: string;
  campaign: string;
  business: { id: string; displayName: string };
  creator: { id: string; publicId: string; displayName: string };
  customer: string;
  expiresAtUtc: string;
}
export interface SaleResult {
  saleId: string;
  purchaseAmount: Money;
  totalBusinessCharge: Money;
  createdAtUtc: string;
}
