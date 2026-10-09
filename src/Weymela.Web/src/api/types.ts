export type Role =
  | "PlatformAdmin"
  | "OperationsAdmin"
  | "Business"
  | "Creator"
  | "Customer"
  | "Cashier"
  | "Onboarding";
export type CampaignTypeCode = "ViewOnly" | "ViewPlusCommission";
export type CampaignType = CampaignTypeCode | "View Only" | "View & Sale";
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
export interface AccountClosureOption {
  role: Role;
  subjectId: string;
  displayName: string;
  status: "Eligible" | "ActionRequired" | "PendingClosure";
  blockers: string[];
}
export interface AccountClosureOverview {
  roles: AccountClosureOption[];
}
export interface AccountClosureResult {
  role: Role;
  subjectId: string;
  status: "Closed" | "Pending" | "PendingIdentityDeletion";
  blockers: string[];
  remainingRoles: number;
  nextRole: Role | null;
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
  creatorNumber?: number | null;
}
export interface Activity {
  id: string;
  title: string;
  atUtc: string;
  reference: string;
}

export interface AdminAccountSummary {
  id: string;
  userId: string | null;
  name: string;
  role: string;
  status: string;
  approvalState: string | null;
  safeIdentifier: string;
  association: string | null;
  lastActivityAtUtc: string | null;
  canManage: boolean;
  joinedAtUtc?: string | null;
  fullEmail: string | null;
}
export interface AdminAuditItem {
  id: string;
  operation: string;
  action: string;
  occurredAtUtc: string;
  actorUserId: string;
  targetUserId: string | null;
  targetRole: string | null;
  targetSubjectId: string | null;
  correlationId: string;
}
export interface AdminAccountProfile {
  subjectId: string;
  role: string;
  displayName: string;
  publicId: string;
  region: string | null;
  category: string | null;
  active: boolean;
  businessId: string | null;
}
export interface AdminCommerceTransaction {
  id: string;
  occurredAtUtc: string;
  purchaseAmount: number;
  currency: string;
  status: string;
  businessId: string;
  creatorId: string | null;
  customerId: string | null;
  cashierId: string | null;
}
export interface AdminBusinessData { availableWallet: number | null; reservedWallet: number | null; promotions: number; ugcOpportunities: number; ugcCustomerOffers: number; cashiers: number; deposits: number; transactions: number; }
export interface AdminCreatorSocialProfile { id: string; platform: string; profileUrl: string; selfReportedAudience: number; verificationStatus: string; verifiedAudience: number | null; audienceVerificationSource?: string; }
export interface AdminSocialProfileReview { id: string; creatorId: string; creatorName: string; creatorNumber: number | null; creatorPhotoKey: string | null; platform: string; profileUrl: string; selfReportedAudience: number; verifiedAudience: number | null; verificationStatus: string; audienceVerificationSource: string; }
export interface AdminCreatorData { promotionRequests: number; allocations: number; contentSubmissions: number; liveParticipations: number; earnings: number | null; payouts: number; socialProfiles: AdminCreatorSocialProfile[]; }
export interface AdminCustomerData { cashback: number | null; purchases: number; qrHistory: number; payouts: number; }
export interface AdminCashierData { businessId: string | null; businessName: string | null; activationState: string; transactionsProcessed: number; }
export interface AdminAdminData { role: string; active: boolean; grantedAtUtc: string | null; roleHistoryEntries: number; }
export interface AdminAccountDetail {
  account: AdminAccountSummary;
  roles: string[];
  profiles: AdminAccountProfile[];
  audit: AdminAuditItem[];
  transactions: AdminCommerceTransaction[];
  activity: AdminAuditItem[];
  roleData: AdminBusinessData | AdminCreatorData | AdminCustomerData | AdminCashierData | AdminAdminData | null;
}
export interface AccountPreauthorizationResult { preauthorizationId: string; userId: string; role: string; status: string; expiresAtUtc: string; activationInstructionsSent: boolean; }
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
    reason?: string | null;
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
  promotionLiveDurationDays: number;
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
  promotionLiveDurationDays: number;
  status: string;
  version: number;
  slogan?: string | null;
  location?: string | null;
  platforms?: { platform: string; approved: number; capacity: number; available: number; minimumAudience?: number | null }[] | null;
  applicationClosesAtUtc?: string | null;
  contentDueAtUtc?: string | null;
}
export interface Applicant {
  id: string;
  creator: CreatorCard;
  message: string;
  contentConcept: string | null;
  status: string;
  appliedAtUtc: string;
  platform?: string | null;
  socialProfile?: { id: string; platform: string; profileUrl: string; selfReportedAudience: number; verificationStatus: string; verifiedAudience: number | null; audienceVerificationSource?: string } | null;
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
  promotionLiveDurationDays: number;
  earnings: CreatorPrice;
  requestStatus: string | null;
  eligibility: string;
  slogan?: string | null;
  location?: string | null;
  platforms?: { platform: string; approved: number; capacity: number; available: number; minimumAudience?: number | null }[] | null;
  eligibleSocialProfiles?: { id: string; platform: string; profileUrl: string; selfReportedAudience: number; verificationStatus: string; verifiedAudience: number | null }[] | null;
  approvedCreators?: number;
  creatorCapacity?: number;
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
  promotionLiveDurationDays: number;
  contentRevisionNumber?: number | null;
  contentReviewStatus?: "UnderReview" | "ChangesRequested" | "Approved" | "Rejected" | null;
  contentFeedback?: string | null;
  contentSubmittedAtUtc?: string | null;
  wentLiveAtUtc?: string | null;
  expiresAtUtc?: string | null;
  remainingDays?: number | null;
  benefitMode?: "Cashback" | "LegacyDiscount" | string;
  description?: string | null;
  requirements?: string | null;
  location?: string | null;
  contentDueAtUtc?: string | null;
  selectedPlatform?: string | null;
  selectedSocialProfileUrl?: string | null;
  reviewMediaUrl?: string | null;
  publication?: PublicationStatus | null;
  selectedSocialProfileId?: string | null;
}
export interface CreatorRequest {
  id: string;
  campaignId: string;
  campaign: string;
  business: string;
  type: string;
  status: string;
  appliedAtUtc: string;
}
export interface PromotionContentReviewCard {
  submissionId: string;
  creator: string;
  promotion: string;
  provider: string | null;
  contentReference: string | null;
  revisionNumber: number;
  submittedAtUtc: string;
  reviewStatus: string;
  feedback: string | null;
  reviewedAtUtc: string | null;
  reviewMediaUrl?: string | null;
  creatorAllocationId?: string | null;
  promotionId?: string | null;
}
export interface PublicationStatus {
  id: string;
  provider: string;
  externalContentId: string;
  status: string;
  verificationLabel: string;
  requestedAtUtc: string;
  verifiedAtUtc: string | null;
  wentLiveAtUtc: string | null;
  watchUrl: string | null;
}
export interface AdminPublicationReview {
  id: string;
  creator: string;
  business: string;
  work: string;
  workType: string;
  provider: string;
  externalContentId: string;
  socialProfileUrl: string;
  status: string;
  requestedAtUtc: string;
  watchUrl: string;
  wentLiveAtUtc?: string | null;
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
    business?: string | null;
    sourceType?: string | null;
    status?: string;
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
export interface AdminWallets {
  businesses: { businessId: string; business: string; totalBalance: number; available: number; reserved: number; pendingDeposit: number; viewOnlyCount: number; viewSaleCount: number; status: string; lastDepositVia: string | null }[];
  promotions: { id: string; business: string; title: string; type: string; budget: number; used: number; remaining: number; verifiedViews: number; verifiedSales: number; status: string }[];
}
export interface AdminUgcFinance { id: string; business: string; title: string; type: string; budget: number; creatorPayment: number; customerDiscountPercent: number | null; creatorUsed: number; offerUsed: number; discountUsed: number; remaining: number; qualifyingSales: number; status: string; }
export interface AdminReport {
  fromUtc: string; toExclusiveUtc: string;
  activity: { businessDeposits: number; promotionalFunding: number; promotionSpend: number; ugcCreatorSpend: number; ugcOfferSpend: number; ugcCreatorPayments: number; creatorEarnings: number; customerCashback: number; customerDiscounts: number; platformRevenue: number; creatorPayouts: number; customerPayouts: number; verifiedViews: number; verifiedSales: number; ugcQualifyingSales: number };
  promotions: { type: string; allocated: number; used: number; verifiedViews: number; verifiedSales: number; currentRemaining: number; creatorPayments: number; customerDiscounts: number }[];
  ugc: { type: string; allocated: number; used: number; verifiedViews: number; verifiedSales: number; currentRemaining: number; creatorPayments: number; customerDiscounts: number }[];
  accounts: { customers: number; creators: number; businesses: number; newAccountsInPeriod: number };
  currentBusinessWalletBalance: number; currentPlatformUnsettled: number;
  purchases: { id: string; sourceId: string; ugcAssignmentId: string | null; sourceType: string;
    source: string; business: string; purchaseAmount: number; businessCharge: number; customerBenefit: number;
    creatorSaleEarning: number; platformShare: number; occurredAtUtc: string; status: string }[];
}
export interface OperationsHome {
  pendingReviews: number;
  businesses: number;
  creators: number;
  customers: number;
  activeCampaigns: number;
  pendingCreatorPayouts: number;
  pendingCustomerPayouts: number;
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
export interface OperationsBusinessView {
  business: BusinessCard;
  status: string;
  activeCampaigns: number;
  lastDepositUtc: string | null;
  fullEmail: string | null;
}
export interface OperationsCreatorView {
  creator: CreatorCard;
  status: string;
  activeCampaigns: number;
  payoutEligible: boolean;
  fullEmail: string | null;
}
export interface OperationsCustomerView {
  customer: { id: string; displayName: string; publicId: string };
  status: string;
  fullEmail: string | null;
}
export interface OperationsCampaignView {
  id: string;
  publicId: string;
  businessId: string;
  business: string;
  title: string;
  type: string;
  creatorCount: number;
  startUtc: string;
  endUtc: string;
  status: string;
  version: number;
  promotionLiveDurationDays: number;
  slogan: string | null;
  location: string | null;
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
  ugc: UgcSettings | null;
  promotionLiveDurationDays: number;
  enforceAudienceRequirements: boolean;
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
export interface UgcPricing {
  minimumCreatorPayment: number;
  platformFeePercent: number;
  minimumUgcBudget: number | null;
  customerOfferPlatformSalePercent: number | null;
  financialConfigurationVersion: number;
  effectiveFromUtc: string;
}
export interface UgcSettings {
  minimumCreatorPayment: number;
  platformFeePercent: number;
  minimumUgcBudget: number | null;
  customerOfferPlatformSalePercent: number | null;
}
export interface ReceivingDestination {
  id: string;
  method: "Telebirr" | "Bank";
  name: string;
  accountReference: string;
  isActive: boolean;
  sortOrder: number;
  version: number;
}
export interface UgcPlatformRequirement {
  platform: string;
  format: string;
  minimumAudience: number | null;
}
export interface UgcPlatformCapacity {
  platform: string;
  capacity: number;
  approved: number;
  available: number;
}
export interface UgcCard {
  id: string;
  businessId: string;
  business: string;
  title: string;
  slogan: string | null;
  contentType: string;
  status: string;
  creatorPayment: number;
  creatorsNeeded: number;
  approvedCreators: number;
  requiredFunding?: number;
  reservedFunding?: number;
  usedFunding?: number;
  dueDateUtc: string;
  applicationClosesAtUtc?: string | null;
  location: string | null;
  platformRequirements: UgcPlatformRequirement[];
  platformCapacities?: UgcPlatformCapacity[];
  eligibleSocialProfiles?: { id: string; platform: string; profileUrl: string }[] | null;
  requestStatus: string | null;
  version: number;
  customerOfferEnabled?: boolean;
  customerDiscountPercent?: number;
  customerOfferFundedAllocation?: number;
  customerOfferRemaining?: number;
  customerOfferStatus?: string;
  customerFacingSlogan?: string;
  platformFeePercent?: number;
  platformFee?: number;
  productProvided: boolean;
  creatorMustPurchase: boolean;
}
export interface UgcAssignment {
  id: string;
  opportunityId: string;
  opportunity: string;
  businessId: string;
  business: string;
  creatorId: string;
  creator: string;
  creatorNumber?: number | null;
  creatorPayment: number;
  status: string;
  acceptedRevision: number;
  revisionAcceptanceRequired: boolean;
  dueDateUtc: string;
  instructions: string;
  resources: string[];
  location: string | null;
  platformRequirements: UgcPlatformRequirement[];
  feedback: string | null;
  submissionUrl: string | null;
  productProvided: boolean;
  creatorMustPurchase: boolean;
  contentRevisionNumber?: number | null;
  reviewMediaUrl?: string | null;
  publication?: PublicationStatus | null;
  selectedPlatform?: string | null;
  selectedSocialProfileUrl?: string | null;
  selectedSocialProfileId?: string | null;
  reviewMediaContentType?: string | null;
}
export interface UgcRequest {
  id: string;
  opportunityId: string;
  creatorId: string;
  creator: string;
  creatorNumber?: number | null;
  status: string;
  requestedAtUtc: string;
  rejectionReason: string | null;
  socialProfile?: { id: string; platform: string; profileUrl: string; selfReportedAudience: number; verificationStatus: string; verifiedAudience: number | null; audienceVerificationSource?: string } | null;
}
export interface UgcDetail {
  opportunity: UgcCard;
  instructions: string;
  resources: string[];
  productProvided: boolean;
  creatorMustPurchase: boolean;
  usageRights: string | null;
  currentRevision: number;
  requests: UgcRequest[];
  assignments: UgcAssignment[];
  revisions: { revisionNumber: number; isMaterial: boolean; createdAtUtc: string; snapshotJson: string }[];
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
  method: string | null;
  provider: string | null;
  account: string | null;
}
export interface PayoutDestination { method: "Telebirr" | "Mpesa" | "Bank" | string; provider: string; account: string; legalName: string; updatedAtUtc: string | null; isMasked: boolean; isConfigured: boolean; registeredPhone?: string | null; phoneCountry?: string | null; telebirrEligible?: boolean; mpesaEligible?: boolean; bankCountry?: string | null; iban?: string | null; swiftBic?: string | null; routingNumber?: string | null }
export interface PayoutWorkspace {
  creators: QueueRow[];
  customers: QueueRow[];
  platformAccrued: number;
  platformSettled: number;
  platformUnsettled: number;
  history: Payout[];
}
export interface OperationsPayoutWorkspace {
  creators: QueueRow[];
  customers: QueueRow[];
  history: Payout[];
}
export interface OperationsUgcView {
  id: string;
  businessId: string;
  business: string;
  title: string;
  status: string;
  creatorsNeeded: number;
  approvedCreators: number;
  dueDateUtc: string;
  location: string | null;
  currentRevision: number;
  customerOfferStatus: string | null;
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
  ugcAssignmentId?: string | null;
  source: "VIEW_AND_SALE_PROMOTION" | "UGC_CUSTOMER_OFFER" | string;
  offer: string;
  business: {
    displayName: string;
    directionsUrl: string | null;
    latitude: number | null;
    longitude: number | null;
    category?: string | null;
    address?: string | null;
  };
  creator: { displayName: string } | null;
  benefitPercent: number;
  watchUrl: string | null;
  slogan: string | null;
  location: string | null;
  remainingDays?: number | null;
  benefitMode?: "Cashback" | "LegacyDiscount" | string;
}
export interface CustomerTransaction {
  source: "VIEW_AND_SALE_PROMOTION" | "UGC_CUSTOMER_OFFER" | string;
  offer: string;
  business: string;
  creator: string | null;
  purchaseAmount: Money;
  customerPaidAmount: Money | null;
  cashbackEarned: Money | null;
  discountReceived: Money | null;
  purchasedAtUtc: string;
}
export interface CustomerCashbackSummary {
  availableCashback: Money;
  minimumCashOut: Money;
  remainingToCashOut: Money;
  eligible: boolean;
  status: "BelowThreshold" | "Eligible" | "PayoutPrepared" | string;
  payoutHistory: {
    amount: Money;
    status: "Eligible" | "Paid" | string;
    eligibleAtUtc: string;
    paidAtUtc: string | null;
  }[];
}
export interface Qr {
  id: string;
  token: string | null;
  expiresAtUtc: string;
  replayed: boolean;
}
export interface CheckoutOffer {
  sessionId: string;
  offer: string;
  business: { displayName: string };
  creator: { displayName: string } | null;
  customer: string;
  expiresAtUtc: string;
  source: string;
  customerDiscountPercent: number | null;
  benefitMode: "Cashback" | "LegacyDiscount" | string;
}
export interface SaleResult {
  saleId: string;
  purchaseAmount: Money;
  totalBusinessCharge: Money;
  createdAtUtc: string;
  customerPays?: Money | null;
  customerDiscount?: Money | null;
  source?: string;
  benefitMode?: "Cashback" | "LegacyDiscount" | string;
}
export interface Cashier {
  id: string;
  name: string;
  maskedPhone: string;
  status: "Pending Activation" | "Active" | "Disabled" | "Revoked" | string;
  createdAtUtc: string;
  activatedAtUtc: string | null;
}
export interface CashierCreated {
  cashier: Cashier;
  activationCode: string;
}
export interface CashierActivationResult {
  token: { customToken: string; expiresAtUtc: string };
}
export interface ManualCheckoutChoice {
  id: string;
  source: "VIEW_AND_SALE_PROMOTION" | "UGC_CUSTOMER_OFFER" | string;
  label: string;
  benefitPercent: number | null;
  benefitMode: "Cashback" | "LegacyDiscount" | string;
}
export interface ManualCheckoutResolution {
  offers: ManualCheckoutChoice[];
}
export interface CheckoutSaleRow {
  id: string;
  offer: string;
  source: string;
  purchaseAmount: number;
  customerDiscount: number;
  customerPays: number;
  businessCharge: number;
  createdAtUtc: string;
  cashier: string | null;
  creator: string | null;
  customerMasked: string;
  status: string;
  creatorNumber?: number | null;
  creatorEarning?: number | null;
  benefitMode: "Cashback" | "LegacyDiscount" | string;
}
