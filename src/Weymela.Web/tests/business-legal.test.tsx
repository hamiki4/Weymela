import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { BusinessLegalPage, businessLegalReturn } from "../src/features/business/BusinessLegalPage";

const initial = [
  { id: "business-v2", type: "BusinessAgreement", version: "2", contentHash: "sha256:business", accepted: false },
  { id: "rules-v3", type: "AntiCircumventionAgreement", version: "3", contentHash: "sha256:rules", accepted: false },
];

function setup({ accepted = false, content = false, changeVersion = false } = {}) {
  let documents = initial.map(document => ({ ...document, accepted }));
  let reads = 0;
  const writes: { path: string; body: { contentHash: string; confirmed: boolean } }[] = [];
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, options?: RequestInit) => {
    const path = String(input).replace(/^\/api/, "");
    if (path === "/legal/current") {
      reads += 1;
      if (changeVersion && reads > 1) documents = documents.map(row => row.type === "BusinessAgreement"
        ? { ...row, id: "business-v3", version: "3", contentHash: "sha256:new" } : row);
      return Response.json(documents);
    }
    const document = documents.find(row => path === `/legal/${row.id}/content`);
    if (document) return content
      ? Response.json({ id: document.id, type: document.type, version: document.version, contentHash: document.contentHash,
          content: `Approved test fixture for ${document.type}` })
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

function mount(returnTo = "/business/campaigns/new") {
  render(<MemoryRouter initialEntries={[`/business/legal?returnTo=${encodeURIComponent(returnTo)}`]}>
    <Routes>
      <Route path="/business/legal" element={<BusinessLegalPage />} />
      <Route path="/business/campaigns/new" element={<h1>Promotion destination</h1>} />
      <Route path="/business/ugc/new" element={<h1>UGC destination</h1>} />
      <Route path="/business" element={<h1>Business Home destination</h1>} />
    </Routes>
  </MemoryRouter>);
}

beforeEach(() => { vi.restoreAllMocks(); invalidateResourceCache(false); });

describe("Business legal resolution", () => {
  it("skips the screen when both exact current versions are accepted", async () => {
    setup({ accepted: true });
    mount();
    expect(await screen.findByRole("heading", { name: "Promotion destination" })).toBeVisible();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("keeps missing approved content unaccepted while both version links remain readable", async () => {
    const { writes } = setup();
    mount();
    const checkbox = await screen.findByRole("checkbox");
    const accept = screen.getByRole("button", { name: "Accept & Continue" });
    expect(checkbox).not.toBeChecked();
    expect(accept).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Business Terms" }));
    let dialog = screen.getByRole("dialog", { name: "Business Terms" });
    expect(within(dialog).getByText("Version 2")).toBeVisible();
    expect(within(dialog).getByText("Approved text for this version is unavailable.")).toBeVisible();
    await userEvent.click(within(dialog).getByRole("button", { name: "Back" }));
    await userEvent.click(screen.getByRole("button", { name: "Anti-Circumvention Rules" }));
    dialog = screen.getByRole("dialog", { name: "Anti-Circumvention Rules" });
    expect(within(dialog).getByText("Version 3")).toBeVisible();
    await userEvent.click(within(dialog).getByRole("button", { name: "Back" }));
    expect(writes).toHaveLength(0);
    await userEvent.click(checkbox);
    expect(accept).toBeDisabled();
    expect(writes).toHaveLength(0);
  });

  it.each([
    ["/business/campaigns/new", "Promotion destination"],
    ["/business/ugc/new", "UGC destination"],
  ])("accepts the exact current versions and returns to %s when approved content exists", async (path, title) => {
    const { writes } = setup({ content: true });
    mount(path);
    const accept = await screen.findByRole("button", { name: "Accept & Continue" });
    await waitFor(() => expect(screen.getByRole("checkbox")).toBeEnabled());
    expect(accept).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Business Terms" }));
    expect(within(screen.getByRole("dialog")).getByText("Approved test fixture for BusinessAgreement")).toBeVisible();
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Back" }));
    expect(writes).toHaveLength(0);
    await userEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(accept).toBeEnabled());
    await userEvent.click(accept);
    expect(await screen.findByRole("heading", { name: title })).toBeVisible();
    expect(writes).toEqual([
      { path: "/legal/business-v2/accept", body: { contentHash: "sha256:business", confirmed: true } },
      { path: "/legal/rules-v3/accept", body: { contentHash: "sha256:rules", confirmed: true } },
    ]);
  });

  it("rejects unsafe return destinations", async () => {
    setup({ accepted: true });
    mount("https://outside.example/redirect");
    expect(await screen.findByRole("heading", { name: "Business Home destination" })).toBeVisible();
    expect(businessLegalReturn("//outside.example")).toBe("/business");
    expect(businessLegalReturn("/business/ugc/new?unexpected=1")).toBe("/business");
  });

  it("stops if a required version changes before submission", async () => {
    const { writes } = setup({ content: true, changeVersion: true });
    mount();
    await screen.findByRole("checkbox");
    await waitFor(() => expect(screen.getByRole("button", { name: "Accept & Continue" })).toBeDisabled());
    await userEvent.click(screen.getByRole("checkbox"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Accept & Continue" })).toBeEnabled());
    await userEvent.click(screen.getByRole("button", { name: "Accept & Continue" }));
    expect(await screen.findByText("Business requirements changed. Review the current versions before accepting.")).toBeVisible();
    expect(writes).toHaveLength(0);
  });
});
