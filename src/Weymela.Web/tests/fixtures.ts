import { vi } from "vitest";
export const business = {
  id: "biz",
  displayName: "Abc Coffee",
  region: "Addis Ababa",
  directionsUrl: "https://maps.google.com/",
};
export const creator = {
  id: "creator",
  displayName: "Bella",
  publicId: "CR-100",
  region: "Addis Ababa",
  category: "Food",
  verifiedFollowers: 42000,
  verifiedViews: 180000,
  socialVerified: true,
  portfolioUrl: null,
};
export const wallet = {
  totalBalance: 10000,
  available: 4000,
  reserved: 6000,
  version: 2,
  history: [],
};
export const campaign = {
  id: "campaign",
  publicId: "CAM-100",
  businessId: "biz",
  business: "Abc Coffee",
  title: "Coffee stories",
  type: "ViewPlusCommission",
  campaignBudget: 6000,
  assignedToCreators: 2000,
  availableCampaignBudget: 4000,
  used: 300,
  remaining: 5700,
  creatorCount: 1,
  startUtc: "2026-09-01T00:00:00Z",
  endUtc: "2027-10-01T00:00:00Z",
  status: "Active",
  version: 5,
};
export const businessPricing = {
  rows: [
    {
      type: "ViewOnly",
      views: 3000,
      businessPays: 300,
      saleCostPercent: 0,
      minimumCampaignBudget: null,
    },
    {
      type: "ViewPlusCommission",
      views: 3000,
      businessPays: 150,
      saleCostPercent: 10,
      minimumCampaignBudget: null,
    },
  ],
  effectiveFromUtc: "2026-09-01T00:00:00Z",
};
export const creatorPricing = {
  rows: [
    { type: "ViewOnly", views: 3000, youEarn: 200, saleCommissionPercent: 0 },
    {
      type: "ViewPlusCommission",
      views: 3000,
      youEarn: 100,
      saleCommissionPercent: 4.5,
    },
  ],
  minimumToCashOut: 5000,
  effectiveFromUtc: "2026-09-01T00:00:00Z",
};
export const applicant = {
  id: "application",
  creator,
  message: "I have a story to tell.",
  contentConcept: "A local coffee visit",
  status: "Pending",
  appliedAtUtc: "2026-09-11T12:00:00Z",
};
export const budget = {
  id: "budget",
  creator,
  creatorBudget: 2000,
  used: 300,
  budgetRemaining: 1700,
  views: 3000,
  sales: 1,
  status: "Active",
  version: 2,
  canIncrease: true,
};
export const detail = {
  campaign,
  description: "Good coffee, thoughtful stories.",
  requirements: "One original video",
  category: "Food",
  region: "Addis Ababa",
  minimumVerifiedFollowers: 1000,
  pricing: businessPricing.rows[1],
  applicants: [applicant],
  creators: [budget],
  history: [],
};
export const opportunity = {
  id: "campaign",
  publicId: "CAM-100",
  business,
  title: "Coffee stories",
  description: "Good coffee",
  type: "ViewPlusCommission",
  requirements: "Original video",
  category: "Food",
  region: "Addis Ababa",
  minimumVerifiedFollowers: 1000,
  startUtc: campaign.startUtc,
  endUtc: campaign.endUtc,
  earnings: creatorPricing.rows[1],
  requestStatus: null,
  eligibility: "Your verified profile meets the Campaign requirements.",
};
export const active = {
  id: "campaign",
  budgetId: "budget",
  participationId: "participation",
  title: "Coffee stories",
  business,
  type: "ViewPlusCommission",
  yourBudget: 2000,
  budgetRemaining: 1700,
  verifiedViews: 3000,
  rewardedViews: 3000,
  viewEarnings: 200,
  saleCommissionEarnings: 45,
  status: "Active",
  contentStatus: "Content connected",
  provider: "TikTok",
  externalContentId: "7611111111111111111",
  startUtc: campaign.startUtc,
  endUtc: campaign.endUtc,
};
export const earnings = {
  availableEarnings: 5400,
  minimumToCashOut: 5000,
  amountNeeded: 0,
  eligibleAmount: 5000,
  history: [
    {
      id: "earning",
      campaign: "Coffee stories",
      source: "View Reward",
      amount: 200,
      atUtc: "2026-09-11T12:00:00Z",
    },
  ],
  payoutHistory: [],
};
export const viewPrice = {
  viewsPerReward: 3000,
  businessPays: 300,
  creatorEarns: 200,
  platformKeeps: 100,
  minimumCampaignBudget: null,
};
export const settings = {
  viewOnly: viewPrice,
  viewPlusCommission: viewPrice,
  creatorCommissionPercent: 4.5,
  customerCashbackPercent: 2,
  platformPercent: 3.5,
  creatorThreshold: 5000,
  customerThreshold: 500,
  effectiveFromUtc: "2026-09-01T00:00:00Z",
};
export const queue = {
  subjectId: "creator",
  payoutId: null,
  name: "Bella",
  available: 5400,
  threshold: 5000,
  payAmount: 5000,
  eligibleSinceUtc: "2026-09-11T12:00:00Z",
  status: "Eligible",
};
export const payouts = {
  creators: [queue],
  customers: [],
  platformAccrued: 300,
  platformSettled: 100,
  platformUnsettled: 200,
  history: [],
};
export const offer = {
  id: "offer",
  campaignId: "campaign",
  campaign: "Coffee stories",
  business,
  creator,
  cashbackPercent: 2,
  watchUrl: "https://www.tiktok.com/@creator/video/7611111111111111111",
};
export const routes: Record<string, unknown> = {
  "/business/home": {
    business,
    wallet,
    activeCampaigns: 1,
    creatorRequests: 1,
    confirmedSales: 1,
  },
  "/business/wallet": wallet,
  "/business/pricing": businessPricing,
  "/business/campaigns": [campaign],
  "/business/campaigns/campaign": detail,
  "/creator/home": { creator, requests: 1, activeCampaigns: 1, earnings },
  "/creator/pricing": creatorPricing,
  "/creator/discover": [opportunity],
  "/creator/discover/campaign": opportunity,
  "/creator/campaigns": [active],
  "/creator/earnings": earnings,
  "/creator/requests": [],
  "/admin/home": {
    businesses: 1,
    creators: 1,
    activeCampaigns: 1,
    campaignSpend: 300,
    creatorEarnings: 200,
    customerCashback: 20,
    platformRevenue: 100,
    activity: [],
  },
  "/admin/campaigns": [campaign],
  "/admin/campaigns/campaign": {
    campaign,
    creators: [
      {
        creator,
        creatorBudget: 2000,
        used: 300,
        budgetRemaining: 1700,
        baselineViews: 1000,
        latestVerifiedViews: 4000,
        verifiedViews: 3000,
        rewardedViews: 3000,
        viewEarnings: 200,
        verifiedSales: 1,
        saleCommission: 45,
        customerCashback: 20,
        platformRevenue: 100,
        status: "Active",
      },
    ],
    creatorEarnings: 245,
    customerCashback: 20,
    platformRevenue: 100,
    history: [],
  },
  "/admin/businesses": [
    {
      business,
      status: "Active",
      ...wallet,
      activeCampaigns: 1,
      lastDepositUtc: null,
    },
  ],
  "/admin/creators": [
    {
      creator,
      status: "Active",
      activeCampaigns: 1,
      availableEarnings: 5400,
      payoutEligible: true,
    },
  ],
  "/admin/financial-settings": {
    current: settings,
    version: 1,
    versions: [
      {
        id: "settings",
        version: 1,
        effectiveFromUtc: settings.effectiveFromUtc,
        changedBy: "admin",
        settings,
      },
    ],
  },
  "/admin/payouts": payouts,
  "/admin/platform": {
    accrued: { amount: 300 },
    settled: { amount: 100 },
    unsettled: { amount: 200 },
    history: [],
  },
  "/admin/notifications": [],
  "/admin/audit": [],
  "/customer/offers": [offer],
  "/customer/history": [],
};
export function mockApi(overrides: Record<string, unknown> = {}) {
  const data = { ...routes, ...overrides };
  const writes: { path: string; body: any; key: string | null }[] = [];
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input).replace(/^\/api/, "");
    if (init?.method === "POST") {
      writes.push({
        path,
        body: init.body ? JSON.parse(String(init.body)) : null,
        key: new Headers(init.headers).get("Idempotency-Key"),
      });
      const body = path.endsWith("/qr")
        ? {
            id: "qr",
            token: "opaque-test-token",
            expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          }
        : path.endsWith("/resolve")
          ? {
              sessionId: "qr",
              campaign: "Coffee stories",
              business,
              creator,
              customer: "Customer CU-100",
              expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
            }
          : path.endsWith("/confirm")
            ? {
                saleId: "sale",
                purchaseAmount: { amount: 1000 },
                totalBusinessCharge: { amount: 100 },
                createdAtUtc: new Date().toISOString(),
              }
            : { id: "saved" };
      return new Response(JSON.stringify(body), { status: 200 });
    }
    return new Response(JSON.stringify(data[path] ?? { status: "Issued" }), {
      status: 200,
    });
  });
  vi.stubGlobal("fetch", fetch);
  return { fetch, writes };
}
