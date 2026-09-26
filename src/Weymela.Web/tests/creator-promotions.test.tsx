import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import type { CreatorCampaign, CreatorRequest, UgcAssignment, UgcRequest } from "../src/api/types";
import { CreatorPromotions } from "../src/features/creator/CreatorExperience";
import { active, business, mockApi } from "./fixtures";

function promotion(id: string, status: string, extra: Partial<CreatorCampaign> = {}): CreatorCampaign {
  return { ...active, type: "ViewPlusCommission", promotionLiveDurationDays: 30,
    id, budgetId: `budget-${id}`, title: id, status,
    participationId: null, remainingDays: null, contentReviewStatus: null, ...extra };
}
function request(id: string, status: string): CreatorRequest {
  return { id, campaignId: id, campaign: id, business: business.displayName,
    type: "ViewOnly", status, appliedAtUtc: "2026-09-10T00:00:00Z" };
}
function assignment(id: string, status: string): UgcAssignment {
  return { id, opportunityId: id, opportunity: id, businessId: business.id,
    business: business.displayName, creatorId: "creator", creator: "Bella", creatorPayment: 300,
    status, acceptedRevision: 1, revisionAcceptanceRequired: false, dueDateUtc: "2026-10-10T00:00:00Z",
    instructions: "Create an original video", resources: [], location: null,
    platformRequirements: [{ platform: "TikTok", format: "Video", minimumAudience: null }],
    feedback: null, submissionUrl: null };
}
function ugcRequest(id: string, status: string): UgcRequest {
  return { id, opportunityId: id, creatorId: "creator", creator: "Bella", status,
    requestedAtUtc: "2026-09-10T00:00:00Z", rejectionReason: status === "Rejected" ? "Capacity reached" : null };
}
function ugcDetail(id: string, customerOfferEnabled = false) {
  return { opportunity: { id, business: business.displayName, title: id,
    customerOfferEnabled, platformRequirements: [] } };
}
function mount() {
  render(<MemoryRouter initialEntries={["/creator/promotions"]}><Routes>
    <Route path="/creator/promotions" element={<CreatorPromotions />} />
  </Routes></MemoryRouter>);
}

beforeEach(() => { invalidateResourceCache(false); vi.restoreAllMocks(); });

describe("Creator Promotions work groups", () => {
  it("shows only Active, Requests and History as primary filters", async () => {
    mockApi({ "/creator/campaigns": [], "/creator/requests": [], "/creator/ugc/assignments": [], "/creator/ugc/requests": [] });
    mount();
    const tabs = within(screen.getByRole("tablist", { name: "Filter Promotions" }));
    expect(tabs.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Active", "Requests", "History"]);
    expect(await screen.findByText("No active promotions")).toBeVisible();
    expect(screen.getByRole("link", { name: "Discover Promotions" })).toBeVisible();
    expect(screen.queryByText("UGC work")).not.toBeInTheDocument();
    expect(screen.queryByText("Promotion participation")).not.toBeInTheDocument();
  });

  it("keeps detailed Promotion statuses and existing next actions in Active", async () => {
    mockApi({ "/creator/campaigns": [
      promotion("Approved work", "Approved"),
      promotion("Review work", "UnderReview", { contentReviewStatus: "UnderReview" }),
      promotion("Revision work", "ChangesRequested", { contentReviewStatus: "ChangesRequested" }),
      promotion("Ready work", "ReadyToGoLive", { contentReviewStatus: "Approved" }),
      promotion("Live work", "Active", { participationId: "live", remainingDays: 12 }),
      promotion("Paused work", "Paused", { participationId: "paused", remainingDays: 12 }),
      promotion("Funding work", "FundingRequired", { participationId: "funding", remainingDays: 12 }),
    ], "/creator/requests": [], "/creator/ugc/assignments": [], "/creator/ugc/requests": [] });
    mount();
    await screen.findByRole("heading", { name: "Funding work" });
    const row = (name: string) => screen.getByRole("heading", { name }).closest("article")!;
    expect(within(row("Approved work")).getByRole("link", { name: "Add Content" })).toBeVisible();
    expect(within(row("Review work")).getByText("Waiting for Business review")).toBeVisible();
    expect(within(row("Revision work")).getByRole("link", { name: "Update Content" })).toBeVisible();
    expect(within(row("Ready work")).getByRole("button", { name: "Go Live" })).toBeVisible();
    expect(within(row("Live work")).getByRole("link", { name: "View progress" })).toBeVisible();
    expect(within(row("Paused work")).getByText("Paused", { exact: true })).toBeVisible();
    expect(within(row("Funding work")).getByText("Funding Required", { exact: true })).toBeVisible();
  });

  it("puts pending requests in Requests and terminal work in History without duplicate approved requests", async () => {
    mockApi({ "/creator/campaigns": [
      promotion("Approved work", "Approved"),
      promotion("Ended work", "Completed", { participationId: "ended", remainingDays: null }),
      promotion("Rejected content", "Rejected", { contentReviewStatus: "Rejected" }),
    ], "/creator/requests": [request("Approved work", "Approved"), request("Pending work", "Pending"),
      request("Rejected request", "Rejected"), request("Withdrawn request", "Withdrawn")],
    "/creator/ugc/assignments": [], "/creator/ugc/requests": [] });
    mount();
    await screen.findByRole("heading", { name: "Approved work" });
    expect(screen.getAllByRole("article")).toHaveLength(1);
    await userEvent.click(screen.getByRole("tab", { name: "Requests" }));
    expect(await screen.findByRole("heading", { name: "Pending work" })).toBeVisible();
    expect(screen.getByText("Waiting for Business decision")).toBeVisible();
    expect(screen.getAllByRole("article")).toHaveLength(1);
    await userEvent.click(screen.getByRole("tab", { name: "History" }));
    for (const name of ["Ended work", "Rejected content", "Rejected request", "Withdrawn request"])
      expect(screen.getByRole("heading", { name })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Pending work" })).not.toBeInTheDocument();
  });

  it("unifies UGC assignments and requests while preserving status, type and submission", async () => {
    const api = mockApi({ "/creator/campaigns": [promotion("Promotion work", "Approved")], "/creator/requests": [],
      "/creator/ugc/assignments": [assignment("UGC active", "ChangesRequested"), assignment("UGC done", "Approved")],
      "/creator/ugc/requests": [ugcRequest("UGC active", "Approved"), ugcRequest("UGC pending", "Pending"), ugcRequest("UGC declined", "Rejected")],
      "/creator/ugc/UGC active": ugcDetail("UGC active", true),
      "/creator/ugc/UGC done": ugcDetail("UGC done"),
      "/creator/ugc/UGC pending": ugcDetail("UGC pending"),
      "/creator/ugc/UGC declined": ugcDetail("UGC declined"),
    });
    mount();
    const activeRow = (await screen.findByRole("heading", { name: "UGC active" })).closest("article")!;
    expect(screen.getByRole("heading", { name: "Promotion work" })).toBeVisible();
    expect(await within(activeRow).findByText("UGC + Discount")).toBeVisible();
    expect(within(activeRow).getByText("Changes Requested", { exact: true })).toBeVisible();
    await userEvent.type(within(activeRow).getByLabelText("Social post link"), "https://example.com/post");
    await userEvent.click(within(activeRow).getByRole("button", { name: "Update Content" }));
    await waitFor(() => expect(api.writes[0]).toMatchObject({ path: "/creator/ugc/assignments/UGC active/submit",
      body: { submissionUrl: "https://example.com/post" } }));
    await userEvent.click(screen.getByRole("tab", { name: "Requests" }));
    expect(await screen.findByRole("heading", { name: "UGC pending" })).toBeVisible();
    expect(screen.getByText("Waiting for Business decision")).toBeVisible();
    await userEvent.click(screen.getByRole("tab", { name: "History" }));
    expect(await screen.findByRole("heading", { name: "UGC done" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "UGC declined" })).toBeVisible();
    expect(screen.getByText("Capacity reached")).toBeVisible();
  });
});
