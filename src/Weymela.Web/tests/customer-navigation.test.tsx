import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import userEvent from "@testing-library/user-event";
import { Shell } from "../src/app/Shell";
import { App } from "../src/app/App";

vi.mock("../src/app/Session", async (importOriginal) => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return {
    ...original,
    roleHome: { Customer: "/customer/offers" },
    RoleGate: ({ children }: { children: import("react").ReactNode }) => children,
    useSession: () => ({
      loading: false,
      deviceAccess: null,
      accountSecurity: null,
      deviceEnrollment: null,
      user: {
        role: "Customer",
        displayName: "Mimi Customer",
        publicId: "CU-100",
        developmentMode: false,
        canCheckout: false,
        profiles: [
          { role: "Customer", subjectId: "customer-1", businessId: null, displayName: "Mimi Customer", publicId: "CU-100", canCheckout: false },
          { role: "Creator", subjectId: "creator-1", businessId: null, displayName: "Mimi Creator", publicId: "CR-100", canCheckout: false },
        ],
        activeProfileKey: "Customer:customer-1:-",
      },
      signOut: vi.fn(async () => undefined),
      switchProfile: vi.fn(async () => undefined),
    }),
  };
});

function CurrentPath() {
  return <output aria-label="Current route">{useLocation().pathname}</output>;
}

beforeEach(() => {
  vi.stubGlobal("navigator", { onLine: true });
});

describe("Customer mobile navigation", () => {
  it("uses exactly Home, Discover, Transactions, Cashback and Profile", () => {
    render(
      <MemoryRouter initialEntries={["/customer/offers"]}>
        <>
          <CurrentPath />
          <Shell><div>Customer content</div></Shell>
        </>
      </MemoryRouter>,
    );

    const navigation = within(screen.getByRole("navigation", { name: "Mobile navigation" }));
    expect(navigation.getAllByRole("link").map((link) => link.textContent)).toEqual([
      "Home", "Discover", "Transactions", "Cashback",
    ]);
    expect(navigation.getByRole("link", { name: "Home" })).toHaveAttribute(
      "href",
      "/customer/offers",
    );
    expect(navigation.getByRole("link", { name: "Discover" })).toHaveAttribute(
      "href",
      "/customer/discover",
    );
    expect(navigation.getByRole("link", { name: "Transactions" })).toHaveAttribute(
      "href",
      "/customer/transactions",
    );
    expect(navigation.getByRole("link", { name: "Cashback" })).toHaveAttribute(
      "href",
      "/customer/cashback",
    );
    expect(navigation.getByRole("button", { name: "Profile" })).toHaveAttribute("aria-controls", "account-menu");
    expect(navigation.queryByRole("link", { name: "Profile" })).not.toBeInTheDocument();
  });

  it("opens the existing account sheet without navigating from Profile", async () => {
    render(
      <MemoryRouter initialEntries={["/customer/offers"]}>
        <>
          <CurrentPath />
          <Shell><div>Customer content</div></Shell>
        </>
      </MemoryRouter>,
    );

    await userEvent.click(screen.getByRole("button", { name: "Profile" }));
    const accountMenu = screen.getByRole("dialog", { name: "Account menu" });
    expect(accountMenu).toBeVisible();
    expect(within(accountMenu).getByLabelText("Switch profile")).toBeVisible();
    expect(within(accountMenu).getByRole("link", { name: "Add a profile" })).toHaveAttribute(
      "href",
      "/onboarding",
    );
    expect(screen.getByLabelText("Current route")).toHaveTextContent("/customer/offers");

    await userEvent.click(within(accountMenu).getByRole("button", { name: "Close account menu" }));
    await userEvent.click(screen.getByRole("button", { name: "Open account menu" }));
    expect(screen.getByRole("dialog", { name: "Account menu" })).toBeVisible();
  });

  it("redirects legacy Customer history to Transactions", async () => {
    render(
      <MemoryRouter initialEntries={["/customer/history"]}>
        <>
          <CurrentPath />
          <App />
        </>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByLabelText("Current route")).toHaveTextContent(
        "/customer/transactions",
      );
    });
    expect(screen.getByRole("heading", { name: "Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Cashback" })).not.toBeInTheDocument();
  });
});
