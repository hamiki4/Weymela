import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import {
  BusinessDashboard,
  BusinessWallet,
  BusinessPricingPage,
  BusinessCampaigns,
} from "../src/features/business/BusinessPages";
import { CreateCampaign } from "../src/features/business/CreateCampaign";
import { BusinessLegalPage } from "../src/features/business/BusinessLegalPage";
import { BusinessUgcPage, CreateBusinessUgcPage } from "../src/features/business/UgcPages";
import { BusinessCampaignDetail } from "../src/features/business/CampaignDetail";
import { PromotionContentReviewQueue } from "../src/features/business/PromotionContentReviewQueue";
import {
  CreatorHowYouEarn,
  CreatorEarnings,
} from "../src/features/creator/CreatorPages";
import {
  CreatorDashboard,
  CreatorDiscover,
  CreatorOpportunity,
  CreatorPromotions,
  CreatorUgcOpportunity,
} from "../src/features/creator/CreatorExperience";
import { CreatorProfile } from "../src/features/creator/CreatorProfile";
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
import { Checkout, CheckoutTransactions } from "../src/features/commerce/Checkout";
import {
  campaign,
  detail,
  earnings,
  active,
  mockApi,
  offer,
  opportunity,
  ugcCustomerOffer,
  wallet,
  business,
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
      <LocationProbe />
      <Routes>
        <Route path={pattern} element={element} />
        <Route
          path="/business/campaigns/saved"
          element={<h1>Saved Campaign</h1>}
        />
        <Route path="/business/legal" element={<BusinessLegalPage />} />
      </Routes>
    </MemoryRouter>,
  );
}
function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location-search">{location.search}</output>;
}
beforeEach(() => {
  vi.restoreAllMocks();
  mockApi();
});

describe("Business workspace", () => {
  it("filters the authoritative Business purchase projection without changing amounts", async () => {
    mockApi({ "/business/transactions": [
      { id: "view-sale", offer: "Coffee stories", source: "VIEW_AND_SALE_PROMOTION", purchaseAmount: 1000,
        customerDiscount: 20, customerPays: 1000, businessCharge: 100, creatorEarning: 45,
        creator: "Mina", creatorNumber: 1001, cashier: "Cashier A", customerMasked: "••••1234",
        status: "Recorded", createdAtUtc: "2026-09-29T12:00:00Z" },
      { id: "ugc-sale", offer: "Product story", source: "UGC_CUSTOMER_OFFER", purchaseAmount: 800,
        customerDiscount: 40, customerPays: 760, businessCharge: 64, creatorEarning: null,
        creator: "Mina", creatorNumber: 1001, cashier: "Cashier A", customerMasked: "••••5678",
        status: "Completed", createdAtUtc: "2026-09-29T13:00:00Z" },
    ] });
    const rendered = mount(<CheckoutTransactions />);
    await screen.findByText("Promotion: Coffee stories");
    expect(rendered.container.querySelectorAll(".business-transaction-card")).toHaveLength(2);
    expect(screen.getByText("Customer Cashback: 20 ETB")).toBeVisible();
    expect(screen.queryByText(/Creator earning|Creator:|Creator ID|Business charge/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "UGC + Sale" }));
    expect(rendered.container.querySelectorAll(".business-transaction-card")).toHaveLength(1);
    expect(screen.getByText("Promotion: Product story")).toBeVisible();
    expect(screen.queryByText("Promotion: Coffee stories")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "All" }));
    expect(rendered.container.querySelectorAll(".business-transaction-card")).toHaveLength(2);
  });
  it("keeps Home funds focused while Wallet retains reserved balance", async () => {
    mount(<BusinessDashboard />);
    expect(
      await screen.findByRole("heading", { name: "Home" }),
    ).toHaveClass("sr-only");
    expect(screen.getByText("10,000")).toBeVisible();
    expect(screen.getByText("4,000")).toBeVisible();
    expect(screen.getByText("Available")).toBeVisible();
    expect(screen.getByText("Total")).toBeVisible();
    expect(screen.queryByText("Reserved")).not.toBeInTheDocument();
    expect(
      screen.getByRole("link", {
        name: "Pricing",
      }),
    ).toBeVisible();
  });
  it("keeps Wallet history off Home while Wallet remains reachable", async () => {
    mockApi({ "/business/home": {
      business,
      wallet: { ...wallet, history: [{ id: "ledger", label: "Customer Offer funded", amount: 120, atUtc: "2026-09-29T12:00:00Z", reference: "ledger-ref" }] },
      activeCampaigns: 1,
      creatorRequests: 1,
      confirmedSales: 1,
    } });
    mount(<BusinessDashboard />);
    expect(await screen.findByRole("heading", { name: "Home" })).toHaveClass("sr-only");
    expect(screen.getByRole("link", { name: "Available funds, view wallet" })).toHaveAttribute("href", "/business/wallet");
    expect(screen.queryByRole("link", { name: "Wallet history" })).not.toBeInTheDocument();
    expect(screen.queryByText("Customer Offer funded")).not.toBeInTheDocument();
  });
  it("shows Total, Available, Reserved and pending deposits with clear history tabs", async () => {
    mockApi({
      "/business/deposit-requests": [{ id: "deposit", amount: 500, status: "Pending", submittedAtUtc: "2026-09-29T12:00:00Z", destinationName: "CBE", destinationAccount: "1000000000" }],
      "/business/wallet": { ...wallet, history: [{ id: "entry", label: "Deposit approved", amount: 250, atUtc: "2026-09-28T12:00:00Z", reference: "ref" }] },
    });
    mount(<BusinessWallet />);
    expect(await screen.findByText("Pending Deposits")).toBeVisible();
    expect(screen.getAllByText("500 ETB")).toHaveLength(2);
    expect(screen.getByText(/CBE · 1000000000/)).toBeVisible();
    await userEvent.click(screen.getByRole("tab", { name: "Wallet Activity" }));
    expect(await screen.findAllByText("Deposit approved")).toHaveLength(2);
  });
  it("routes Business operations to their destination and keeps one Create Promotion action", async () => {
    mount(<BusinessDashboard />);
    const active = await screen.findByRole("link", { name: /Active Promotions/ });
    expect(active).toHaveAttribute("href", "/business/campaigns?filter=Active");
    expect(screen.getByRole("link", { name: /Creator Requests/ })).toHaveAttribute("href", "/business/requests");
    expect(screen.queryByRole("link", { name: /Open UGC/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: /Create Promotion/ })).toHaveLength(1);
  });
  it("does not repeat the Create Promotion action on an empty Promotions page", async () => {
    mockApi({ "/business/campaigns": [] });
    mount(<BusinessCampaigns />);
    expect(await screen.findByText("No Promotions yet.")).toBeVisible();
    expect(screen.getAllByRole("link", { name: /Create Promotion/ })).toHaveLength(1);
  });
  it("keeps the legacy UGC workspace represented as Promotions", async () => {
    mockApi({ "/business/ugc": [] });
    mount(<BusinessUgcPage />);
    expect(await screen.findByText("No Promotions yet.")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Promotions", level: 2 })).toBeVisible();
    expect(screen.getByRole("link", { name: "Create Promotion" })).toHaveAttribute("href", "/business/campaigns/new");
    expect(screen.queryByLabelText("Promotion title")).not.toBeInTheDocument();
  });
  it("records any positive deposit with the current wallet version", async () => {
    const api = mockApi();
    mount(<BusinessWallet />);
    expect(screen.queryByText("Open Add Funds")).not.toBeInTheDocument();
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
    expect(screen.queryByText("Open Add Funds")).not.toBeInTheDocument();
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
    expect(screen.getAllByRole("article")).toHaveLength(4);
    expect(screen.getByText("10% per verified sale")).toBeVisible();
    expect(
      screen.queryByText(/Creator:|Customer Cashback|Platform Keeps/),
    ).not.toBeInTheDocument();
  });
  it("shows one Create Promotion gateway with all four Promotion types", async () => {
    mount(<CreateCampaign />, "/business/campaigns/new");

    expect(await screen.findByText("What do you want to achieve?")).toBeVisible();

    const choices = screen.getAllByRole("link");

    expect(choices.find(link => link.getAttribute("href") === "/business/campaigns/new?type=views")).toBeTruthy();
    expect(choices.find(link => link.getAttribute("href") === "/business/campaigns/new?type=views-sales")).toBeTruthy();
    expect(choices.find(link => link.getAttribute("href") === "/business/campaigns/new?type=ugc")).toBeTruthy();
    expect(choices.find(link => link.getAttribute("href") === "/business/campaigns/new?type=ugc-sales")).toBeTruthy();
  });

  it("opens the selected Views Promotion form without asking for the type again", async () => {
    mount(<CreateCampaign />, "/business/campaigns/new?type=views");

    expect(await screen.findByLabelText("Promotion title")).toBeVisible();
    expect(screen.getByText("View Only", { selector: "strong" })).toBeVisible();
    expect(screen.queryByRole("combobox", { name: "Promotion type" })).not.toBeInTheDocument();
  });

  it("opens the selected View + Sale Promotion form", async () => {
    mount(<CreateCampaign />, "/business/campaigns/new?type=views-sales");

    expect(await screen.findByLabelText("Promotion title")).toBeVisible();
    expect(screen.getByText("View + Sale", { selector: "strong" })).toBeVisible();
  });

  it("opens UGC from the unified Promotion choice without a Customer sale", async () => {
    mount(<CreateBusinessUgcPage />, "/business/ugc/new?type=ugc");

    expect(await screen.findByText("UGC", { selector: ".promotion-selected-type strong" })).toBeVisible();
    expect(screen.queryByLabelText("Customer cashback %")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Customer cashback budget")).not.toBeInTheDocument();
  });

  it("opens UGC + Sale with Customer sale fields already enabled", async () => {
    mount(<CreateBusinessUgcPage />, "/business/ugc/new?type=ugc-sales");

    expect(await screen.findByText("UGC + Sale", { selector: ".promotion-selected-type strong" })).toBeVisible();
    expect(screen.getByLabelText("Customer cashback %")).toBeVisible();
    expect(screen.getByLabelText("Customer cashback budget")).toBeVisible();
  });

  it("saves a private Promotion Draft without accepting platform rates", async () => {
    const api = mockApi();
    mount(<CreateCampaign />, "/business/campaigns/new?type=views");
    await screen.findByLabelText("Promotion title");
    await userEvent.type(
      screen.getByLabelText("Promotion title"),
      "Local stories",
    );
    expect(screen.getByLabelText("Description")).toBeInTheDocument();
    expect(screen.queryByLabelText("Start date")).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Application closes"), "2027-09-19");
    await userEvent.type(screen.getByLabelText("Content due"), "2027-09-20");
    await userEvent.click(screen.getByRole("button", { name: "Add TikTok Creator slot" }));
    await userEvent.type(
      screen.getByLabelText("Promotion budget"),
      "1000",
    );
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(api.writes.find(write => write.path === "/business/promotions")?.body).toMatchObject({ campaignBudget: 1000, description: "", platforms: [{ platform: "TikTok", capacity: 1 }] }));
    expect(api.writes.find(write => write.path === "/business/promotions")?.body).not.toHaveProperty("creatorCommissionPercent");
  });
  it("does not block Promotion creation on legacy Business agreements", async () => {
    mockApi({ "/legal/current": [
      { id: "new-business-agreement", type: "BusinessAgreement", version: "2", contentHash: "new", accepted: false },
      { id: "anti-circumvention", type: "AntiCircumventionAgreement", version: "1", contentHash: "fixture", accepted: true },
    ] });
    mount(<CreateCampaign />, "/business/campaigns/new?type=views");
    expect(await screen.findByLabelText("Promotion title")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Before you continue" })).not.toBeInTheDocument();
  });
  it("does not block UGC creation on legacy Business agreements", async () => {
    mockApi({ "/legal/current": [
      { id: "new-business-agreement", type: "BusinessAgreement", version: "2", contentHash: "new", accepted: false },
      { id: "anti-circumvention", type: "AntiCircumventionAgreement", version: "1", contentHash: "fixture", accepted: true },
    ] });
    mount(<CreateBusinessUgcPage />, "/business/ugc/new");
    expect(await screen.findByLabelText("Promotion title")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Before you continue" })).not.toBeInTheDocument();
  });
  it("keeps UGC creation focused on the Promotion without funding internals", async () => {
    mockApi({ "/business/wallet": { ...wallet, totalBalance: 1000, available: 1000, reserved: 0 } });
    mount(<CreateBusinessUgcPage />);
    await userEvent.type(await screen.findByLabelText("Creator payment (ETB)"), "500");
    await userEvent.clear(screen.getByLabelText("Creators needed"));
    await userEvent.type(screen.getByLabelText("Creators needed"), "3");
    expect(screen.queryByText(/Funding Summary|Total funding|Customer cashback funding|Need/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox", { name: "Add Customer cashback sale" }));
    await userEvent.type(screen.getByLabelText("Customer cashback %"), "5");
    await userEvent.type(screen.getByLabelText("Customer cashback budget"), "200");
    expect(screen.queryByText(/10,000|Platform fee|Reserved/)).not.toBeInTheDocument();
  });
  it.each([
    ["Product provided by Business", true, false],
    ["Creator purchases product", false, true],
  ])("requires and submits an explicit %s arrangement", async (label, productProvided, creatorMustPurchase) => {
    const { writes } = mockApi();
    mount(<CreateBusinessUgcPage />);
    expect(await screen.findByText("Product arrangement")).toBeVisible();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    await userEvent.type(screen.getByLabelText("Promotion title"), "Product story");
    await userEvent.type(screen.getByLabelText("Instructions"), "Make a short video");
    await userEvent.type(screen.getByLabelText("Application closes"), "2027-11-30T10:00");
    await userEvent.type(screen.getByLabelText("Content due"), "2027-12-01T10:00");
    await userEvent.type(screen.getByLabelText("Creator payment (ETB)"), "500");
    await userEvent.click(screen.getByRole("radio", { name: new RegExp(label) }));
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(writes.find((write) => write.path === "/business/ugc")?.body).toMatchObject({ productProvided, creatorMustPurchase, creatorPayment: 500, customerOfferEnabled: false }));
  });
  it("shows the arrangement in Business management and before a Creator joins", async () => {
    const card = { id: "ugc-1", businessId: "biz", business: "Abc Coffee", title: "Product story", slogan: null,
      contentType: "Video", status: "Open", creatorPayment: 500, creatorsNeeded: 1, approvedCreators: 0,
      requiredFunding: 550, reservedFunding: 550, usedFunding: 0, dueDateUtc: "2027-12-01T10:00:00Z",
      location: null, platformRequirements: [], requestStatus: null, version: 1,
      productProvided: false, creatorMustPurchase: true };
    mockApi({ "/business/ugc": [card], "/creator/ugc": [card] });
    const businessView = mount(<BusinessUgcPage />);
    expect(await screen.findByRole("link", { name: "Manage" })).toHaveAttribute("href", "/business/ugc/ugc-1");
    businessView.unmount();
    mount(<CreatorDiscover />, "/creator/discover");
    expect(await screen.findByText("Product story")).toBeVisible();
    expect(screen.getByText("Creator purchases product")).toBeVisible();
    expect(screen.getByRole("button", { name: "Request to Join" })).toBeVisible();
  });
  it.each([0, 9000, 15000])("allows a private Draft regardless of current available funds (%i)", async (available) => {
    mockApi({ "/business/wallet": { ...wallet, totalBalance: available, available, reserved: 0 } });
    mount(<CreateCampaign />, "/business/campaigns/new?type=views");
    await userEvent.type(await screen.findByLabelText("Promotion title"), "Local stories");
    await userEvent.type(screen.getByLabelText("Application closes"), "2027-09-19");
    await userEvent.type(screen.getByLabelText("Content due"), "2027-09-20");
    await userEvent.click(screen.getByRole("button", { name: "Add TikTok Creator slot" }));
    await userEvent.type(await screen.findByLabelText("Promotion budget"), "10000");
    expect(screen.queryByText(/Available funds cannot cover this Promotion/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });
  it("omits the slogan and sends social capacities with the Promotion", async () => {
    const api = mockApi();
    mount(<CreateCampaign />, "/business/campaigns/new?type=views");
    await userEvent.type(await screen.findByLabelText("Promotion title"), "Weekend Special");
    expect(screen.queryByLabelText("Promotion slogan (optional)")).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Application closes"), "2027-09-19");
    await userEvent.type(screen.getByLabelText("Content due"), "2027-09-20");
    await userEvent.click(screen.getByRole("button", { name: "Add TikTok Creator slot" }));
    await userEvent.click(screen.getByRole("button", { name: "Add TikTok Creator slot" }));
    await userEvent.type(screen.getByLabelText("Promotion budget"), "1000");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(api.writes.find(write => write.path === "/business/promotions")?.body).toMatchObject({ slogan: null, platforms: [{ platform: "TikTok", capacity: 2 }] }));
  });
  it("shows optional slogan and approved platform occupancy in Business management", async () => {
    mockApi({ "/business/campaigns/campaign": { ...detail, campaign: { ...campaign, slogan: "Weekend Special", platforms: [{ platform: "TikTok", approved: 1, capacity: 2, available: 1 }] } } });
    const { container } = mount(<BusinessCampaignDetail />, "/business/campaigns/campaign", "/business/campaigns/:id");
    expect(await screen.findByText("Weekend Special")).toBeVisible();
    expect(screen.getByText("Creators and platforms")).toBeVisible();
    expect(screen.getByText("TikTok · 2 Creators")).toBeVisible();
    expect(container.querySelector(".business-platform-summary")).not.toHaveTextContent(/pending requests/);
  });
  it("shows Publish and Edit as the next actions for a private Draft", async () => {
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
    expect(await screen.findByRole("button", { name: "Publish" })).toBeVisible();
    expect(screen.getByText(/Publish when the details and confirmed funding are ready/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Edit" })).toBeVisible();
    expect(api.writes).toHaveLength(0);
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
    await screen.findByRole("heading", { name: "Promotions", level: 2 });
    expect(screen.queryByRole("heading", { name: "UGC Promotions", level: 2 })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Promotions", level: 1 })).toBeVisible();
    expect(screen.getByRole("link", { name: "Manage" })).toBeVisible();
    expect(
      screen.queryByRole("button", { name: /End Campaign|End Promotion/ }),
    ).not.toBeInTheDocument();
  });
  it("lets the owning Business request changes on a submitted Promotion revision", async () => {
    const api = mockApi({
      "/business/promotion-content-submissions": [{
        submissionId: "submission-safe-key",
        promotionId: "campaign",
        creator: "Mina Creator",
        promotion: "Seasonal stories",
        provider: "TikTok",
        contentReference: "https://www.tiktok.com/t/ZMabcdef",
        watchUrl: "https://www.tiktok.com/t/ZMabcdef",
        creatorProfileUrl: "https://www.tiktok.com/@mina",
        revisionNumber: 1,
        submittedAtUtc: "2026-09-22T12:00:00Z",
        reviewStatus: "UnderReview",
        feedback: null,
        reviewedAtUtc: null,
      }],
    });
    mount(<PromotionContentReviewQueue />);
    expect(await screen.findByText("Mina Creator · Seasonal stories")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Revision 1" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Creator Profile" })).toHaveAttribute("href", "https://www.tiktok.com/@mina");
    expect(screen.getByRole("link", { name: "Play Sample Video" })).toHaveAttribute("href", "https://www.tiktok.com/t/ZMabcdef");
    expect(screen.queryByText("https://www.tiktok.com/t/ZMabcdef")).not.toBeInTheDocument();
    expect(screen.queryByText(/AllocationId|BusinessId|CreatorId|Wallet|Commission/)).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Feedback (required for changes requested)"), "Please adjust the opening shot.");
    await userEvent.click(screen.getByRole("button", { name: "Request Changes" }));
    await waitFor(() => expect(api.writes[0]).toMatchObject({
      path: "/business/promotion-content-submissions/submission-safe-key/review",
      body: { action: "requestchanges", feedback: "Please adjust the opening shot." },
    }));
  });

  it("does not fabricate inaccessible links or allow approval without a validated submission link", async () => {
    mockApi({ "/business/promotion-content-submissions": [{
      submissionId: "missing-link", promotionId: "campaign", creator: "Mina Creator", promotion: "Seasonal stories",
      provider: "TikTok", contentReference: "not-a-url", revisionNumber: 2,
      submittedAtUtc: "2026-09-22T12:00:00Z", reviewStatus: "UnderReview", feedback: null, reviewedAtUtc: null,
    }] });
    mount(<PromotionContentReviewQueue />);
    expect(await screen.findByText("Review unavailable: this TikTok link is invalid or unsupported.")).toBeVisible();
    expect(screen.getByRole("button", { name: "Play Sample Video" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Request Changes" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeEnabled();
  });

  it("keeps explicit Approve and Reject decisions available for valid submitted links", async () => {
    const api = mockApi({ "/business/promotion-content-submissions": ["approve", "reject"].map((decision) => ({
      submissionId: `${decision}-submission`, promotionId: "campaign", creator: "Mina Creator", promotion: "Seasonal stories",
      provider: "TikTok", contentReference: "https://www.tiktok.com/t/ZMabcdef",
      watchUrl: "https://www.tiktok.com/t/ZMabcdef", creatorProfileUrl: "https://www.tiktok.com/@mina",
      revisionNumber: 1, submittedAtUtc: "2026-09-22T12:00:00Z", reviewStatus: "UnderReview", feedback: null, reviewedAtUtc: null,
    })) });
    mount(<PromotionContentReviewQueue />);
    await screen.findByText("Mina Creator · Seasonal stories");
    await userEvent.click(screen.getAllByRole("button", { name: "Approve" })[0]);
    await waitFor(() => expect(api.writes).toContainEqual(expect.objectContaining({ body: { action: "approve", feedback: null } })));
    await userEvent.click(screen.getAllByRole("button", { name: "Reject" })[1]);
    await waitFor(() => expect(api.writes).toContainEqual(expect.objectContaining({ body: { action: "reject", feedback: null } })));
  });
});

describe("Creator workspace", () => {
  const acceptedCreatorLegal = [
    { id: "creator-agreement", type: "CreatorAgreement", version: "1", contentHash: "creator-fixture", accepted: true },
    { id: "anti-circumvention", type: "AntiCircumventionAgreement", version: "1", contentHash: "fixture", accepted: true },
  ];
  function mockCreatorApi(overrides: Record<string, unknown> = {}) {
    return mockApi({ "/legal/current": acceptedCreatorLegal, ...overrides });
  }
  beforeEach(() => { mockCreatorApi(); });

  it("shows only server recorded social profiles and supported platform statuses", async () => {
    mockCreatorApi({ "/creator/social-accounts": [{ id: "social-1", platform: "TikTok", profileUrl: "https://www.tiktok.com/@bella",
      verificationStatus: "Verified" }, { id: "social-2", platform: "YouTube", profileUrl: "javascript:alert(1)", verificationStatus: "Unverified" }] });
    mount(<CreatorProfile />);
    expect(await screen.findByRole("heading", { name: "Social Profiles" })).toBeVisible();
    expect(screen.queryByText("Verified")).not.toBeInTheDocument();
    expect(screen.getByText("Profile added")).toBeVisible();
    expect(screen.getAllByText("Not added")).toHaveLength(2);
    expect(screen.getByText("@bella")).toBeVisible();
    expect(screen.queryByText("social-1")).not.toBeInTheDocument();
    expect(screen.queryByText("javascript:alert(1)")).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "View TikTok profile" })).toHaveLength(1);
  });
  it("uses Pending, Active and Completed for My Promotions", async () => {
    mount(<CreatorPromotions />);

    expect(await screen.findByRole("heading", { name: "My Promotions" })).toBeVisible();

    const tabs = screen.getByRole("tablist", { name: "Filter Promotions" });
    expect(
      within(tabs).getAllByRole("tab").map((tab) => tab.textContent),
    ).toEqual(["Pending", "Active", "Completed"]);

    expect(within(tabs).queryByRole("tab", { name: "Requests" })).not.toBeInTheDocument();
    expect(within(tabs).queryByRole("tab", { name: "History" })).not.toBeInTheDocument();
    await userEvent.click(within(tabs).getByRole("tab", { name: "Pending" }));
    expect(screen.getByTestId("location-search")).toHaveTextContent("?filter=Pending");
    await userEvent.click(within(tabs).getByRole("tab", { name: "Active" }));
    expect(screen.getByTestId("location-search")).toHaveTextContent("?filter=Active");
    await userEvent.click(within(tabs).getByRole("tab", { name: "Completed" }));
    expect(screen.getByTestId("location-search")).toHaveTextContent("?filter=Completed");
  });

  it("orients Creator Home with authoritative summaries and clear destinations", async () => {
    mockCreatorApi({
      "/creator/campaigns": [{ ...active, remainingDays: 12 }],
    });

    mount(<CreatorDashboard />);

    expect(await screen.findByRole("heading", { name: "Home" })).toHaveClass("sr-only");
    expect(screen.queryByText("Bella")).not.toBeInTheDocument();

    expect(screen.getByRole("link", { name: "Discover Promotions" })).toHaveAttribute(
      "href",
      "/creator/discover",
    );

    const pending = screen.getByRole("link", { name: /Pending Requests, 1, Waiting for Business/ });
    expect(pending).toHaveAttribute("href", "/creator/promotions?filter=Pending");
    expect(within(pending).getByText("Pending Requests")).toBeVisible();
    expect(within(pending).getByText("Waiting for Business")).toBeVisible();

    const activeWork = screen.getByRole("link", { name: /Live Promotions, 1, Continue your Promotion/ });
    expect(activeWork).toHaveAttribute("href", "/creator/promotions?filter=Active");
    expect(within(activeWork).getByText("Live Promotions")).toBeVisible();
    expect(within(activeWork).getByText("Continue your Promotion")).toBeVisible();

    const earningsLink = screen.getByRole("link", { name: /Available Earnings, 5,400 ETB/ });
    expect(earningsLink).toHaveAttribute("href", "/creator/earnings");
    expect(within(earningsLink).getByText("Available Earnings")).toBeVisible();
    expect(within(earningsLink).getByText("5,400 ETB")).toBeVisible();

    expect(screen.getByRole("link", { name: "My Promotions" })).toHaveAttribute(
      "href",
      "/creator/promotions",
    );

    const preview = screen.getByRole("link", { name: "View Promotion Coffee stories" });
    expect(within(preview).getByText("Continue working")).toBeVisible();
    expect(within(preview).getByText("Active")).toBeVisible();
    expect(within(preview).getByText("12 days left")).toBeVisible();

    expect(screen.getByRole("heading", { name: "Promotions for you" })).toBeVisible();
    expect(screen.getByRole("link", { name: "See all" })).toHaveAttribute("href", "/creator/discover");
    const discoveryPreview = screen.getByText("Promotions for you").closest("section")!;
    expect(within(discoveryPreview).getByText("Coffee stories")).toBeVisible();
    expect(within(discoveryPreview).getByRole("link", { name: "View details" })).toHaveAttribute(
      "href",
      "/creator/discover/campaign",
    );
    expect(screen.queryByText("Recent earnings")).not.toBeInTheDocument();
    expect(screen.queryByText("Request to Join")).not.toBeInTheDocument();
    expect(screen.queryByText("verified views")).not.toBeInTheDocument();
    expect(screen.queryByText("Payout paid")).not.toBeInTheDocument();
  });

  it("does not reserve Home space for absent active work or render earning history", async () => {
    mockCreatorApi({ "/creator/campaigns": [], "/creator/discover": [], "/creator/ugc": [] });
    mount(<CreatorDashboard />);

    expect(await screen.findByRole("heading", { name: "Home" })).toHaveClass("sr-only");
    expect(screen.queryByText("Continue working")).not.toBeInTheDocument();
    expect(screen.queryByText("Coffee stories")).not.toBeInTheDocument();
    expect(screen.getByText("No new promotions right now.")).toBeVisible();
    expect(screen.queryByText("View Earnings")).not.toBeInTheDocument();
    expect(screen.queryByText("Recent earnings")).not.toBeInTheDocument();
    expect(screen.queryByText("Payout paid")).not.toBeInTheDocument();
  });
  it("opens UGC detail through the existing Creator detail projection", async () => {
    const card = { id: "ugc-detail", businessId: "biz", business: "Abc Coffee", title: "Coffee video", slogan: null,
      contentType: "Video", status: "Open", creatorPayment: 4750, creatorsNeeded: 1, approvedCreators: 0,
      dueDateUtc: "2027-12-01T10:00:00Z", location: "Addis Ababa", platformRequirements: [{ platform: "TikTok", format: "Video", minimumAudience: 0 }],
      platformCapacities: [{ platform: "TikTok", capacity: 1, approved: 0, available: 1 }], requestStatus: null, version: 1,
      productProvided: true, creatorMustPurchase: false };
    mockCreatorApi({ "/creator/ugc/ugc-detail": { opportunity: card, instructions: "Make a short video", resources: [], productProvided: true,
      creatorMustPurchase: false, usageRights: null, currentRevision: 1, requests: [], assignments: [], revisions: [] } });
    mount(<CreatorUgcOpportunity />, "/creator/ugc/ugc-detail", "/creator/ugc/:id");
    expect(await screen.findByRole("heading", { name: "Coffee video" })).toBeVisible();
    expect(screen.getByText("Make a short video")).toBeVisible();
    expect(screen.getByRole("button", { name: "Request to Join" })).toBeVisible();
  });
  it("discovers all Promotion types in one compact feed", async () => {
    mockCreatorApi({ "/creator/ugc": [] });
    mount(<CreatorDiscover />);
    expect(await screen.findByRole("heading", { name: "Discover" })).toBeVisible();
    expect(
      screen.queryByRole("tablist", { name: "Opportunity type" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Opportunities" })).toBeVisible();
    expect(await screen.findByText(/Abc Coffee/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Request to Join" })).toHaveAttribute("href", "/creator/discover/campaign");
    expect(screen.queryByText(/Business Wallet|Reserved Funds|Campaign Budget/)).not.toBeInTheDocument();
  });
  it("collapses an absent slogan and hides slot occupancy details", async () => {
    const { container } = mount(<CreatorDiscover />);
    await screen.findByRole("link", { name: "Request to Join" });
    expect(container.querySelector(".creator-opportunity-slogan")).toBeNull();
    expect(container.querySelector('[aria-label*="filled"]')).toBeNull();
  });
  it("shows optional slogan and keeps pending requests out of platform occupancy", async () => {
    mockCreatorApi({ "/creator/discover": [{ ...opportunity, slogan: "Weekend Special", platforms: [{ platform: "TikTok", approved: 1, capacity: 2, available: 1 }], approvedCreators: 1, creatorCapacity: 2, requestStatus: "Pending" }] });
    mount(<CreatorDiscover />);
    expect(await screen.findByText("Coffee stories")).toBeVisible();
    expect(screen.queryByText("Weekend Special")).not.toBeInTheDocument();
    expect(screen.queryByText(/Creators 1\/2/)).not.toBeInTheDocument();
    expect(screen.getByText("TikTok")).toBeVisible();
    expect(screen.getByRole("link", { name: "View Promotion" })).toBeVisible();
  });
  it("shows a full Promotion without offering an impossible request", async () => {
    mockCreatorApi({ "/creator/discover": [{ ...opportunity, platforms: [{ platform: "TikTok", approved: 2, capacity: 2, available: 0 }], requestStatus: null }] });
    mount(<CreatorDiscover />);
    expect(await screen.findByText("No spots available")).toBeVisible();
    expect(screen.getByText("TikTok")).toBeVisible();
    expect(screen.queryByText(/2\/2/)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Request to Join" })).not.toBeInTheDocument();
  });
  it("shows the UGC customer offer and net content earning without internal funding", async () => {
    const card = { id: "ugc-sale", businessId: "biz", business: "Abc Coffee", title: "Coffee video", slogan: null,
      contentType: "Video", status: "Open", creatorPayment: 4750, creatorsNeeded: 1, approvedCreators: 0,
      dueDateUtc: "2027-12-01T10:00:00Z", location: "Addis Ababa", platformRequirements: [],
      requestStatus: null, version: 1, customerOfferEnabled: true, customerDiscountPercent: 3,
      customerOfferFundedAllocation: 10000, platformFee: 250, productProvided: true, creatorMustPurchase: false };
    mockCreatorApi({ "/creator/ugc": [card] });
    mount(<CreatorDiscover />, "/creator/discover");
    expect(await screen.findByText("UGC + Sale")).toBeVisible();
    expect(screen.getByText("Creator payment 4,750 ETB")).toBeVisible();
    expect(screen.getByText("Customer earns 3% cashback")).toBeVisible();
    expect(screen.getByText("Product provided by Business")).toBeVisible();
    expect(screen.queryByText(/10,000|Platform fee|250/)).not.toBeInTheDocument();
  });
  it("requires a specific available verified profile for posted UGC and sends its binding", async () => {
    const card = { id: "posted-ugc", businessId: "biz", business: "Abc Coffee", title: "Posted story", slogan: null,
      contentType: "Video", status: "Open", creatorPayment: 450, creatorsNeeded: 2, approvedCreators: 1,
      dueDateUtc: "2027-12-01T10:00:00Z", location: null, platformRequirements: [], requestStatus: null,
      version: 2, productProvided: true, creatorMustPurchase: false,
      platformCapacities: [{ platform: "TikTok", capacity: 1, approved: 1, available: 0 },
        { platform: "Instagram", capacity: 1, approved: 0, available: 1 }],
      eligibleSocialProfiles: [{ id: "verified-tiktok", platform: "TikTok", profileUrl: "https://tiktok.com/@bella" },
        { id: "verified-instagram", platform: "Instagram", profileUrl: "https://instagram.com/bella" }] };
    const api = mockCreatorApi({ "/creator/ugc": [card] });
    mount(<CreatorDiscover />, "/creator/discover");
    expect(await screen.findByText("Posted story")).toBeVisible();
    expect(screen.queryByText(/1\/1/)).not.toBeInTheDocument();
    expect(screen.queryByText(/0\/1/)).not.toBeInTheDocument();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.getByText("Verified Instagram profile")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Request to Join" }));
    await waitFor(() => expect(api.writes[0]?.path).toBe("/creator/ugc/posted-ugc/request?selectedPlatform=Instagram&verifiedSocialProfileId=verified-instagram"));
  });
  it("disables joining when every posted UGC platform is full", async () => {
    mockCreatorApi({ "/creator/ugc": [{ id: "full-ugc", business: "Abc Coffee", title: "Full story", slogan: null,
      contentType: "Video", status: "InProgress", creatorPayment: 450, creatorsNeeded: 1, approvedCreators: 1,
      dueDateUtc: "2027-12-01T10:00:00Z", location: null, requestStatus: null, version: 2,
      productProvided: true, creatorMustPurchase: false,
      platformCapacities: [{ platform: "TikTok", capacity: 1, approved: 1, available: 0 }],
      eligibleSocialProfiles: [{ id: "verified-tiktok", platform: "TikTok", profileUrl: "https://tiktok.com/@bella" }] }] });
    mount(<CreatorDiscover />, "/creator/discover");
    expect(await screen.findByText("All Creator spots filled")).toBeVisible();
    expect(screen.getByRole("button", { name: "Request to Join" })).toBeDisabled();
  });
  it("joins delivery-only UGC without inventing a social profile", async () => {
    const api = mockCreatorApi({ "/creator/ugc": [{ id: "delivery-ugc", business: "Abc Coffee", title: "Delivery story", slogan: null,
      contentType: "Video", status: "Open", creatorPayment: 450, creatorsNeeded: 1, approvedCreators: 0,
      dueDateUtc: "2027-12-01T10:00:00Z", location: null, requestStatus: null, version: 1,
      productProvided: true, creatorMustPurchase: false, platformCapacities: [], eligibleSocialProfiles: [] }] });
    mount(<CreatorDiscover />, "/creator/discover");
    expect(await screen.findByText("Delivery story")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "Request to Join" }));
    await waitFor(() => expect(api.writes[0]?.path).toBe("/creator/ugc/delivery-ugc/request"));
  });
  it("shows approved occupancy and makes a full platform unavailable", async () => {
    mockCreatorApi({ "/creator/discover/campaign": { ...opportunity, platforms: [{ platform: "TikTok", approved: 2, capacity: 2, available: 0 }], eligibleSocialProfiles: [] } });
    mount(<CreatorOpportunity />, "/creator/discover/campaign", "/creator/discover/:id");
    expect(await screen.findByText("Full")).toBeVisible();
    expect(screen.getByRole("button", { name: "Submit Request" })).toBeDisabled();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.queryByText("Category")).not.toBeInTheDocument();
    expect(screen.queryByText("Verified followers")).not.toBeInTheDocument();
  });
  it("requests the specific Promotion and selected available social profile", async () => {
    const api = mockCreatorApi({ "/creator/discover/campaign": { ...opportunity,
      platforms: [{ platform: "TikTok", approved: 1, capacity: 2, available: 1 }, { platform: "Instagram", approved: 1, capacity: 1, available: 0 }],
      eligibleSocialProfiles: [{ id: "social-tiktok", platform: "TikTok", profileUrl: "https://tiktok.com/@bella", selfReportedAudience: 5000, verificationStatus: "Verified", verifiedAudience: 5000 }],
    } });
    mount(<CreatorOpportunity />, "/creator/discover/campaign", "/creator/discover/:id");
    const selector = await screen.findByRole("radiogroup", { name: "Choose platform" });
    expect(selector).toHaveTextContent("Available");
    expect(selector).toHaveTextContent("InstagramFull");
    const submit = screen.getByRole("button", { name: "Submit Request" });
    expect(submit).toBeDisabled();
    await userEvent.click(screen.getByRole("radio"));
    await userEvent.click(submit);
    await waitFor(() => expect(api.writes[0]).toMatchObject({ path: "/creator/promotions/campaign/request", body: { platform: "TikTok", creatorSocialProfileId: "social-tiktok" } }));
  });
  it("has a designed empty discovery state", async () => {
    mockCreatorApi({ "/creator/discover": [], "/creator/ugc": [] });
    mount(<CreatorDiscover />);
    expect(await screen.findByText("No available opportunities")).toBeVisible();
    expect(screen.queryByRole("tablist", { name: "Opportunity type" })).not.toBeInTheDocument();

  });
  it("sends a join message without negotiating budget or rates", async () => {
    const api = mockCreatorApi();
    mount(
      <CreatorOpportunity />,
      "/creator/discover/campaign",
      "/creator/discover/:id",
    );
    await userEvent.type(
      await screen.findByLabelText("Short message"),
      "My idea",
    );
    await userEvent.click(screen.getByRole("button", { name: "Submit Request" }));
    await waitFor(() =>
      expect(api.writes[0].body).toEqual({
        message: "My idea",
        contentConcept: null,
      }),
    );
    expect(api.writes[0].path).toBe("/creator/promotions/campaign/request");
  });
  it("shows own Promotion progress without Business budget or wallet details", async () => {
    mockCreatorApi({ "/creator/campaigns": [{ ...active, remainingDays: 20 }] });
    mount(<CreatorPromotions />);
    expect(await screen.findByRole("heading", { name: "My Promotions" })).toBeVisible();
    expect(screen.getByText("20 days left")).toBeVisible();
    expect(screen.getByText(/3,000 verified views/)).toBeVisible();
    expect(screen.queryByText(/Your Budget|Budget Remaining|Campaign Budget/)).not.toBeInTheDocument();
    expect(screen.queryByText("Business Wallet")).not.toBeInTheDocument();
  });
  it("refreshes verified views through the participation endpoint", async () => {
    const api = mockCreatorApi({ "/creator/campaigns": [{ ...active, remainingDays: 30 }] });
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
    mockCreatorApi({ "/creator/campaigns": [{
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
  it("allows Go Live only after the approved publication is verified", async () => {
    const api = mockCreatorApi({ "/creator/campaigns": [{
      ...active,
      participationId: null,
      status: "ReadyToGoLive",
      contentReviewStatus: "Approved",
      contentRevisionNumber: 1,
      selectedPlatform: "TikTok",
      selectedSocialProfileId: "social-tiktok",
      selectedSocialProfileUrl: "https://www.tiktok.com/@bella",
      publication: { id: "publication", provider: "TikTok", externalContentId: "7611111111111111111", status: "Verified", verificationLabel: "Provider verified", requestedAtUtc: "2026-09-22T12:00:00Z", verifiedAtUtc: "2026-09-22T12:01:00Z", wentLiveAtUtc: null, watchUrl: "https://www.tiktok.com/@creator/video/7611111111111111111" },
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
    expect(screen.getAllByRole("article")).toHaveLength(3);
    expect(screen.getByText("Minimum to cash out: 5,000")).toBeVisible();
    expect(
      screen.queryByText(/Business Pays|Platform Keeps|Customer Cashback/),
    ).not.toBeInTheDocument();
  });
  it("shows friendly earning sources", async () => {
    mount(<CreatorEarnings />);
    expect((await screen.findAllByText("Verified Views")).length).toBeGreaterThan(
      0,
    );
    expect(screen.queryByText("VIEW_REWARD")).not.toBeInTheDocument();
    expect(screen.queryByText("One balance.")).not.toBeInTheDocument();
  });
  it("uses the verified Weymela phone for Telebirr and accepts only Bank destination fields", async () => {
    const api = mockCreatorApi();
    mount(<CreatorEarnings />);
    expect(await screen.findByLabelText("Verified Weymela phone")).toHaveValue("+251911223344");
    expect(screen.getByLabelText("Verified Weymela phone")).toHaveAttribute("readonly");
    expect(screen.queryByLabelText("Account Number")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("radio", { name: "Bank account" }));
    await userEvent.type(screen.getByLabelText("Bank Name"), "CBE");
    await userEvent.type(screen.getByLabelText("Account Number"), "1000123456789");
    await userEvent.click(screen.getByRole("button", { name: "Save Destination" }));
    await waitFor(() => expect(api.writes.at(-1)).toMatchObject({ path: "/creator/payout-destination",
      body: { method: "Bank", bankName: "CBE", accountNumber: "1000123456789" } }));
  });
  it("uses only the registered phone for M-PESA, with no alternate phone or payout amount", async () => {
    const api = mockCreatorApi({ "/creator/payout-destination": {
      method: "Bank", provider: "CBE", account: "••••6789", legalName: "Bella",
      updatedAtUtc: null, isMasked: true, isConfigured: true, registeredPhone: "+251711223344",
      phoneCountry: "ET", telebirrEligible: true, mpesaEligible: true,
    } });
    mount(<CreatorEarnings />);
    await userEvent.click(await screen.findByRole("radio", { name: "M-PESA" }));
    expect(screen.getByLabelText("Verified Weymela phone")).toHaveValue("+251711223344");
    expect(screen.getByLabelText("Verified Weymela phone")).toHaveAttribute("readonly");
    expect(screen.queryByLabelText("Account Number")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Amount")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save Destination" }));
    await waitFor(() => expect(api.writes.at(-1)).toMatchObject({ path: "/creator/payout-destination",
      body: { method: "Mpesa", bankName: null, accountNumber: null } }));
  });
  it("lets a Creator without a verified phone choose Bank while keeping Telebirr unavailable", async () => {
    const api = mockCreatorApi({ "/creator/payout-destination": {
      method: "Telebirr", provider: "Telebirr", account: "", legalName: "Bella",
      updatedAtUtc: null, isMasked: false, isConfigured: false,
    } });
    mount(<CreatorEarnings />);
    expect(await screen.findByLabelText("Verified Weymela phone")).toHaveAttribute("placeholder", "No verified phone available");
    expect(screen.getByRole("button", { name: "Save Destination" })).toBeDisabled();
    await userEvent.click(screen.getByRole("radio", { name: "Bank account" }));
    await userEvent.type(screen.getByLabelText("Bank Name"), "CBE");
    await userEvent.type(screen.getByLabelText("Account Number"), "1000123456789");
    expect(screen.getByRole("button", { name: "Save Destination" })).toBeEnabled();
    await userEvent.click(screen.getByRole("button", { name: "Save Destination" }));
    await waitFor(() => expect(api.writes.at(-1)).toMatchObject({ path: "/creator/payout-destination",
      body: { method: "Bank", bankName: "CBE", accountNumber: "1000123456789" } }));
  });
  it("collects country-specific international bank identifiers without asking for the legal name again", async () => {
    const api = mockCreatorApi();
    mount(<CreatorEarnings />);
    await userEvent.click(await screen.findByRole("radio", { name: "Bank account" }));
    await userEvent.selectOptions(screen.getByLabelText("Bank country"), "US");
    await userEvent.type(screen.getByLabelText("Bank Name"), "US Test Bank");
    await userEvent.type(screen.getByLabelText("Account Number"), "US123456789");
    await userEvent.type(screen.getByLabelText("Routing number"), "021000021");
    expect(screen.queryByLabelText(/Legal name/i)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save Destination" }));
    await waitFor(() => expect(api.writes.at(-1)).toMatchObject({ path: "/creator/payout-destination",
      body: { method: "Bank", bankName: "US Test Bank", accountNumber: "US123456789",
        bankCountry: "US", routingNumber: "021000021" } }));
  });
  it("shows Creator income from attributed entries without treating purchase totals as earnings", async () => {
    mockCreatorApi({ "/creator/earnings": { ...earnings, history: [
      { id: "sale-income", campaign: "Coffee stories", business: "Bella Restaurant", source: "Sale Earnings",
        sourceType: "VIEW_PLUS_SALE", amount: 30, atUtc: "2026-09-29T16:47:00Z" },
      { id: "ugc-income", campaign: "Product story", business: "Bella Restaurant", source: "UGC Earnings",
        sourceType: "UGC", amount: 4500, atUtc: "2026-09-28T12:00:00Z" },
    ] } });
    const rendered = mount(<CreatorEarnings />);
    await screen.findByRole("heading", { name: "Earning History" });
    const cards = rendered.container.querySelectorAll(".mobile-data .data-card");
    expect(cards).toHaveLength(2);
    expect(cards[0]).toHaveTextContent("Bella Restaurant");
    expect(cards[0]).toHaveTextContent("Sale Commission");
    expect(cards[0]).toHaveTextContent("+30 ETB");
    expect(cards[1]).toHaveTextContent("UGC");
    expect(cards[1]).toHaveTextContent("+4,500 ETB");
    expect(rendered.container).not.toHaveTextContent("1,000 ETB");
  });
  it("shows threshold eligibility without payout calendar dates", async () => {
    mount(<CreatorEarnings />);
    expect(
      await screen.findByText("Eligible for payout · 5,000"),
    ).toBeVisible();
    expect(screen.queryByRole("button", { name: "Request Payout" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save Destination" })).toBeEnabled();
  });
  it("shows amount remaining and disables payout below threshold", async () => {
    mockCreatorApi({
      "/creator/earnings": {
        ...earnings,
        availableEarnings: 400,
        amountNeeded: 4600,
        eligibleAmount: 0,
      },
    });
    mount(<CreatorEarnings />);
    expect(await screen.findByText("Amount remaining: 4,600")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Request Payout" })).not.toBeInTheDocument();
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
      (await screen.findAllByRole("button", { name: "Pay" }))[0],
    );
    expect(screen.getByRole("button", { name: "Confirm Paid" })).toBeDisabled();
    expect(
      screen.getByLabelText(
        "I confirm this payment has been completed externally.",
      ),
    ).not.toBeChecked();
  });
  it("submits the Admin-selected payout amount and previews the remaining balance", async () => {
    const api = mockApi();
    mount(<AdminPayouts />);
    await userEvent.click((await screen.findAllByRole("button", { name: "Pay" }))[0]);
    const amountInput = screen.getByLabelText("Amount to Pay");
    await userEvent.clear(amountInput);
    await userEvent.type(amountInput, "5200");
    expect(screen.getByText("Remaining: 200")).toBeVisible();
    await userEvent.type(screen.getByLabelText("Payment reference"), "uat-payment-5200");
    await userEvent.click(screen.getByLabelText("I confirm this payment has been completed externally."));
    await userEvent.click(screen.getByRole("button", { name: "Confirm Paid" }));
    await waitFor(() => expect(api.writes).toHaveLength(2));
    expect(api.writes[0]).toMatchObject({ path: "/admin/payouts/Creator/creator/prepare", body: { amount: 5200 } });
    expect(api.writes[1]).toMatchObject({ path: "/admin/payouts/saved/paid", body: { reference: "uat-payment-5200" } });
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
      await screen.findByRole("link", { name: "Get Offer QR" }),
    ).toBeVisible();
    expect(screen.getByText("2% cashback")).toBeVisible();
    expect(screen.getByText("Coffee Shop")).toBeVisible();
    expect(screen.getByText("Good coffee, thoughtful stories.")).toBeVisible();
    expect(screen.getByText("Bella", { selector: "[data-no-translate]" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Watch Promotion" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Get Directions" })).toBeVisible();
    expect(screen.getAllByText("Addis Ababa").some(element => element.tagName === "SPAN")).toBe(true);
    expect(screen.queryByText("CAM-100")).not.toBeInTheDocument();
    expect(screen.queryByText("creator")).not.toBeInTheDocument();
    expect(screen.queryByText(/wallet|platform revenue|commission|budget/i)).not.toBeInTheDocument();
    expect(api.fetch.mock.calls.some(([path]) => path === "/api/customer/offers")).toBe(true);
  });
  it("renders a UGC Customer Offer without inventing Creator attribution", async () => {
    mockApi({ "/customer/offers": [ugcCustomerOffer] });
    mount(<CustomerOffers />);
    expect(await screen.findByText("Save on your next visit")).toBeVisible();
    expect(screen.getByText("5% cashback")).toBeVisible();
    expect(screen.queryByText("Customer offer")).not.toBeInTheDocument();
    expect(screen.queryByText(/By /)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Watch Promotion" })).not.toBeInTheDocument();
  });
  it("uses the Customer offer title when the optional slogan is blank", async () => {
    mockApi({ "/customer/offers": [{ ...offer, slogan: "  " }] });
    const { container } = mount(<CustomerOffers discover />);
    expect(await screen.findByText("Coffee stories")).toBeVisible();
    expect(container.querySelector(".customer-promotion-title")).toHaveTextContent("Coffee stories");
    expect(container.querySelector(".customer-promotion-title")).not.toHaveTextContent(/Not provided|No slogan/i);
  });
  it("keeps the Customer offer detail compact without a slogan or Creator attribution", async () => {
    mockApi({ "/customer/offers": [{ ...ugcCustomerOffer, slogan: "" }] });
    mount(<CustomerOfferQr />, "/customer/offers/ugc-offer", "/customer/offers/:id");
    expect(await screen.findByRole("heading", { name: "Bella Beauty" })).toBeVisible();
    expect(screen.queryByText("Save on your next visit")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Watch Promotion" })).not.toBeInTheDocument();
    expect(screen.queryByText("Available from this Business")).not.toBeInTheDocument();
  });
  it("only renders optional promotion links when the API provides safe URLs", async () => {
    mockApi({
      "/customer/offers": [{ ...offer, watchUrl: null, business: { ...offer.business, directionsUrl: null } }],
    });
    mount(<CustomerOffers />);
    await screen.findByRole("link", { name: "Get Offer QR" });
    expect(screen.queryByRole("link", { name: "Watch Promotion" })).not.toBeInTheDocument();
  });
  it.each([false, true])("searches actual Business and Creator names on Customer Home and Discover (%s)", async discover => {
    mockApi({ "/customer/offers": [offer, ugcCustomerOffer] });
    mount(<CustomerOffers discover={discover} />);
    const search=await screen.findByRole("searchbox", {name:"Search business or creator"});
    await userEvent.type(search,"Abc");
    expect(screen.getAllByRole("article")).toHaveLength(1);
    expect(screen.getByText("Abc Coffee")).toBeVisible();
    await userEvent.clear(search);await userEvent.type(search,"Bella");
    expect(screen.getAllByRole("article")).toHaveLength(2);
    await userEvent.clear(search);await userEvent.type(search,"No matching name");
    expect(screen.queryAllByRole("article")).toHaveLength(0);
  });
  it("keeps Discover on the same eligible Customer offer API and filters safely", async () => {
    mockApi({ "/customer/offers": [offer, ugcCustomerOffer] });
    mount(<CustomerOffers discover />);
    expect(await screen.findByRole("heading", { name: "Discover Promotions", level: 1 })).toBeVisible();
    expect(screen.getAllByRole("heading", { name: "Discover Promotions" })).toHaveLength(1);
    expect(screen.getAllByRole("article")).toHaveLength(2);
    expect(screen.getAllByRole("link", { name: "Get Directions" })).toHaveLength(2);
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Filter promotions" }), "UGC_CUSTOMER_OFFER");
    expect(screen.getByText("Bella Beauty")).toBeVisible();
    expect(screen.queryByText("Abc Coffee")).not.toBeInTheDocument();
  });
  it("keeps Watch, Directions and Offer QR visible on Customer Home, Discover and details", async () => {
    const screens: Array<{ element: ReactNode; path?: string; pattern?: string; qrRole: "link" | "button" }> = [
      { element: <CustomerOffers />, qrRole: "link" },
      { element: <CustomerOffers discover />, qrRole: "link" },
      { element: <CustomerOfferQr />, path: "/customer/offers/offer", pattern: "/customer/offers/:id", qrRole: "button" },
    ];
    for (const item of screens) {
      const rendered = mount(item.element, item.path, item.pattern);
      const directions = await screen.findByRole("link", { name: "Get Directions" });
      if (item.qrRole === "link") expect(screen.getByRole("link", { name: "Watch Promotion" })).toBeVisible();
      else expect(screen.queryByRole("link", { name: "Watch Promotion" })).not.toBeInTheDocument();
      expect(directions).toBeVisible();
      expect(new URL(directions.getAttribute("href")!).hostname).toMatch(/google\.com$/);
      expect(screen.getByRole(item.qrRole, { name: "Get Offer QR" })).toBeVisible();
      rendered.unmount();
    }
  });
  it("does not show Directions when URL, coordinates and address are all missing", async () => {
    mockApi({ "/customer/offers": [{ ...offer, location: null, business: { ...offer.business, directionsUrl: null, latitude: null, longitude: null, address: null } }] });
    mount(<CustomerOffers />);
    await screen.findByRole("link", { name: "Get Offer QR" });
    expect(screen.queryByRole("link", { name: "Get Directions" })).not.toBeInTheDocument();
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
          customerPaidAmount: { amount: 1000, currency: "ETB" }, cashbackEarned: { amount: 50, currency: "ETB" },
          discountReceived: null, purchasedAtUtc: "2026-09-21T10:00:00Z",
        },
      ],
    });
    const rendered = mount(<CustomerTransactions />);
    expect(await screen.findByText("Total Spent")).toBeVisible();
    expect(screen.getByText("Total Cashback Earned")).toBeVisible();
    expect(screen.getByText("2,000 ETB")).toBeVisible();
    expect(screen.getByText("90 ETB")).toBeVisible();
    expect(screen.getAllByText("Cashback Earned")).toHaveLength(2);
    expect(screen.queryByText("Promoted by Bella")).not.toBeInTheDocument();
    expect(screen.queryByText("Original purchase")).not.toBeInTheDocument();
    expect(screen.queryByText("Discount")).not.toBeInTheDocument();
    const cards = rendered.container.querySelectorAll(".customer-transaction-card");
    expect(within(cards[1] as HTMLElement).getByText("+50 ETB")).toBeVisible();
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
    expect(await screen.findByRole("region", { name: "Cashback summary" })).toBeVisible();
    expect(screen.getByRole("region", { name: "Cashback summary" })).toHaveTextContent("2,800 ETB");
    expect(screen.getByRole("region", { name: "Cashback summary" })).toHaveTextContent("4,000 ETB");
    expect(screen.getByText("1,200 ETB to cash out")).toBeVisible();
    expect(screen.getByText("Paid out")).toBeVisible();
    expect(rendered.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "70");
    expect(rendered.container).not.toHaveTextContent(/journal|customerId|reference/i);
  });
  it("shows empty transaction and payout histories", async () => {
    mockApi();
    const rendered = mount(<CustomerTransactions />);
    expect(await screen.findByText("No transactions yet.")).toBeVisible();
    rendered.unmount();
    mount(<CustomerCashback />);
    expect(await screen.findByText("No payouts yet.")).toBeVisible();
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
    await userEvent.click(screen.getByRole("button", { name: "Get Offer QR" }));
    expect(
      await screen.findByRole("img", { name: "Offer QR for the cashier" }),
    ).toBeVisible();
    expect(screen.getByText("Show this QR to the cashier.")).toBeVisible();
    expect(api.writes[0]?.path).toBe("/customer/offers/offer/qr");
  });
  it("provides scanner and an authoritative manual lookup fallback", () => {
    mount(<Checkout />);
    expect(screen.getByRole("button", { name: "Scan QR" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Enter Manually" })).toBeEnabled();
  });
  it("completes the supported manual checkout without exposing a raw QR token", async () => {
    const api = mockApi();
    mount(<Checkout />);
    expect(screen.queryByLabelText("QR code")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Enter Manually" }));
    await userEvent.type(screen.getByLabelText("Creator ID"), "100");
    await userEvent.type(screen.getByLabelText("Customer phone"), "0911111111");
    await userEvent.click(screen.getByRole("button", { name: "Find eligible offers" }));
    expect(await screen.findByLabelText("Total Purchase Amount")).toBeVisible();
    await userEvent.type(
      screen.getByLabelText("Total Purchase Amount"),
      "1000",
    );
    await userEvent.click(screen.getByRole("button", { name: "Submit" }));
    await waitFor(() =>
      expect(api.writes[1].body).toEqual({
        creatorId: "100",
        customerPhone: "0911111111",
        offerId: "offer",
        purchaseAmount: 1000,
      }),
    );
  });
});
