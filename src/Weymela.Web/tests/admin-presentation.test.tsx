import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { AdminUgcPage, AdminWalletsPage } from "../src/features/admin/AdminFinancePages";
import { mockApi } from "./fixtures";

beforeEach(() => { vi.restoreAllMocks(); invalidateResourceCache(false); });
function mount(element: React.ReactNode) { render(<MemoryRouter>{element}</MemoryRouter>); }

describe("Platform Admin financial presentation", () => {
  it("shows fixed UGC Creator pay separately from the Business customer discount", async () => {
    mockApi({ "/admin/ugc/finance": [
      { id: "one", business: "Abc Coffee", title: "UGC only", type: "UGC Only", budget: 220, creatorPayment: 200, customerDiscountPercent: null, creatorUsed: 0, offerUsed: 0, discountUsed: 0, remaining: 220, qualifyingSales: 0, status: "Open" },
      { id: "two", business: "Abc Coffee", title: "UGC sale", type: "UGC + Discount Sale", budget: 720, creatorPayment: 200, customerDiscountPercent: 2, creatorUsed: 220, offerUsed: 25, discountUsed: 20, remaining: 475, qualifyingSales: 1, status: "InProgress" },
    ] });
    mount(<AdminUgcPage />);
    const rows = await screen.findAllByRole("listitem");
    expect(within(rows[0]).getByText("Fixed Creator pay")).toBeInTheDocument();
    expect(within(rows[0]).queryByText("Customer discount")).not.toBeInTheDocument();
    expect(within(rows[1]).getByText("Fixed Creator pay")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Customer discount")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Discount consumed")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Qualifying sales")).toBeInTheDocument();
    expect(within(rows[1]).getByText("2%")).toBeInTheDocument();
    expect(within(rows[1]).getByText("20 Br")).toBeInTheDocument();
  });

  it("uses the existing Business promotional funding endpoint from Wallets", async () => {
    const api = mockApi({
      "/admin/wallets": { businesses: [{ businessId: "biz", business: "Abc Coffee", totalBalance: 1000, available: 700, reserved: 300, pendingDeposit: 0, viewOnlyCount: 0, viewSaleCount: 1, status: "Active" }], promotions: [] },
      "/admin/deposit-requests": [],
    });
    mount(<AdminWalletsPage />);
    await userEvent.click((await screen.findAllByRole("button", { name: "Add Funds" }))[0]);
    const dialog = screen.getByRole("dialog", { name: /Add funds/ });
    await userEvent.type(within(dialog).getByLabelText("Amount (ETB)"), "500");
    await userEvent.type(within(dialog).getByLabelText("Reason"), "Launch support");
    await userEvent.click(within(dialog).getByRole("button", { name: "Add Promotional Funds" }));
    await waitFor(() => expect(api.writes).toHaveLength(1));
    expect(api.writes[0].path).toBe("/admin/accounts/businesses/biz/promotional-funding");
    expect(api.writes[0].body).toEqual({ amount: 500, reason: "Launch support" });
  });

  it("reviews a pending deposit through the existing versioned review endpoint", async () => {
    const api = mockApi({
      "/admin/wallets": { businesses: [{ businessId: "biz", business: "Abc Coffee", totalBalance: 1000, available: 700, reserved: 300, pendingDeposit: 150, viewOnlyCount: 0, viewSaleCount: 1, status: "Active" }], promotions: [] },
      "/admin/deposit-requests": [{ id: "deposit", businessId: "biz", amount: 150, status: "Pending", provider: "ManualApproval", externalReference: "bank-123", proofReference: "proof-123", submittedAtUtc: "2026-09-01T00:00:00Z", version: 3 }],
    });
    mount(<AdminWalletsPage />);
    await userEvent.click((await screen.findAllByRole("button", { name: "Review Deposit" }))[0]);
    const dialog = screen.getByRole("dialog", { name: /Review deposits/ });
    expect(within(dialog).getByText("Proof: proof-123")).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText("Confirmation reference"), "confirmed-123");
    await userEvent.click(within(dialog).getByRole("button", { name: "Approve" }));
    await waitFor(() => expect(api.writes).toHaveLength(1));
    expect(api.writes[0].path).toBe("/admin/deposit-requests/deposit/review");
    expect(api.writes[0].body).toEqual({ approve: true, expectedVersion: 3, confirmationReference: "confirmed-123" });
  });
});
