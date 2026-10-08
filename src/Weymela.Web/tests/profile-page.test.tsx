import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ProfilePage } from "../src/app/ProfilePage";
import { invalidateResourceCache } from "../src/api/client";
import { mockApi } from "./fixtures";

vi.mock("../src/app/Session", () => ({ useSession: () => ({ user: { displayName: "Bella" } }) }));

const base = { displayName: "Bella", publicId: "CR-BFB1200263DE37F79F1C4B0EA5D61A9D", creatorId: 7205, email: "bella@example.com", phone: null,
  status: "Active", businessType: null, region: null };

function mount(role: "Customer" | "Creator" | "Business", extra: Record<string, unknown> = {}) {
  const api = mockApi({ "/profile": { ...base, role, ...extra }, "/creator/social-accounts": [
    { id: "own-social", platform: "TikTok", profileUrl: "https://www.tiktok.com/@bella", verificationStatus: "Verified" },
  ] });
  render(<MemoryRouter><ProfilePage /></MemoryRouter>);
  return api;
}

beforeEach(() => { vi.restoreAllMocks(); invalidateResourceCache(false); });

describe("full profile page", () => {
  it("shows only available Customer identity fields", async () => {
    mount("Customer", { displayName: "Mimi", publicId: "CU-100", email: null, phone: "+251900000000" });
    expect(await screen.findByText("+251900000000")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Profile", level: 1 })).toHaveClass("sr-only");
    expect(screen.getByText("+251900000000")).toBeVisible();
    expect(screen.queryByText("CU-100")).not.toBeInTheDocument();
    expect(screen.queryByText("Creator ID 7205")).not.toBeInTheDocument();
    expect(screen.queryByText("Public ID")).not.toBeInTheDocument();
    expect(screen.queryByText("Email")).not.toBeInTheDocument();
    expect(screen.queryByText("Social Profiles")).not.toBeInTheDocument();
    expect(screen.queryByText("Business Information")).not.toBeInTheDocument();
    expect(screen.queryByText("Not provided")).not.toBeInTheDocument();
  });

  it("shows compact Creator social links without exposing the long ID or claiming connection", async () => {
    mount("Creator");
    const section = await screen.findByRole("heading", { name: "Social Profiles" });
    expect(section).toBeVisible();
    expect(await screen.findByText("@bella")).toBeVisible();
    expect(screen.getAllByText("Not added")).toHaveLength(3);
    expect(screen.getAllByRole("button", { name: "Add profile" })).toHaveLength(3);
    expect(screen.queryByText("Verified")).not.toBeInTheDocument();
    expect(screen.queryByText("Connected")).not.toBeInTheDocument();
    expect(screen.queryByText(base.publicId)).not.toBeInTheDocument();
    expect(screen.queryByText("Public ID")).not.toBeInTheDocument();
    expect(screen.getByText("Creator ID 7205")).toBeVisible();
    expect(screen.getByRole("button", { name: "Add photo" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Remove photo" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /edit creator id/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /creator id/i })).not.toBeInTheDocument();
    for (const platform of ["tiktok", "youtube", "instagram", "facebook"])
      expect(document.querySelector(`.creator-platform-${platform}`)).toBeInTheDocument();
    expect(screen.queryByText("Phone")).not.toBeInTheDocument();
  });

  it("groups only recorded Business information", async () => {
    mount("Business", { displayName: "Abc Coffee", publicId: "BU-100", businessType: "Cafe", region: "Addis Ababa" });
    const business = await screen.findByRole("heading", { name: "Business Information" });
    expect(business).toBeVisible();
    expect(screen.getByText("Cafe")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Contact Information" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "Business Location" })).toBeVisible();
    expect(screen.getByLabelText("Address or area")).toHaveValue("Addis Ababa");
    expect(screen.queryByText("Phone")).not.toBeInTheDocument();
    expect(screen.queryByText("Wallet")).not.toBeInTheDocument();
    expect(screen.queryByText("Social Profiles")).not.toBeInTheDocument();
    expect(screen.queryByText("BU-100")).not.toBeInTheDocument();
    expect(screen.queryByText("Creator ID 7205")).not.toBeInTheDocument();
    expect(screen.queryByText("Public ID")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add photo" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Change photo" })).not.toBeInTheDocument();
  });

  it("lets the Business complete its directions without requesting Customer location", async () => {
    const api = mount("Business", { displayName: "Abc Coffee", region: "", directionsUrl: null, latitude: null, longitude: null });
    await screen.findByRole("heading", { name: "Business Location" });
    await userEvent.type(screen.getByLabelText("Address or area"), "Bole Road, Addis Ababa");
    await userEvent.type(screen.getByLabelText("Google Maps link"), "https://maps.app.goo.gl/AbCd1234");
    await userEvent.type(screen.getByLabelText("Latitude"), "9.03");
    await userEvent.type(screen.getByLabelText("Longitude"), "38.74");
    await userEvent.click(screen.getByRole("button", { name: "Save location" }));
    await waitFor(() => expect(api.writes[0]).toMatchObject({
      path: "/business/location",
      body: { address: "Bole Road, Addis Ababa", directionsUrl: "https://maps.app.goo.gl/AbCd1234", latitude: 9.03, longitude: 38.74 },
    }));
    expect(await screen.findByText("Business location saved.")).toBeVisible();
  });

  it("shows initials when a referenced Creator photo cannot be retrieved", async () => {
    const fetch = vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input).replace(/^\/api/, "");
      if (path === "/profile") return Response.json({ ...base, role: "Creator", hasCreatorPhoto: true });
      if (path === "/creator/social-accounts") return Response.json([]);
      if (path === "/creator/photo") return new Response(null, { status: 404 });
      return Response.json({}, { status: 404 });
    });
    vi.stubGlobal("fetch", fetch);
    render(<MemoryRouter><ProfilePage /></MemoryRouter>);
    const avatar = await screen.findByLabelText("Bella profile photo");
    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/creator/photo", expect.any(Object)));
    expect(avatar).toHaveTextContent("B");
    expect(avatar.querySelector("img")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Change photo" })).toBeVisible();
  });

  it("adds, views, edits, and removes the Creator's own URL through the small dialog", async () => {
    const user = userEvent.setup();
    let profiles: { id: string; platform: string; profileUrl: string }[] = [];
    const writes: string[] = [];
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input).replace(/^\/api/, "");
      if (path === "/profile") return Response.json({ ...base, role: "Creator" });
      if (path === "/creator/social-accounts") return Response.json(profiles);
      if (path === "/creator/social-profiles/TikTok" && init?.method === "POST") {
        const body = JSON.parse(String(init.body)) as { profileUrl: string };
        writes.push(body.profileUrl);
        profiles = [{ id: "saved", platform: "TikTok", profileUrl: body.profileUrl }];
        return Response.json({ id: "saved" });
      }
      if (path === "/creator/social-profiles/TikTok" && init?.method === "DELETE") {
        profiles = [];
        return new Response(null, { status: 204 });
      }
      return Response.json({}, { status: 404 });
    }));
    render(<MemoryRouter><ProfilePage /></MemoryRouter>);
    const row = (await screen.findByText("TikTok")).closest(".creator-social-row") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: "Add profile" }));
    expect(screen.getByRole("dialog", { name: "TikTok profile" })).toBeVisible();
    await user.type(screen.getByLabelText("TikTok profile URL"), "https://www.tiktok.com/@bella");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(within(row).getByText("@bella")).toBeVisible());
    expect(within(row).getByRole("link", { name: "View TikTok profile" })).toHaveAttribute("href", "https://www.tiktok.com/@bella");
    expect(within(row).getByRole("link", { name: "View TikTok profile" })).toHaveAttribute("rel", "noopener noreferrer");
    await user.click(within(row).getByRole("button", { name: "Edit" }));
    expect(screen.getByLabelText("TikTok profile URL")).toHaveValue("https://www.tiktok.com/@bella");
    await user.clear(screen.getByLabelText("TikTok profile URL"));
    await user.type(screen.getByLabelText("TikTok profile URL"), "https://www.tiktok.com/@new_bella");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(within(row).getByText("@new_bella")).toBeVisible());
    expect(writes).toEqual(["https://www.tiktok.com/@bella", "https://www.tiktok.com/@new_bella"]);
    await user.click(within(row).getByRole("button", { name: "Edit" }));
    await user.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(within(row).getByText("Not added")).toBeVisible());
    expect(within(row).queryByRole("link", { name: "View TikTok profile" })).not.toBeInTheDocument();
  });
});
