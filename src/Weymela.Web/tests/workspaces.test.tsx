import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ReactNode } from "react";
import {
  BusinessDashboard,
  BusinessWallet,
  BusinessPricingPage,
  BusinessCampaigns,
} from "../src/features/business/BusinessPages";
import { CreateCampaign } from "../src/features/business/CreateCampaign";
import { BusinessCampaignDetail } from "../src/features/business/CampaignDetail";
import { PromotionContentReviewQueue } from "../src/features/business/PromotionContentReviewQueue";
import {
  CreatorOpportunity,
  CreatorHowYouEarn,
  CreatorEarnings,
} from "../src/features/creator/CreatorPages";
import {
  CreatorDashboard,
  CreatorDiscover,
  CreatorPromotions,
} from "../src/features/creator/CreatorExperience";
import {
  CreatorActiveDetail,
} from "../src/features/creator/ActiveCampaigns";
import {
  AdminCampaigns,
  AdminCampaignDetail,
  AdminBusinesses,
} from "../src/features/admin/AdminPages";
import { AdminFinancialSettings } from "../src/features/admin/FinancialSettings";
import { AdminPayouts } from "../src/features/admin/Payouts";
import {
  CustomerOffers,
  CustomerOfferQr,
  CustomerTransactions,
  CustomerCashback,
} from "../src/features/commerce/CustomerPages";
import { Checkout } from "../src/features/commerce/Checkout";
import {
  campaign,
  detail,
  earnings,
  active,
  mockApi,
  offer,
  ugcCustomerOffer,
  wallet,
} from "./fixtures";
vi.mock("../src/app/Session", () => ({
  useSession: () => ({ user: { developmentMode: true, role: "Business" } }),
}));
vi.mock("qrcode", () => ({
  default: { toDataURL: vi.fn(async () => "data:image/png;base64,fixture") },
}));
function mount(element: ReactNode, path = "/", pattern = "*") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path={pattern} element={element} />
        <Route
          path="/business/campaigns/saved"
          element={<h1>Saved Campaign</h1>}
        />
      </Routes>
    </MemoryRouter>,
  );
}
beforeEach(() => {
  vi.restoreAllMocks();
  mockApi();
});

describe("Business workspace", () => {
  it("groups authoritative advertising fund balances", async () => {
    mount(<BusinessDashboard />);
    expect(
      await screen.findByRole("heading", { name: "Advertising Funds" }),
    ).toBeVisible();
    expect(screen.getByText("10,000")).toBeVisible();
    expect(screen.getByText("4,000")).toBeVisible();
    expect(screen.getByText("6,000")).toBeVisible();
    expect(
      screen.getByRole("link", {
        name: "Pricing",
      }),
    ).toBeVisible();
  });
  it("records any positive deposit with the current wallet version", async () => {
    const api = mockApi();
    mount(<BusinessWallet />);
    await screen.findByLabelText("Amount");
    await userEvent.type(screen.getByLabelText("Amount"), "12.34");
    await userEvent.click(screen.getByRole("button", { name: "Add Funds" }));
    await waitFor(() =>
      expect(api.writes[0]?.body).toEqual({
        amount: 12.34,
        expectedVersion: 2,
      }),
    );
  });
  it("rejects a zero deposit in the rendered form", async () => {
    const api = mockApi();
    mount(<BusinessWallet />);
    const input = await screen.findByLabelText("Amount");
    await userEvent.type(input, "0");
    await userEvent.click(screen.getByRole("button", { name: "Add Funds" }));
    expect(input).toBeInvalid();
    expect(api.writes).toHaveLength(0);
  });
  it("shows compact pricing cards and no internal split", async () => {
    mount(<BusinessPricingPage />);
    expect(
      await screen.findByRole("region", { name: "Business pricing options" }),
    ).toBeVisible();
    expect(screen.getAllByRole("article")).toHaveLength(3);
    expect(screen.getByText("10% per verified sale")).toBeVisible();
    expect(
      screen.queryByText(/Creator:|Customer Cashback|Platform Keeps/),
    ).not.toBeInTheDocument();
  });
  it("creates a guided Campaign without accepting platform rates", async () => {
    const api = mockApi();
    mount(<CreateCampaign />);
    await screen.findByLabelText("Campaign Title");
    await userEvent.type(
      screen.getByLabelText("Campaign Title"),
      "Local stories",
    );
    await userEvent.type(
      screen.getByLabelText("Description"),
      "A thoughtful visit",
    );
    await userEvent.type(
      screen.getByLabelText("Start date"),
      "2026-09-12T10:00",
    );
    await userEvent.type(screen.getByLabelText("End date"), "2027-09-20T10:00");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await userEvent.type(screen.getByLabelText("Creator category"), "Food");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await userEvent.type(
      screen.getByLabelText("Campaign Budget"),
      "1000",
    );
    await userEvent.click(screen.getByRole("button", { name: "Create Draft" }));
    await screen.findByRole("heading", { name: "Saved Campaign" });
    expect(api.writes[0].body.campaignBudget).toBe(1000);
    expect(api.writes[0].body).not.toHaveProperty("creatorCommissionPercent");
  });
  it("shows funding confirmation before reserving any money", async () => {
    const api = mockApi({
      "/business/campaigns/campaign": {
        ...detail,
        campaign: { ...campaign, status: "Draft", campaignBudget: 1000 },
      },
    });
    mount(
      <BusinessCampaignDetail />,
      "/business/campaigns/campaign",
      "/business/campaigns/:id",
    );
    await userEvent.click(
      await screen.findByRole("button", { name: "Review Funding" }),
    );
    const dialog = screen.getByRole("dialog", {
      name: "Confirm Campaign Funding",
    });
    expect(
      within(dialog).getByText("3,000 Available / 7,000 Reserved"),
    ).toBeVisible();
    expect(api.writes).toHaveLength(0);
    await userEvent.click(
      within(dialog).getByRole("button", { name: "Confirm & Reserve Funds" }),
    );
    await waitFor(() =>
      expect(api.writes[0].body).toEqual({
        campaignVersion: 5,
        walletVersion: 2,
      }),
    );
  });
  it("prevents funding when the wallet cannot cover the Campaign Budget", async () => {
    mockApi({
      "/business/campaigns/campaign": {
        ...detail,
        campaign: { ...campaign, status: "Draft" },
      },
      "/business/wallet": { ...wallet, available: 100 },
    });
    mount(
      <BusinessCampaignDetail />,
      "/business/campaigns/campaign",
      "/business/campaigns/:id",
    );
    await userEvent.click(
      await screen.findByRole("button", { name: "Review Funding" }),
    );
    expect(
      screen.getByRole("button", { name: "Confirm & Reserve Funds" }),
    ).toBeDisabled();
  });
  it("approves an applicant and sets Creator Budget in one request", async () => {
    const api = mockApi();
    mount(
      <BusinessCampaignDetail />,
      "/business/campaigns/campaign?tab=applicants",
      "/business/campaigns/:id",
    );
    await userEvent.click(
      await screen.findByRole("button", { name: "Approve" }),
    );
    const d = screen.getByRole("dialog", { name: "Approve Bella" });
    await userEvent.type(
      within(d).getByLabelText("Creator Budget"),
      "1500",
    );
    await userEvent.click(
      within(d).getByRole("button", { name: "Approve & Set Budget" }),
    );
    await waitFor(() =>
      expect(api.writes[0].body).toEqual({ amount: 1500, version: 5 }),
    );
    expect(api.writes).toHaveLength(1);
  });
  it("blocks a Creator Budget larger than available Campaign funds", async () => {
    mount(
      <BusinessCampaignDetail />,
      "/business/campaigns/campaign?tab=applicants",
      "/business/campaigns/:id",
    );
    await userEvent.click(
      await screen.findByRole("button", { name: "Approve" }),
    );
    await userEvent.type(screen.getByLabelText("Creator Budget"), "5000");
    expect(
      screen.getByRole("button", { name: "Approve & Set Budget" }),
    ).toBeDisabled();
  });
  it("increases a Creator Budget using its concurrency version", async () => {
    const api = mockApi();
    mount(
      <BusinessCampaignDetail />,
      "/business/campaigns/campaign?tab=budgets",
      "/business/campaigns/:id",
    );
    await userEvent.click(
      (await screen.findAllByRole("button", { name: "Increase Budget" }))[0],
    );
    const d = screen.getByRole("dialog", { name: "Increase Bella’s Budget" });
    await userEvent.type(
      within(d).getByLabelText("Amount to add"),
      "100",
    );
    await userEvent.click(
      within(d).getByRole("button", { name: "Confirm Increase" }),
    );
    await waitFor(() =>
      expect(api.writes[0].body).toEqual({ amount: 100, version: 2 }),
    );
    expect(
      screen.queryByRole("button", { name: "Decrease Budget" }),
    ).not.toBeInTheDocument();
  });
  it("has no destructive End Campaign action", async () => {
    mount(<BusinessCampaigns />);
    await screen.findByText("Active Campaigns and drafts");
    expect(
      screen.queryByRole("button", { name: /End Campaign|End Promotion/ }),
    ).not.toBeInTheDocument();
  });
  it("lets the owning Business request changes on a submitted Promotion revision", async () => {
    const api = mockApi({
      "/business/promotion-content-submissions": [{
        submissionId: "submission-safe-key",
        creator: "Mina Creator",
        promotion: "Seasonal stories",
        provider: "TikTok",
        contentReference: "video-123",
        revisionNumber: 1,
        submittedAtUtc: "2026-09-22T12:00:00Z",
        reviewStatus: "UnderReview",
        feedback: null,
        reviewedAtUtc: null,
      }],
    });
    mount(<PromotionContentReviewQueue />);
    expect(await screen.findByText("Mina Creator · Seasonal stories")).toBeVisible();
    expect(screen.getByText("TikTok content · Revision 1")).toBeVisible();
    expect(screen.queryByText(/AllocationId|BusinessId|CreatorId|Wallet|Commission/)).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Feedback (required for changes requested)"), "Please adjust the opening shot.");
    await userEvent.click(screen.getByRole("button", { name: "Request Changes" }));
    await waitFor(() => expect(api.writes[0]).toMatchObject({
      path: "/business/promotion-content-submissions/submission-safe-key/review",
      body: { action: "requestchanges", feedback: "Please adjust the opening shot." },
    }));
  });
});

describe("Creator workspace", () => {
  it("groups own earnings and Campaigns", async () => {
    mount(<CreatorDashboard />);
    expect(
      await screen.findByRole("heading", { name: "Dashboard" }),
    ).toBeVisible();
    expect(screen.getAllByText("5,400").length).toBeGreaterThan(0);
  });
  it("discovers Business-created Promotions and keeps All, Promotions and UGC filters", async () => {
    mockApi({ "/creator/ugc": [] });
    mount(<CreatorDiscover />);
    expect(await screen.findByRole("heading", { name: "Available Opportunities" })).toBeVisible();
    const tabs = within(screen.getByRole("tablist", { name: "Opportunity type" }));
    expect(tabs.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["All", "Promotions", "UGC"]);
    expect(await screen.findByText(/Abc Coffee/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Review Promotion" })).toHaveAttribute("href", "/creator/discover/campaign");
    expect(screen.queryByText(/Business Wallet|Reserved Funds|Campaign Budget/)).not.toBeInTheDocument();
  });
  it("has a designed empty discovery state", async () => {
    mockApi({ "/creator/discover": [], "/creator/ugc": [] });
    mount(<CreatorDiscover />);
    expect(await screen.findByText("No available Promotions")).toBeVisible();
    expect(screen.getByText("No available UGC opportunities")).toBeVisible();
  });
  it("sends a join message without negotiating budget or rates", async () => {
    const api = mockApi();
    mount(
      <CreatorOpportunity />,
      "/creator/discover/campaign",
      "/creator/discover/:id",
    );
    await userEvent.type(
      await screen.findByLabelText("Short message"),
      "My idea",
    );
    await userEvent.click(
      screen.getByRole("button", { name: "Request to Join" }),
    );
    await waitFor(() =>
      expect(api.writes[0].body).toEqual({
        message: "My idea",
        contentConcept: null,
      }),
    );
    expect(api.writes[0].path).toBe("/creator/promotions/campaign/request");
  });
  it("shows own Promotion progress without Business budget or wallet details", async () => {
    mockApi({ "/creator/campaigns": [{ ...active, remainingDays: 20 }] });
    mount(<CreatorPromotions />);
    expect(await screen.findByRole("heading", { name: "My Promotions" })).toBeVisible();
    expect(screen.getByText("20 days left")).toBeVisible();
    expect(screen.getByText(/3,000 verified views/)).toBeVisible();
    expect(screen.queryByText(/Your Budget|Budget Remaining|Campaign Budget/)).not.toBeInTheDocument();
    expect(screen.queryByText("Business Wallet")).not.toBeInTheDocument();
  });
  it("refreshes verified views through the participation endpoint", async () => {
    const api = mockApi({ "/creator/campaigns": [{ ...active, remainingDays: 30 }] });
    mount(
      <CreatorActiveDetail />,
      "/creator/campaigns/budget",
      "/creator/campaigns/:id",
    );
    await userEvent.click(
      await screen.findByRole("button", { name: "Refresh Views" }),
    );
    await waitFor(() =>
      expect(api.writes[0].path).toBe(
        "/creator/participations/participation/refresh",
      ),
    );
  });
  it("keeps Go Live unavailable while submitted content is under Business review", async () => {
    mockApi({ "/creator/campaigns": [{
      ...active,
      participationId: null,
      status: "UnderReview",
      contentReviewStatus: "UnderReview",
      contentRevisionNumber: 1,
      remainingDays: null,
    }] });
    mount(<CreatorActiveDetail />, "/creator/promotions/budget", "/creator/promotions/:id");
    expect(await screen.findByText("Content is under Business review. Go Live is not available yet.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Go Live" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Submit Content for Review" })).not.toBeInTheDocument();
  });
  it("allows the Creator to start the live window only after content approval", async () => {
    const api = mockApi({ "/creator/campaigns": [{
      ...active,
      participationId: null,
      status: "ReadyToGoLive",
      contentReviewStatus: "Approved",
      contentRevisionNumber: 1,
      remainingDays: null,
    }] });
    mount(<CreatorActiveDetail />, "/creator/promotions/budget", "/creator/promotions/:id");
    await userEvent.click(await screen.findByRole("button", { name: "Go Live" }));
    await waitFor(() => expect(api.writes[0].path).toBe("/creator/creator-budgets/budget/go-live"));
  });
  it("has compact earning cards without other role finances", async () => {
    mount(<CreatorHowYouEarn />);
    expect(
      await screen.findByRole("region", { name: "Creator earning options" }),
    ).toBeVisible();
    expect(screen.getAllByRole("article")).toHaveLength(2);
    expect(screen.getByText("Minimum to cash out: 5,000")).toBeVisible();
    expect(
      screen.queryByText(/Business Pays|Platform Keeps|Customer Cashback/),
    ).not.toBeInTheDocument();
  });
  it("shows friendly earning sources", async () => {
    mount(<CreatorEarnings />);
    expect((await screen.findAllByText("View Reward")).length).toBeGreaterThan(
      0,
    );
    expect(screen.queryByText("VIEW_REWARD")).not.toBeInTheDocument();
  });
  it("shows threshold eligibility without payout calendar dates", async () => {
    mount(<CreatorEarnings />);
    expect(
      await screen.findByText("Eligible for payout · 5,000"),
    ).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Request Payout" }),
    ).toBeEnabled();
  });
  it("shows amount remaining and disables payout below threshold", async () => {
    mockApi({
      "/creator/earnings": {
        ...earnings,
        availableEarnings: 400,
        amountNeeded: 4600,
        eligibleAmount: 0,
      },
    });
    mount(<CreatorEarnings />);
    expect(await screen.findByText("4,600 more needed")).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Request Payout" }),
    ).toBeDisabled();
  });
});

describe("Admin workspace", () => {
  it("filters Campaigns with a designed empty result", async () => {
    mount(<AdminCampaigns />);
    await userEvent.type(
      await screen.findByLabelText("Business"),
      "No matching Business",
    );
    expect(await screen.findByText("No Campaigns to show")).toBeVisible();
  });
  it("shows the full Campaign financial oversight", async () => {
    mount(
      <AdminCampaignDetail />,
      "/admin/campaigns/campaign",
      "/admin/campaigns/:id",
    );
    expect(await screen.findByText("Financial summary")).toBeVisible();
    expect(screen.getAllByText("Customer cashback").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Platform revenue").length).toBeGreaterThan(0);
  });
  it("loads saved settings and rejects an invalid view split", async () => {
    mount(<AdminFinancialSettings />);
    const input = await screen.findByLabelText("View Only Business Pays");
    expect(input).toHaveValue(300);
    await userEvent.clear(input);
    await userEvent.type(input, "400");
    expect(
      screen.getByRole("button", { name: "Save Financial Settings" }),
    ).toBeDisabled();
  });
  it("supports scheduled effective dates without applying them silently", async () => {
    mount(<AdminFinancialSettings />);
    await userEvent.click(await screen.findByLabelText("Schedule for Later"));
    expect(screen.getByLabelText("Effective from")).toBeRequired();
  });
  it("submits valid settings with the saved version", async () => {
    const api = mockApi();
    mount(<AdminFinancialSettings />);
    const duration = await screen.findByLabelText("Promotion live duration");
    expect(duration).toHaveValue(30);
    await userEvent.clear(duration);
    await userEvent.type(duration, "20");
    await userEvent.click(
      await screen.findByRole("button", { name: "Save Financial Settings" }),
    );
    await waitFor(() => expect(api.writes[0].body.expectedVersion).toBe(1));
    expect(api.writes[0].body.settings.effectiveFromUtc).toBeNull();
    expect(api.writes[0].body.settings.promotionLiveDurationDays).toBe(20);
  });
  it("has all four payout tabs and supports keyboard selection", async () => {
    mount(<AdminPayouts />);
    await screen.findByRole("heading", { name: "Creators payout queue" });
    expect(screen.getAllByRole("tab")).toHaveLength(4);
    screen.getByRole("tab", { name: "Creators" }).focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(screen.getByRole("tab", { name: "Customers" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });
  it("requires explicit external payment confirmation", async () => {
    mount(<AdminPayouts />);
    await userEvent.click(
      (await screen.findAllByRole("button", { name: "Mark Paid" }))[0],
    );
    expect(screen.getByRole("button", { name: "Confirm Paid" })).toBeDisabled();
    expect(
      screen.getByLabelText(
        "I confirm this payment has been completed externally.",
      ),
    ).not.toBeChecked();
  });
  it("shows Business balances without a Business minimum", async () => {
    mount(<AdminBusinesses />);
    expect((await screen.findAllByText("Available")).length).toBeGreaterThan(0);
    expect(
      screen.queryByText("Required Business Minimum"),
    ).not.toBeInTheDocument();
  });
});

describe("Accepted commerce compatibility", () => {
  it("renders the safe Customer offer projection with cashback, directions and video", async () => {
    const api = mockApi();
    mount(<CustomerOffers />);
    expect(
      await screen.findByRole("link", { name: "Get Offer" }),
    ).toBeVisible();
    expect(screen.getByText("2% cashback")).toBeVisible();
    expect(screen.getByText("Good coffee, thoughtful stories.")).toBeVisible();
    expect(screen.getByText("By Bella")).toBeVisible();
    expect(screen.getByRole("link", { name: "Watch Promotion" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Get Directions" })).toBeVisible();
    expect(screen.queryByText("CAM-100")).not.toBeInTheDocument();
    expect(screen.queryByText("creator")).not.toBeInTheDocument();
    expect(screen.queryByText(/wallet|platform revenue|commission|budget/i)).not.toBeInTheDocument();
    expect(api.fetch.mock.calls.some(([path]) => path === "/api/customer/offers")).toBe(true);
  });
  it("renders a UGC Customer Offer without inventing Creator attribution", async () => {
    mockApi({ "/customer/offers": [ugcCustomerOffer] });
    mount(<CustomerOffers />);
    expect(await screen.findByText("Save on your next visit")).toBeVisible();
    expect(screen.getByText("5% off")).toBeVisible();
    expect(screen.getByText("Customer offer")).toBeVisible();
    expect(screen.queryByText(/By /)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Watch Promotion" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Get Directions" })).not.toBeInTheDocument();
  });
  it("only renders optional promotion links when the API provides safe URLs", async () => {
    mockApi({
      "/customer/offers": [{ ...offer, watchUrl: null, business: { ...offer.business, directionsUrl: null } }],
    });
    mount(<CustomerOffers />);
    await screen.findByRole("link", { name: "Get Offer" });
    expect(screen.queryByRole("link", { name: "Watch Promotion" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Get Directions" })).not.toBeInTheDocument();
  });
  it("keeps Discover on the same eligible Customer offer API and filters safely", async () => {
    mockApi({ "/customer/offers": [offer, ugcCustomerOffer] });
    mount(<CustomerOffers discover />);
    expect(await screen.findByRole("heading", { name: "Discover Promotions" })).toBeVisible();
    expect(screen.getAllByRole("article")).toHaveLength(2);
    await userEvent.click(screen.getByRole("button", { name: "Customer offers" }));
    expect(screen.getByText("Bella Beauty")).toBeVisible();
    expect(screen.queryByText("Abc Coffee")).not.toBeInTheDocument();
  });
  it("requests location only after an explicit tap and keeps offers when permission is denied", async () => {
    const original = Object.getOwnPropertyDescriptor(navigator, "geolocation");
    const getCurrentPosition = vi.fn((_success: unknown, failure: (error: unknown) => void) =>
      failure({ code: 1, PERMISSION_DENIED: 1 }));
    Object.defineProperty(navigator, "geolocation", {
      configurable: true,
      value: { getCurrentPosition },
    });
    sessionStorage.removeItem("weymela.customer-location");
    try {
      mockApi({ "/customer/offers": [offer] });
      mount(<CustomerOffers discover />);
      expect(await screen.findByRole("heading", { name: "Abc Coffee" })).toBeVisible();
      expect(getCurrentPosition).not.toHaveBeenCalled();
      await userEvent.click(screen.getByRole("button", { name: "Use Location" }));
      expect(getCurrentPosition).toHaveBeenCalledTimes(1);
      expect(await screen.findByText("Location access is off. You can still browse and filter promotions by location.")).toBeVisible();
      expect(screen.getByRole("heading", { name: "Abc Coffee" })).toBeVisible();
    } finally {
      if (original) Object.defineProperty(navigator, "geolocation", original);
      else Reflect.deleteProperty(navigator, "geolocation");
    }
  });
  it("renders authoritative View + Sale and UGC transactions without merging their benefits", async () => {
    mockApi({
      "/customer/transactions": [
        {
          source: "VIEW_AND_SALE_PROMOTION", offer: "Coffee stories", business: "Abc Coffee",
          creator: "Bella", purchaseAmount: { amount: 1000, currency: "ETB" },
          customerPaidAmount: null, cashbackEarned: { amount: 40, currency: "ETB" },
          discountReceived: null, purchasedAtUtc: "2026-09-22T10:00:00Z",
        },
        {
          source: "UGC_CUSTOMER_OFFER", offer: "Save on your next visit", business: "Bella Beauty",
          creator: null, purchaseAmount: { amount: 1000, currency: "ETB" },
          customerPaidAmount: { amount: 950, currency: "ETB" }, cashbackEarned: null,
          discountReceived: { amount: 50, currency: "ETB" }, purchasedAtUtc: "2026-09-21T10:00:00Z",
        },
      ],
    });
    const rendered = mount(<CustomerTransactions />);
    expect(await screen.findByText("Cashback earned")).toBeVisible();
    expect(screen.getByText("Promoted by Bella")).toBeVisible();
    expect(screen.getByText("Original purchase")).toBeVisible();
    expect(screen.getByText("Discount")).toBeVisible();
    const cards = rendered.container.querySelectorAll(".customer-transaction-card");
    expect(within(cards[1] as HTMLElement).getByText("Paid")).toBeVisible();
    expect(cards[0]).toHaveTextContent("Abc Coffee");
    expect(cards[1]).toHaveTextContent("Bella Beauty");
    expect(cards[1]).not.toHaveTextContent("Promoted by");
    expect(rendered.container).not.toHaveTextContent(/creatorId|businessId|commission|journal/i);
  });
  it("loads cashback balance, threshold and payout history from its safe projection", async () => {
    mockApi({
      "/customer/cashback": {
        availableCashback: { amount: 2800, currency: "ETB" },
        minimumCashOut: { amount: 4000, currency: "ETB" },
        remainingToCashOut: { amount: 1200, currency: "ETB" },
        eligible: false,
        status: "BelowThreshold",
        payoutHistory: [{ amount: { amount: 5000, currency: "ETB" }, status: "Paid", eligibleAtUtc: "2026-09-20T10:00:00Z", paidAtUtc: "2026-09-21T10:00:00Z" }],
      },
    });
    const rendered = mount(<CustomerCashback />);
    expect(await screen.findByText("Available Cashback")).toBeVisible();
    expect(screen.getByText("2,800")).toBeVisible();
    expect(screen.getByText("4,000")).toBeVisible();
    expect(screen.getByText("1,200 remaining to cash out.")).toBeVisible();
    expect(screen.getByText("Paid out")).toBeVisible();
    expect(rendered.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "70");
    expect(rendered.container).not.toHaveTextContent(/journal|customerId|reference/i);
  });
  it("shows empty transaction and payout histories", async () => {
    mockApi();
    const rendered = mount(<CustomerTransactions />);
    expect(await screen.findByText("No transactions yet")).toBeVisible();
    rendered.unmount();
    mount(<CustomerCashback />);
    expect(await screen.findByText("No payouts recorded yet")).toBeVisible();
  });
  it("renders the simple QR page with working Back destination", async () => {
    const api = mockApi();
    mount(
      <CustomerOfferQr />,
      "/customer/offers/offer",
      "/customer/offers/:id",
    );
    await screen.findByRole("heading", { name: "Abc Coffee" });
    expect(screen.getByRole("link", { name: "← Back" })).toHaveAttribute(
      "href",
      "/customer/offers",
    );
    expect(screen.queryByText(/selected creator|Creator promotion/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Get Offer" }));
    expect(
      await screen.findByRole("img", { name: "Offer QR for the cashier" }),
    ).toBeVisible();
    expect(screen.getByText("Show this QR to the cashier.")).toBeVisible();
    expect(api.writes[0]?.path).toBe("/customer/offers/offer/qr");
  });
  it("provides scanner and a clearly unavailable manual lookup shell", () => {
    mount(<Checkout />);
    expect(screen.getByRole("button", { name: "Scan QR" })).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Manual Lookup Unavailable" }),
    ).toBeDisabled();
  });
  it("resolves the QR before showing only the purchase amount entry", async () => {
    const api = mockApi();
    mount(<Checkout />);
    await userEvent.click(screen.getByText("Use a scanned code instead"));
    await userEvent.type(screen.getByLabelText("Scanned QR code"), "opaque");
    await userEvent.click(
      screen.getByRole("button", { name: "Resolve Offer" }),
    );
    expect(await screen.findByLabelText("Purchase Amount")).toBeVisible();
    expect(screen.queryByLabelText("Customer phone")).not.toBeInTheDocument();
    await userEvent.type(
      screen.getByLabelText("Purchase Amount"),
      "1000",
    );
    await userEvent.click(
      screen.getByRole("button", { name: "Confirm Purchase" }),
    );
    await waitFor(() =>
      expect(api.writes[1].body).toEqual({
        token: "opaque",
        purchaseAmount: 1000,
      }),
    );
  });
});
