import { beforeEach, expect, it } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { CreatorGoLive } from "../src/features/creator/CreatorGoLive";
import { Inbox } from "../src/features/notifications/Inbox";
import { mockApi } from "./fixtures";

beforeEach(() => invalidateResourceCache(false));

it("submits a Facebook public video with one Go Live action and no verification button", async () => {
  const api = mockApi({});
  let reloads = 0;
  render(<MemoryRouter><CreatorGoLive path="/creator/ugc/assignments/work/launch" profileId="owned-facebook" platform="Facebook" reload={() => reloads++} /></MemoryRouter>);
  await userEvent.type(screen.getByLabelText("Video Link"), "https://m.facebook.com/reel/123456789/");
  await userEvent.click(screen.getByRole("button", { name: "Go Live" }));
  await waitFor(() => expect(reloads).toBe(1));
  expect(api.writes[0]).toMatchObject({ path: "/creator/ugc/assignments/work/launch", body: {
    provider: "Facebook", creatorSocialProfileId: "owned-facebook", externalContentId: "https://m.facebook.com/reel/123456789/",
  } });
  expect(screen.queryByRole("button", { name: "Verify Publication" })).not.toBeInTheDocument();
  expect(screen.queryByText("Live", { exact: true })).not.toBeInTheDocument();
});

it("shows actionable review state without claiming that a pending URL is live", () => {
  mockApi({});
  render(<MemoryRouter><CreatorGoLive path="/creator/work/launch" profileId="owned" reload={() => {}} publication={{
    id: "pending", provider: "TikTok", externalContentId: "7412345678901234567", status: "VerificationPending",
    verificationLabel: "Awaiting Review", requestedAtUtc: "2026-10-17T00:00:00Z", verifiedAtUtc: null,
    wentLiveAtUtc: null, watchUrl: "https://www.tiktok.com/@creator/video/7412345678901234567", goLiveRequestedAtUtc: "2026-10-17T00:00:01Z",
  }} /></MemoryRouter>);
  expect(screen.getByText("Awaiting Review")).toBeVisible();
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
  expect(screen.queryByText(/provider unavailable/i)).not.toBeInTheDocument();
});

it("distinguishes unread and read notification cards for title and message styling", async () => {
  mockApi({ "/notifications": { unreadCount: 1, items: [
    { id: "new", title: "New work", message: "Review this video", route: "/creator/promotions", readAtUtc: null, createdAtUtc: "2026-10-17T00:00:00Z" },
    { id: "seen", title: "Earlier work", message: "Already reviewed", route: "/creator/promotions", readAtUtc: "2026-10-17T00:01:00Z", createdAtUtc: "2026-10-17T00:00:00Z" },
  ] } });
  render(<MemoryRouter><Inbox /></MemoryRouter>);
  expect((await screen.findByText("New work")).closest("article")).toHaveAttribute("data-unread", "true");
  expect(screen.getByText("Review this video").closest("article")).toHaveAttribute("data-unread", "true");
  expect(screen.getByText("Earlier work").closest("article")).toHaveAttribute("data-unread", "false");
});

it("never offers a second launch for a previously live publication requiring attention", () => {
  mockApi({});
  render(<MemoryRouter><CreatorGoLive path="/creator/work/launch" profileId="owned" reload={() => {}} publication={{
    id: "already-live", provider: "TikTok", externalContentId: "7412345678901234567", status: "Failed",
    verificationLabel: "Action Required", requestedAtUtc: "2026-10-17T00:00:00Z", verifiedAtUtc: "2026-10-17T00:00:01Z",
    wentLiveAtUtc: "2026-10-17T00:00:02Z", watchUrl: null, expiresAtUtc: "2026-11-16T00:00:02Z", remainingDays: 20,
  }} /></MemoryRouter>);
  expect(screen.getByText("Action Required")).toBeVisible();
  expect(screen.queryByRole("button", { name: "Go Live" })).not.toBeInTheDocument();
  expect(screen.queryByLabelText("Video Link")).not.toBeInTheDocument();
});
