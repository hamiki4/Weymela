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
import {
  CreatorDashboard,
  CreatorDiscovery,
  CreatorOpportunity,
  CreatorHowYouEarn,
  CreatorEarnings,
} from "../src/features/creator/CreatorPages";
import {
  CreatorActiveCampaigns,
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
} from "../src/features/commerce/CustomerPages";
import { Checkout } from "../src/features/commerce/Checkout";
import { campaign, detail, earnings, mockApi, wallet } from "./fixtures";
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
});

describe("Creator workspace", () => {
  it("groups own earnings and Campaigns", async () => {
    mount(<CreatorDashboard />);
    expect(
      await screen.findByRole("heading", {
        name: "Make your creativity count.",
      }),
    ).toBeVisible();
    expect(screen.getAllByText("5,400").length).toBeGreaterThan(0);
  });
  it("renders eligible discovery opportunities with Join Campaign", async () => {
    mount(<CreatorDiscovery />);
    expect(
      await screen.findByRole("link", { name: "Join Campaign" }),
    ).toBeVisible();
    expect(screen.queryByText("Campaign Budget")).not.toBeInTheDocument();
  });
  it("has a designed empty discovery state", async () => {
    mockApi({ "/creator/discover": [] });
    mount(<CreatorDiscovery />);
    expect(
      await screen.findByText("No available Campaigns right now"),
    ).toBeVisible();
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
  });
  it("shows only own active Campaign budget", async () => {
    mount(<CreatorActiveCampaigns />);
    expect(await screen.findByText("Your Budget")).toBeVisible();
    expect(screen.getByText("Budget Remaining")).toBeVisible();
    expect(screen.queryByText("Business Wallet")).not.toBeInTheDocument();
  });
  it("refreshes verified views through the participation endpoint", async () => {
    const api = mockApi();
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
    mount(<CreatorEarnings payout />);
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
    mount(<CreatorEarnings payout />);
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
    await userEvent.click(
      await screen.findByRole("button", { name: "Save Financial Settings" }),
    );
    await waitFor(() => expect(api.writes[0].body.expectedVersion).toBe(1));
    expect(api.writes[0].body.settings.effectiveFromUtc).toBeNull();
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
  it("preserves cashback, directions, video and Get Offer QR", async () => {
    mount(<CustomerOffers />);
    expect(
      await screen.findByRole("link", { name: "Get Offer QR" }),
    ).toBeVisible();
    expect(screen.getByText("2% Cashback")).toBeVisible();
    expect(screen.getByRole("link", { name: "Watch Promotion" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Get Directions" })).toBeVisible();
  });
  it("renders the simple QR page with working Back destination", async () => {
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
    expect(
      screen.queryByText(/selected creator|Creator promotion/),
    ).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Get Offer QR" }));
    expect(
      await screen.findByRole("img", { name: "Offer QR for the cashier" }),
    ).toBeVisible();
    expect(screen.getByText("Show this QR to the cashier.")).toBeVisible();
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
