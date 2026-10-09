import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

const mocks = vi.hoisted(() => ({
  startEmailCode: vi.fn(),
  verifyEmailCode: vi.fn(),
  signInWithPassword: vi.fn(),
  verifyPasswordRecovery: vi.fn(),
  resetPassword: vi.fn(),
  cancelPasswordRecovery: vi.fn(),
  post: vi.fn(),
  refresh: vi.fn(),
}));
vi.mock("../src/api/client", () => ({
  post: mocks.post,
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
    profiles: unknown[];
    constructor(profiles: unknown[]) {
      super("Choose a profile");
      this.profiles = profiles;
    }
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
  window.sessionStorage.clear();
  mocks.startEmailCode.mockResolvedValue(undefined);
  mocks.verifyEmailCode.mockResolvedValue(undefined);
  mocks.signInWithPassword.mockResolvedValue(undefined);
  mocks.verifyPasswordRecovery.mockResolvedValue("PasswordReset");
  mocks.resetPassword.mockResolvedValue(undefined);
  mocks.cancelPasswordRecovery.mockResolvedValue(undefined);
  mocks.post.mockResolvedValue(undefined);
  mocks.refresh.mockResolvedValue(undefined);
});

describe("final authentication experience", () => {
  it("separates Create account and Sign in before asking for an identifier", () => {
    renderSignIn();
    expect(screen.getByRole("img", { name: "Weymela" })).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Create Account" }),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeVisible();
    expect(
      screen.queryByLabelText(/Email|Phone|Password/),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Forgot your PIN|Firebase|session|token|E\.164/),
    ).not.toBeInTheDocument();
  });

  it("chooses a public role before collecting the compact registration form", async () => {
    renderSignIn();
    await userEvent.click(screen.getByRole("button", { name: "Create Account" }));
    expect(screen.getByRole("heading", { name: "How would you like to join?" })).toBeVisible();
    expect(screen.getByRole("button", { name: /Customer/ })).toHaveTextContent("ሸማች");
    expect(screen.getByRole("button", { name: /Business Owner/ })).toHaveTextContent("ንግድ ባለቤት");
    expect(screen.getByRole("button", { name: /Content Creator/ })).toHaveTextContent("ይዘት ፈጣሪ");
    expect(screen.queryByRole("button", { name: /Platform Admin|Operations Admin|Cashier/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Customer/ }));
    expect(screen.getByRole("heading", { name: "Customer registration" })).toBeVisible();
    expect(screen.getByLabelText("Full legal name")).toBeVisible();
    expect(screen.getByLabelText("Email address")).toBeVisible();
    expect(screen.getByLabelText("Phone number")).toBeVisible();
    expect(screen.getByLabelText("Country code")).toHaveValue("+251");
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Full legal name"), "Abebe Kebede");
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Phone number"), "+251911111111");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Complete Registration" }));
    expect(mocks.startEmailCode).toHaveBeenCalledWith(
      "owner@example.com",
      "Signup",
    );
    expect(await screen.findByRole("heading", { name: "Verify your email" })).toBeVisible();
    expect(screen.getByText(/five-digit code sent to owner@example.com/)).toBeVisible();
    expect(screen.getByLabelText("Verification code")).toHaveAttribute("maxlength", "5");
    expect(screen.getByRole("button", { name: "Verify" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Resend email" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Edit details" })).toBeVisible();
  });

  it("composes international registration phones from the selected country before claiming them", async () => {
    renderSignIn();
    await userEvent.click(screen.getByRole("button", { name: "Create Account" }));
    await userEvent.click(screen.getByRole("button", { name: /Customer/ }));
    await userEvent.type(screen.getByLabelText("Full legal name"), "International Customer");
    await userEvent.type(screen.getByLabelText("Email address"), "international@example.com");
    await userEvent.selectOptions(screen.getByLabelText("Country code"), "+1");
    await userEvent.type(screen.getByLabelText("Phone number"), "(404) 555-0123");
    await userEvent.click(screen.getByRole("checkbox"));
    fireEvent.submit(screen.getByRole("button", { name: "Complete Registration" }).closest("form")!);
    await userEvent.type(await screen.findByLabelText("Verification code"), "12345");
    await userEvent.click(screen.getByRole("button", { name: "Verify" }));
    expect(mocks.verifyEmailCode).toHaveBeenCalledWith("international@example.com", "Signup", "12345");
    expect(mocks.post).toHaveBeenCalledWith("/account/registration-phone", { phone: "+14045550123" });
    expect(mocks.refresh).toHaveBeenCalledOnce();
  });

  it("requires an explicit dialing code for other supported countries", async () => {
    renderSignIn();
    await userEvent.click(screen.getByRole("button", { name: "Create Account" }));
    await userEvent.click(screen.getByRole("button", { name: /Customer/ }));
    await userEvent.type(screen.getByLabelText("Full legal name"), "Other Country Customer");
    await userEvent.type(screen.getByLabelText("Email address"), "other@example.com");
    await userEvent.selectOptions(screen.getByLabelText("Country code"), "INTL");
    await userEvent.type(screen.getByLabelText("International phone number"), "712345678");
    await userEvent.click(screen.getByRole("checkbox"));
    fireEvent.submit(screen.getByRole("button", { name: "Complete Registration" }).closest("form")!);
    expect(await screen.findByText(/Include the international country code/)).toBeVisible();
    expect(mocks.startEmailCode).not.toHaveBeenCalled();
  });

  it("resends through the same Create Account request without revealing the backend route", async () => {
    mocks.startEmailCode.mockResolvedValue(0);
    renderSignIn();
    await userEvent.click(screen.getByRole("button", { name: "Create Account" }));
    await userEvent.click(screen.getByRole("button", { name: /Customer/ }));
    await userEvent.type(screen.getByLabelText("Full legal name"), "Abebe Kebede");
    await userEvent.type(screen.getByLabelText("Email address"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Phone number"), "+251911111111");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Complete Registration" }));
    await screen.findByRole("heading", { name: "Verify your email" });
    await userEvent.click(screen.getByRole("button", { name: "Resend email" }));
    expect(mocks.startEmailCode).toHaveBeenNthCalledWith(2, "owner@example.com", "Signup");
    expect(screen.getByRole("heading", { name: "Verify your email" })).toBeVisible();
  });

  it("shows only Business fields for Business Owner registration", async () => {
    renderSignIn();
    await userEvent.click(screen.getByRole("button", { name: "Create Account" }));
    await userEvent.click(screen.getByRole("button", { name: /Business Owner/ }));
    expect(screen.getByLabelText("Full legal name")).toBeVisible();
    expect(screen.getByLabelText("Business name")).toBeVisible();
    expect(screen.getByLabelText("Business type")).toBeVisible();
    expect(screen.queryByLabelText("Social-media profile link")).not.toBeInTheDocument();
  });

  it("validates Creator social and audience fields before email verification", async () => {
    renderSignIn();
    await userEvent.click(screen.getByRole("button", { name: "Create Account" }));
    await userEvent.click(screen.getByRole("button", { name: /Content Creator/ }));
    expect(screen.getByLabelText("Social platform")).toBeVisible();
    expect(screen.getByLabelText("Social-media profile link")).toHaveAttribute("type", "url");
    expect(screen.getByLabelText("Follower count")).toHaveAttribute("min", "0");
    await userEvent.selectOptions(screen.getByLabelText("Social platform"), "YouTube");
    expect(screen.getByLabelText("Subscriber count")).toBeVisible();
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

  it("keeps profile selection visible while the authoritative session is still resolving", async () => {
    const { ProfileSelectionRequiredError } = await import("../src/auth/firebase");
    const refresh = (() => {
      let resolve!: () => void;
      const promise = new Promise<void>(yes => { resolve = yes; });
      return { promise, resolve };
    })();
    mocks.refresh.mockReturnValueOnce(refresh.promise);
    mocks.signInWithPassword.mockRejectedValueOnce(new ProfileSelectionRequiredError([
      { role: "Business", subjectId: "business-1", businessId: "business-1", displayName: "Business", publicId: "BUS-1", canCheckout: true },
      { role: "Customer", subjectId: "customer-1", businessId: null, displayName: "Customer", publicId: "CU-1", canCheckout: false },
    ]));
    renderSignIn("/sign-in?intent=sign-in");
    await userEvent.type(screen.getByLabelText("Phone number"), "0911111111");
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    await screen.findByRole("heading", { name: "Choose a profile" });
    const options = screen.getAllByRole("radio");
    expect(options).toHaveLength(2);
    expect(options[0]).toBeChecked();
    expect(options[0]).toHaveAccessibleName(/Business/);
    expect(options[1]).toHaveAccessibleName(/Customer/);

    await userEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(mocks.refresh).toHaveBeenCalledOnce();
    expect(screen.getByRole("heading", { name: "Choose a profile" })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Welcome back" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Preparing secure sign-in/)).not.toBeInTheDocument();
    refresh.resolve();
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
    await userEvent.type(screen.getByLabelText("Verification code"), "12345");
    await userEvent.click(screen.getByRole("button", { name: "Verify" }));
    expect(mocks.verifyPasswordRecovery).toHaveBeenCalledWith("owner@example.com", "12345");

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
    await userEvent.type(screen.getByLabelText("Verification code"), "12345");
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
