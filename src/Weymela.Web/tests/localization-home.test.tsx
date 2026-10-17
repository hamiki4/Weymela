import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { LanguageChoice, LanguageProvider } from "../src/localization/Language";
import { BusinessDashboard } from "../src/features/business/BusinessPages";
import { CreatorDashboard } from "../src/features/creator/CreatorExperience";
import { mockApi, wallet, business } from "./fixtures";

vi.mock("../src/app/Session", () => ({ useSession: () => ({ user: { role: "Business" } }) }));
beforeEach(() => { vi.restoreAllMocks(); invalidateResourceCache(false); localStorage.setItem("weymela.language", "am"); mockApi(); });
afterEach(() => { localStorage.removeItem("weymela.language"); document.documentElement.lang = "en"; });

describe("localized role homes", () => {
  it("translates Business controls and preserves funding routes, names and amounts through language changes", async () => {
    mockApi({ "/business/home": { business, wallet, activeCampaigns: 1, creatorRequests: 1,
      lowFundsAlerts: [{ title: "Weekend", fundingPath: "/business/campaigns/fixture?tab=funds" }] } });
    render(<MemoryRouter><LanguageProvider><LanguageChoice /><BusinessDashboard /></LanguageProvider></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "ገንዘብ" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "ማስታወቂያዎችዎ" })).toBeVisible();
    expect(screen.getByRole("link", { name: /ንቁ ማስታወቂያዎች/ })).toHaveAttribute("href", "/business/campaigns?filter=Active");
    expect(screen.getByRole("link", { name: /የፈጣሪ ጥያቄዎች/ })).toHaveAttribute("href", "/business/requests");
    expect(screen.getByRole("link", { name: "QR ቃኝ" })).toHaveAttribute("href", "/checkout");
    expect(screen.getByRole("link", { name: "ዋጋዎች" })).toHaveAttribute("href", "/business/pricing");
    expect(screen.getByRole("link", { name: "ገንዘብ ጨምር" })).toHaveAttribute("href", "/business/campaigns/fixture?tab=funds");
    expect(screen.getByText("Weekend")).toBeVisible(); expect(screen.getByText("4,000")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "English" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "Funds" })).toBeVisible());
    expect(screen.getByRole("link", { name: "Checkout / Scan QR" })).toHaveAttribute("href", "/checkout");
    expect(screen.getByText("Weekend")).toBeVisible(); expect(screen.getByText("4,000")).toBeVisible();
  });

  it("translates Creator work labels and count-bearing accessible names", async () => {
    render(<MemoryRouter><LanguageProvider><LanguageChoice /><CreatorDashboard /></LanguageProvider></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "ሥራዎችዎ" })).toBeVisible();
    expect(await screen.findByRole("link", { name: "የሚጠብቁ ጥያቄዎች፣ 1፣ ንግዱን በመጠባበቅ ላይ" })).toHaveAttribute("href", "/creator/promotions?filter=Pending");
    expect(screen.getByRole("link", { name: "የተጀመሩ ማስታወቂያዎች፣ 1፣ ማስታወቂያዎን ይቀጥሉ" })).toHaveAttribute("href", "/creator/promotions?filter=Active");
    expect(screen.getByRole("heading", { name: "ለእርስዎ ማስታወቂያዎች" })).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "English" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "Your work" })).toBeVisible());
    expect(screen.getByRole("link", { name: "Pending Requests, 1, Waiting for Business" })).toBeVisible();
  });
});
