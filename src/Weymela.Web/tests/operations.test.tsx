import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Inbox } from "../src/features/notifications/Inbox";
import { ManualDeposit, DepositSubmission } from "../src/features/business/DepositSubmission";
import { ConnectionStatus } from "../src/app/ConnectionStatus";
import { Checkout } from "../src/features/commerce/Checkout";
import { mockApi } from "./fixtures";

vi.mock("../src/app/Session", () => ({
  useSession: () => ({ user: { role: "Business" } }),
}));

const decoder = vi.hoisted(() => ({ start: vi.fn(), stop: vi.fn() }));
vi.mock("@zxing/browser", () => ({ BrowserQRCodeReader: class { decodeFromStream = decoder.start; } }));
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); decoder.start.mockReset(); decoder.stop.mockReset(); });
const wrap = (element: React.ReactNode) => render(<MemoryRouter>{element}</MemoryRouter>);
const notification = { id: "one", title: "View Reward earned", message: "Your earnings are updated.", route: "/creator/earnings", createdAtUtc: "2026-09-12T00:00:00Z", readAtUtc: null };
describe("Operational states", () => {
  it("renders a designed empty notification inbox", async () => {
    mockApi({ "/notifications": { items: [], unreadCount: 0 } }); wrap(<Inbox />);
    expect(await screen.findByText("You’re all caught up")).toBeVisible(); expect(screen.getByRole("button", { name: "Mark all read" })).toBeDisabled();
  });
  it("marks one notification read using the scoped API", async () => {
    const api = mockApi({ "/notifications": { items: [notification], unreadCount: 1 } }); wrap(<Inbox />);
    await userEvent.click(await screen.findByRole("button", { name: "Mark read" }));
    expect(api.writes[0].path).toBe("/notifications/one/read"); expect(screen.getByRole("link", { name: "Open" })).toHaveAttribute("href", "/creator/earnings");
  });
  it("marks all read without submitting a client-selected user or role", async () => {
    const api = mockApi({ "/notifications": { items: [notification], unreadCount: 1 } }); wrap(<Inbox />);
    await userEvent.click(await screen.findByRole("button", { name: "Mark all read" })); expect(api.writes[0].path).toBe("/notifications/read-all"); expect(api.writes[0].body).toBeNull();
  });
  it("shows offline state and does not queue a notification mutation", async () => {
    const api = mockApi({ "/notifications": { items: [notification], unreadCount: 1 } }); vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
    wrap(<><ConnectionStatus /><Inbox /></>); await userEvent.click(await screen.findByRole("button", { name: "Mark read" }));
    expect(screen.getAllByText(/Nothing.*queued/).length).toBeGreaterThan(0); expect(api.writes).toHaveLength(0);
  });
  it("does not interrupt an active session when an update becomes available", () => {
    const worker = { postMessage: vi.fn() }; vi.stubGlobal("navigator", { onLine: true, serviceWorker: { addEventListener: vi.fn() } });
    wrap(<ConnectionStatus />); act(() => window.dispatchEvent(new CustomEvent("weymela-update", { detail: worker })));
    expect(screen.queryByRole("status", { name: "Update available" })).not.toBeInTheDocument();
    expect(worker.postMessage).not.toHaveBeenCalled();
  });
  it("keeps the offline warning after removing the update prompt", () => {
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
    wrap(<ConnectionStatus />);
    expect(screen.getByText(/You’re offline/)).toBeVisible();
    expect(screen.queryByText("Update available")).not.toBeInTheDocument();
  });
  it("never pretends disconnected deposit processing credited funds", async () => {
    mockApi({ "/business/deposit-method": { mode: "Disabled" } }); wrap(<DepositSubmission />);
    expect(await screen.findByText(/Deposits are not connected/)).toBeVisible(); expect(screen.queryByRole("button", { name: /Submit|Add Funds/ })).not.toBeInTheDocument();
  });
  it("rejects receipts over 4 MiB before sending a request", async () => {
    const api = mockApi({ "/business/deposit-requests": [] }); wrap(<ManualDeposit />);
    await userEvent.type(screen.getByLabelText("Amount"), "3000");
    await userEvent.upload(screen.getByLabelText("Payment receipt"), new File([new Uint8Array(4 * 1024 * 1024 + 1)], "large.png", { type: "image/png" }));
    expect(await screen.findByText("Receipt must be 4 MB or smaller.")).toBeVisible();
    expect(screen.getByRole("button", { name: "Submit for Review" })).toBeDisabled();
    expect(api.writes).toHaveLength(0);
  });
  it("maps a non-JSON proxy 413 to the receipt size message", async () => {
    mockApi({ "/business/deposit-requests": [] }); wrap(<ManualDeposit />);
    await screen.findByLabelText("Amount");
    await userEvent.type(screen.getByLabelText("Amount"), "3000");
    await userEvent.upload(screen.getByLabelText("Payment receipt"), new File(["png"], "receipt.png", { type: "image/png" }));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("<html>413</html>", { status: 413 })));
    await userEvent.click(screen.getByRole("button", { name: "Submit for Review" }));
    expect(await screen.findByText("Receipt must be 4 MB or smaller.")).toBeVisible();
  });
  it("submits arbitrary positive deposit for review without claiming wallet credit", async () => {
    const api = mockApi({ "/business/deposit-requests": [] }); wrap(<ManualDeposit />);
    await userEvent.type(screen.getByLabelText("Amount"), "17.23");
    await userEvent.upload(screen.getByLabelText("Payment receipt"), new File(["receipt"], "receipt.png", { type: "image/png" }));
    await userEvent.click(screen.getByRole("button", { name: "Submit for Review" }));
    expect(await screen.findByText(/Your payment is waiting for approval/)).toBeVisible();
    expect(api.writes[0].body).toBeInstanceOf(FormData);
    expect(api.writes[0].body.get("amount")).toBe("17.23");
    expect(api.writes[0].body.get("receipt")).toBeInstanceOf(File);
    expect(screen.queryByLabelText("Payment reference")).not.toBeInTheDocument();
  });
});
describe("Camera lifecycle", () => {
  it.each(["NotAllowedError", "NotFoundError"])("renders %s without exposing browser error internals", async (name) => {
    vi.stubGlobal("navigator", { onLine: true, mediaDevices: { getUserMedia: vi.fn().mockRejectedValue(new DOMException("sensitive browser detail", name as string)) } });
    wrap(<Checkout />); await userEvent.click(screen.getByRole("button", { name: "Scan QR" }));
    expect(await screen.findByText(/Camera unavailable/)).toBeVisible(); expect(screen.queryByText(/sensitive browser detail/)).not.toBeInTheDocument();
  });
  it("stops every camera track when leaving checkout", async () => {
    const stop = vi.fn(); const stream = { getTracks: () => [{ stop }] }; decoder.start.mockResolvedValue({ stop: decoder.stop });
    vi.stubGlobal("navigator", { onLine: true, mediaDevices: { getUserMedia: vi.fn().mockResolvedValue(stream) } });
    const page = wrap(<Checkout />); await userEvent.click(screen.getByRole("button", { name: "Scan QR" })); await waitFor(() => expect(decoder.start).toHaveBeenCalled());
    page.unmount(); expect(stop).toHaveBeenCalled(); expect(decoder.stop).toHaveBeenCalled();
  });
  it("stops a late-resolving camera stream after screen was left", async () => {
    const stop = vi.fn(); let resolve!: (value: unknown) => void;
    const capture = vi.fn().mockImplementation(() => new Promise(r => { resolve = r; }));
    vi.stubGlobal("navigator", { onLine: true, mediaDevices: { getUserMedia: capture } }); const page = wrap(<Checkout />);
    await userEvent.click(screen.getByRole("button", { name: "Scan QR" })); await waitFor(() => expect(capture).toHaveBeenCalled()); page.unmount();
    await act(async () => resolve({ getTracks: () => [{ stop }] })); expect(stop).toHaveBeenCalled(); expect(decoder.start).not.toHaveBeenCalled();
  });
});
