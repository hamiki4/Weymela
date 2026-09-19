import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Inbox } from "../src/features/notifications/Inbox";
import { ManualDeposit, DepositSubmission } from "../src/features/business/DepositSubmission";
import { ConnectionStatus } from "../src/app/ConnectionStatus";
import { Checkout } from "../src/features/commerce/Checkout";
import { mockApi } from "./fixtures";

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
  it("requires user confirmation before installing an available update", async () => {
    const worker = { postMessage: vi.fn() }; vi.stubGlobal("navigator", { onLine: true, serviceWorker: { addEventListener: vi.fn() } });
    wrap(<ConnectionStatus />); act(() => window.dispatchEvent(new CustomEvent("weymela-update", { detail: worker })));
    expect(worker.postMessage).not.toHaveBeenCalled(); await userEvent.click(screen.getByRole("button", { name: "Reload when ready" }));
    expect(worker.postMessage).toHaveBeenCalledWith({ type: "ACTIVATE_UPDATE" });
  });
  it("never pretends disconnected deposit processing credited funds", async () => {
    mockApi({ "/business/deposit-method": { mode: "Disabled" } }); wrap(<DepositSubmission />);
    expect(await screen.findByText(/Deposits are not connected/)).toBeVisible(); expect(screen.queryByRole("button", { name: /Submit|Add Funds/ })).not.toBeInTheDocument();
  });
  it("submits arbitrary positive deposit for review without claiming wallet credit", async () => {
    const api = mockApi({ "/business/deposit-requests": [] }); wrap(<ManualDeposit />);
    await userEvent.type(screen.getByLabelText("Amount"), "17.23"); await userEvent.type(screen.getByLabelText("Payment reference"), "PAY-17");
    await userEvent.click(screen.getByRole("button", { name: "Submit for Review" }));
    expect(await screen.findByText(/wallet has not been credited/)).toBeVisible(); expect(api.writes[0].body).toEqual({ amount: 17.23, externalReference: "PAY-17", proofReference: null });
  });
});
describe("Camera lifecycle", () => {
  it.each([ ["NotAllowedError", /Camera permission was denied/], ["NotFoundError", /No camera was found/] ])("renders %s without exposing browser error internals", async (name, message) => {
    vi.stubGlobal("navigator", { onLine: true, mediaDevices: { getUserMedia: vi.fn().mockRejectedValue(new DOMException("sensitive browser detail", name as string)) } });
    wrap(<Checkout />); await userEvent.click(screen.getByRole("button", { name: "Scan QR" }));
    expect(await screen.findByText(message as RegExp)).toBeVisible(); expect(screen.queryByText(/sensitive browser detail/)).not.toBeInTheDocument();
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
