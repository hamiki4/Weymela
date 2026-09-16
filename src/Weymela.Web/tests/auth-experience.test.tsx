import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

const mocks = vi.hoisted(() => ({
  startEmailCode: vi.fn(),
  verifyEmailCode: vi.fn(),
  signInWithPassword: vi.fn(),
  verifyPasswordRecovery: vi.fn(),
  resetPassword: vi.fn(),
  cancelPasswordRecovery: vi.fn(),
  refresh: vi.fn(),
}));
vi.mock("../src/api/client", () => ({
  post: vi.fn(),
  useAction: () => ({ busy: false, error: null, run: vi.fn() }),
  useResource: () => ({
    data: { development: false, personas: null },
    loading: false,
    error: null,
    reload: vi.fn(),
  }),
}));
vi.mock("../src/app/Session", () => ({
  roleHome: {
    Business: "/business",
    Creator: "/creator",
    PlatformAdmin: "/admin",
    Customer: "/customer/offers",
    Cashier: "/checkout",
    Onboarding: "/onboarding",
  },
  useSession: () => ({ user: null, refresh: mocks.refresh }),
}));
vi.mock("../src/auth/firebase", () => ({
  ProfileSelectionRequiredError: class extends Error {
    profiles = [];
  },
  exchangeFirebaseToken: vi.fn(),
  readFirebasePublicConfig: vi.fn(() => ({})),
  initializeFirebase: vi.fn(() => ({
    auth: {},
    persistenceReady: Promise.resolve(),
  })),
  FirebaseWebAuthAdapter: class {
    startEmailCode = mocks.startEmailCode;
    verifyEmailCode = mocks.verifyEmailCode;
    signInWithPassword = mocks.signInWithPassword;
    verifyPasswordRecovery = mocks.verifyPasswordRecovery;
    resetPassword = mocks.resetPassword;
    cancelPasswordRecovery = mocks.cancelPasswordRecovery;
    completeRedirect = vi.fn(async () => false);
    selectProfile = vi.fn();
  },
}));

import { SignIn } from "../src/app/SignIn";

function renderSignIn(path = "/sign-in") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SignIn />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.startEmailCode.mockResolvedValue(undefined);
  mocks.verifyEmailCode.mockResolvedValue(undefined);
  mocks.signInWithPassword.mockResolvedValue(undefined);
  mocks.verifyPasswordRecovery.mockResolvedValue("PasswordReset");
  mocks.resetPassword.mockResolvedValue(undefined);
  mocks.cancelPasswordRecovery.mockResolvedValue(undefined);
  mocks.refresh.mockResolvedValue(undefined);
});

describe("final authentication experience", () => {
  it("separates Create account and Sign in before asking for an identifier", () => {
    renderSignIn();
    expect(screen.getByRole("img", { name: "Weymela" })).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Create account" }),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeVisible();
    expect(
      screen.queryByLabelText(/Email|Phone|Password/),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Forgot your PIN|Firebase|session|token|E\.164/),
    ).not.toBeInTheDocument();
  });

  it("starts account creation with email only and uses Continue", async () => {
    renderSignIn();
    await userEvent.click(
      screen.getByRole("button", { name: "Create account" }),
    );
    expect(
      screen.getByRole("heading", { name: "Create your account" }),
    ).toBeVisible();
    expect(screen.getByLabelText("Email address")).toBeVisible();
    expect(screen.queryByLabelText("Phone number")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
    await userEvent.type(
      screen.getByLabelText("Email address"),
      "owner@example.com",
    );
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.startEmailCode).toHaveBeenCalledWith(
      "owner@example.com",
      "Signup",
    );
    expect(
      await screen.findByRole("heading", { name: "Check your email" }),
    ).toBeVisible();
    expect(screen.getByText(/If this email can be used to create a Weymela account/)).toBeVisible();
    expect(screen.queryByText("We sent a verification code to your email.")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Verify" })).toBeVisible();
  });

  it("uses phone and password for full sign-in without starting email", async () => {
    renderSignIn("/sign-in?intent=sign-in");
    expect(screen.getByLabelText("Phone number")).toHaveAttribute(
      "autocomplete",
      "username",
    );
    expect(screen.getByLabelText("Password")).toHaveAttribute(
      "autocomplete",
      "current-password",
    );
    await userEvent.type(screen.getByLabelText("Phone number"), "0911111111");
    await userEvent.type(
      screen.getByLabelText("Password"),
      "correct horse battery staple",
    );
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(mocks.signInWithPassword).toHaveBeenCalledWith(
      "0911111111",
      "correct horse battery staple",
    );
    expect(mocks.startEmailCode).not.toHaveBeenCalled();
  });

  it("keeps forgot-password recovery email-only", async () => {
    renderSignIn("/sign-in?intent=sign-in");
    await userEvent.click(
      screen.getByRole("button", { name: "Forgot password?" }),
    );
    expect(
      screen.getByRole("heading", { name: "Reset your password" }),
    ).toBeVisible();
    expect(screen.getByLabelText("Email address")).toBeVisible();
    expect(screen.queryByLabelText("Phone number")).not.toBeInTheDocument();
  });

  it("does not expose password enrollment as a permanent sign-in action", async () => {
    renderSignIn("/sign-in?intent=sign-in");
    expect(
      screen.queryByRole("button", { name: "Set up password" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Forgot password?" }),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "Back" })).toBeVisible();
  });

  it("finishes password recovery through the server-bound transaction without a client grant", async () => {
    renderSignIn("/sign-in?intent=sign-in");
    await userEvent.click(screen.getByRole("button", { name: "Forgot password?" }));
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.com");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await userEvent.type(screen.getByLabelText("Verification code"), "123456");
    await userEvent.click(screen.getByRole("button", { name: "Verify" }));
    expect(mocks.verifyPasswordRecovery).toHaveBeenCalledWith("owner@example.com", "123456");

    await userEvent.type(screen.getByLabelText("New password"), "new correct horse battery staple");
    await userEvent.type(screen.getByLabelText("Confirm new password"), "new correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Reset password" }));
    expect(mocks.resetPassword).toHaveBeenCalledWith(
      "new correct horse battery staple",
      "new correct horse battery staple",
    );
  });

  it("resumes an email-verified account that does not yet have a password", async () => {
    mocks.verifyPasswordRecovery.mockResolvedValueOnce("AccountSetup");
    renderSignIn("/sign-in?intent=sign-in");
    await userEvent.click(screen.getByRole("button", { name: "Forgot password?" }));
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.com");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    await userEvent.type(screen.getByLabelText("Verification code"), "123456");
    await userEvent.click(screen.getByRole("button", { name: "Verify" }));

    expect(mocks.refresh).toHaveBeenCalledOnce();
    expect(screen.queryByLabelText("New password")).not.toBeInTheDocument();
    expect(mocks.resetPassword).not.toHaveBeenCalled();
  });

  it("cancels the server recovery transaction when returning to sign in", async () => {
    renderSignIn("/sign-in?intent=sign-in");
    await userEvent.click(screen.getByRole("button", { name: "Forgot password?" }));
    await userEvent.click(screen.getByRole("button", { name: "Back to sign in" }));
    expect(mocks.cancelPasswordRecovery).toHaveBeenCalledOnce();
    expect(await screen.findByRole("heading", { name: "Welcome back" })).toBeVisible();
  });
});
