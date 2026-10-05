import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { CreatorLegalGate, CreatorLegalPage, creatorLegalReturn } from "../src/features/creator/CreatorLegalPage";

const promotion = "/creator/discover/00000000-0000-4000-8000-000000000301";
const initial = [
  { id: "creator-v1", type: "CreatorAgreement", version: "1", contentHash: "creator-hash", accepted: true },
  { id: "rules-v2", type: "AntiCircumventionAgreement", version: "2", contentHash: "sha256:rules", accepted: false },
];

function setup({ accepted = false, content = true, changeVersion = false } = {}) {
  let documents = initial.map(document => ({ ...document, accepted: document.type === "CreatorAgreement" || accepted }));
  let reads = 0;
  const writes: { path: string; body: { contentHash: string; confirmed: boolean } }[] = [];
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, options?: RequestInit) => {
    const path = String(input).replace(/^\/api/, "");
    if (path === "/legal/current") {
      reads += 1;
      if (changeVersion && reads > 1) documents = documents.map(row => row.type === "AntiCircumventionAgreement"
        ? { ...row, id: "rules-v3", version: "3", contentHash: "sha256:new" } : row);
      return Response.json(documents);
    }
    const document = documents.find(row => path === `/legal/${row.id}/content`);
    if (document) return content
      ? Response.json({ id: document.id, type: document.type, version: document.version,
          contentHash: document.contentHash, content: "Approved shared rules fixture" })
      : Response.json({ message: "Unavailable" }, { status: 503 });
    if (options?.method === "POST" && path.endsWith("/accept")) {
      const body = JSON.parse(String(options.body)) as { contentHash: string; confirmed: boolean };
      writes.push({ path, body });
      documents = documents.map(row => path === `/legal/${row.id}/accept` ? { ...row, accepted: true } : row);
      return Response.json({ id: path.split("/")[2] });
    }
    return Response.json({ message: "Not found" }, { status: 404 });
  }));
  return { writes };
}

function mount(path: string) {
  render(<MemoryRouter initialEntries={[path]}>
    <Routes>
      <Route path="/creator/legal" element={<CreatorLegalPage />} />
      <Route path="/creator/discover/:id" element={<CreatorLegalGate><h1>Promotion action</h1></CreatorLegalGate>} />
      <Route path="/creator/discover" element={<h1>UGC destination</h1>} />
      <Route path="/creator/promotions" element={<h1>Promotions destination</h1>} />
      <Route path="/creator" element={<h1>Creator Home</h1>} />
    </Routes>
  </MemoryRouter>);
}

beforeEach(() => { vi.restoreAllMocks(); invalidateResourceCache(false); });

describe("Creator legal resolution", () => {
  it("skips the screen for the already accepted current shared version", async () => {
    setup({ accepted: true });
    mount(`/creator/legal?returnTo=${encodeURIComponent(promotion)}`);
    expect(await screen.findByRole("heading", { name: "Promotion action" })).toBeVisible();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("routes a protected Promotion action to one unchecked acknowledgement", async () => {
    const { writes } = setup();
    mount(promotion);
    const checkbox = await screen.findByRole("checkbox", { name: "I agree to Weymela's rules and regulations." });
    expect(screen.getByRole("heading", { name: "Before you continue" })).toBeVisible();
    expect(checkbox).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Accept & Continue" })).toBeDisabled();
    expect(writes).toHaveLength(0);
  });

  it("viewing the verified rules does not accept them", async () => {
    const { writes } = setup();
    mount(`/creator/legal?returnTo=${encodeURIComponent(promotion)}`);
    const checkbox = await screen.findByRole("checkbox");
    await userEvent.click(screen.getByRole("button", { name: "rules and regulations" }));
    const dialog = screen.getByRole("dialog", { name: "Weymela rules and regulations" });
    expect(within(dialog).getByText("Approved shared rules fixture")).toBeVisible();
    expect(within(dialog).getByText("Version 2")).toBeVisible();
    expect(checkbox).not.toBeChecked();
    expect(writes).toHaveLength(0);
    await userEvent.click(within(dialog).getByRole("button", { name: "Back" }));
  });

  it("blocks acceptance when hash-verified content is unavailable", async () => {
    const { writes } = setup({ content: false });
    mount(`/creator/legal?returnTo=${encodeURIComponent(promotion)}`);
    const checkbox = await screen.findByRole("checkbox");
    await userEvent.click(checkbox);
    expect(await screen.findByText("Approved document text is unavailable. Acceptance is paused.")).toBeVisible();
    expect(screen.getByRole("button", { name: "Accept & Continue" })).toBeDisabled();
    expect(writes).toHaveLength(0);
  });

  it.each([
    [promotion, "Promotion action"],
    ["/creator/discover", "UGC destination"],
    ["/creator/promotions?filter=Pending", "Promotions destination"],
    ["/creator/promotions?filter=Active", "Promotions destination"],
    ["/creator/promotions?filter=Completed", "Promotions destination"],
  ])("accepts only the current shared version and returns to %s", async (destination, title) => {
    const { writes } = setup();
    mount(`/creator/legal?returnTo=${encodeURIComponent(destination)}`);
    const checkbox = await screen.findByRole("checkbox");
    const accept = screen.getByRole("button", { name: "Accept & Continue" });
    expect(accept).toBeDisabled();
    await userEvent.click(checkbox);
    await waitFor(() => expect(accept).toBeEnabled());
    await userEvent.click(accept);
    expect(await screen.findByRole("heading", { name: title })).toBeVisible();
    expect(writes).toEqual([{ path: "/legal/rules-v2/accept", body: { contentHash: "sha256:rules", confirmed: true } }]);
  });

  it("rejects unsafe return paths", async () => {
    setup({ accepted: true });
    mount("/creator/legal?returnTo=https%3A%2F%2Foutside.example");
    expect(await screen.findByRole("heading", { name: "Creator Home" })).toBeVisible();
    expect(creatorLegalReturn("//outside.example")).toBe("/creator");
    expect(creatorLegalReturn("/creator/promotions?filter=Active&next=https://outside.example")).toBe("/creator");
    expect(creatorLegalReturn("/creator/campaigns/00000000-0000-4000-8000-000000000301"))
      .toBe("/creator/campaigns/00000000-0000-4000-8000-000000000301");
  });

  it("stops if the required version changes before submission", async () => {
    const { writes } = setup({ changeVersion: true });
    mount(`/creator/legal?returnTo=${encodeURIComponent(promotion)}`);
    const checkbox = await screen.findByRole("checkbox");
    await userEvent.click(checkbox);
    await waitFor(() => expect(screen.getByRole("button", { name: "Accept & Continue" })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: "Accept & Continue" }));
    expect(await screen.findByText("Creator requirements changed. Review the current version before accepting.")).toBeVisible();
    expect(writes).toHaveLength(0);
  });
});
