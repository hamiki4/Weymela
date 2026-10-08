import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Outlet, useLocation } from "react-router-dom";
import userEvent from "@testing-library/user-event";
import { Shell } from "../src/app/Shell";
import { App } from "../src/app/App";

vi.mock("../src/app/Session", async (importOriginal) => {
  const original = await importOriginal<typeof import("../src/app/Session")>();
  return {
    ...original,
    roleHome: { Customer: "/customer/offers" },
    RoleGate: ({ children }: { children?: import("react").ReactNode }) => children ?? <Outlet />,
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
  window.sessionStorage.setItem("weymela.profile-key", "Customer:customer-1:-");
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
      "Home", "Discover", "Cashback", "Transactions", "Profile",
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
    expect(navigation.getByRole("link", { name: "Profile" })).toHaveAttribute("href", "/profile");
  });

  it("navigates to Profile while the gear opens the Settings route", async () => {
    render(
      <MemoryRouter initialEntries={["/customer/offers"]}>
        <>
          <CurrentPath />
          <Shell><div>Customer content</div></Shell>
        </>
      </MemoryRouter>,
    );

    const navigation = within(screen.getByRole("navigation", { name: "Mobile navigation" }));
    await userEvent.click(navigation.getByRole("link", { name: "Profile" }));
    await waitFor(() => expect(screen.getByLabelText("Current route")).toHaveTextContent("/profile"));
    expect(screen.queryByRole("dialog", { name: "Settings" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("link", { name: "Open Settings" }));
    await waitFor(() => expect(screen.getByLabelText("Current route")).toHaveTextContent("/settings"));
    expect(screen.queryByRole("dialog", { name: "Settings" })).not.toBeInTheDocument();
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
