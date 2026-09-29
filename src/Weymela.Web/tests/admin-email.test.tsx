import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { AdminAccountDetail, AdminAccounts } from "../src/features/admin/AdminAccounts";
import { AdminRoleEnrollments, OperationsBusinesses, OperationsCreators, OperationsCustomers } from "../src/features/admin/AdminPages";
import { business, creator, mockApi } from "./fixtures";

beforeEach(() => { vi.restoreAllMocks(); invalidateResourceCache(false); });

const email = "very.long.account-holder@example.test";
const account = {
  id: "customer-id", userId: "customer-id", name: "Michael Kibru", role: "Customer",
  status: "Active", approvalState: "Active", safeIdentifier: "v*****r@example.test",
  fullEmail: email, association: null, lastActivityAtUtc: null, canManage: false,
  joinedAtUtc: "2026-09-16T00:00:00Z",
};

describe("authorized Admin account email presentation", () => {
  it("shows the full email in the Platform Admin directory and detail", async () => {
    mockApi({
      "/admin/accounts?role=Customer": [account],
      "/admin/accounts/customer-id?role=Customer": { account, roles: ["Customer"], profiles: [], transactions: [], roleData: null },
      "/admin/accounts/customer-id/deletion-status": { status: "NotRequested" },
    });
    render(<MemoryRouter initialEntries={["/admin/customers"]}><Routes>
      <Route path="/admin/customers" element={<AdminAccounts area="Customer" />} />
      <Route path="/admin/accounts/:id" element={<AdminAccountDetail />} />
    </Routes></MemoryRouter>);
    expect((await screen.findAllByText(email)).length).toBeGreaterThan(0);
    expect(screen.queryByText(account.safeIdentifier)).not.toBeInTheDocument();
    screen.getAllByRole("link", { name: account.name })[0].click();
    expect(await screen.findByText(email)).toBeInTheDocument();
  });

  it("opens a pending invitation detail by its invitation ID", async () => {
    mockApi({ "/admin/accounts?role=Customer": [{ ...account, id: "invitation-id", status: "Pending" }] });
    render(<MemoryRouter><AdminAccounts area="Customer" /></MemoryRouter>);
    const links = await screen.findAllByRole("link", { name: account.name });
    expect(links[0]).toHaveAttribute("href", "/admin/accounts/invitation-id?role=Customer");
  });

  it.each([
    ["Business", OperationsBusinesses, "/admin/businesses", [{ business, status: "Active", activeCampaigns: 1, lastDepositUtc: null, fullEmail: email }]],
    ["Creator", OperationsCreators, "/admin/creators", [{ creator, status: "Active", activeCampaigns: 1, payoutEligible: true, fullEmail: email }]],
    ["Customer", OperationsCustomers, "/admin/customers", [{ customer: { id: "customer-id", displayName: account.name, publicId: "CU-100" }, status: "Active", fullEmail: email }]],
  ])("shows the full email in authorized Operations %s management", async (_role, Component, path, rows) => {
    mockApi({ [path]: rows });
    render(<MemoryRouter><Component /></MemoryRouter>);
    expect((await screen.findAllByText(email)).length).toBeGreaterThan(0);
  });

  it("shows the applicant email during Admin profile review", async () => {
    mockApi({ "/admin/role-enrollments": [{ id: "review", role: "Creator", status: "Pending",
      displayName: account.name, publicId: "CR-100", submittedAtUtc: "2026-09-16T00:00:00Z",
      version: 1, fullEmail: email }] });
    render(<MemoryRouter><AdminRoleEnrollments /></MemoryRouter>);
    expect(await screen.findByText(email)).toBeInTheDocument();
  });
});
