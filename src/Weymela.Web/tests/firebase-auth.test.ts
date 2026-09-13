import { beforeEach, describe, expect, it, vi } from "vitest";

const { firebaseApp, auth, getApps, initializeApp, getAuth, getIdToken,
  signInWithCustomToken, onAuthStateChanged, firebaseSignOut,
  setPersistence, inMemoryPersistence, signInWithEmailAndPassword,
  createUserWithEmailAndPassword } = vi.hoisted(() => {
  const firebaseApp = { options: { projectId: "weymela-pilot" } };
  const auth: { currentUser?: unknown } = {};
  return {
    firebaseApp, auth,
    getApps: vi.fn(() => [] as unknown[]),
    initializeApp: vi.fn(() => firebaseApp),
    getAuth: vi.fn(() => auth),
    getIdToken: vi.fn(async () => "verified-id-token"),
    signInWithCustomToken: vi.fn(async () => ({ user: auth.currentUser ?? { uid: "v3-user" } })),
    onAuthStateChanged: vi.fn(() => vi.fn()), firebaseSignOut: vi.fn(),
    setPersistence: vi.fn(async () => undefined), inMemoryPersistence: { type: "inMemory" },
    signInWithEmailAndPassword: vi.fn(), createUserWithEmailAndPassword: vi.fn(),
  };
});

vi.mock("firebase/app", () => ({ getApps, initializeApp }));
vi.mock("firebase/auth", () => ({
  getAuth,
  getIdToken,
  signInWithCustomToken,
  onAuthStateChanged,
  setPersistence,
  inMemoryPersistence,
  signOut: firebaseSignOut,
  signInWithEmailAndPassword,
  createUserWithEmailAndPassword,
}));

import {
  FirebaseConfigurationError,
  FirebaseWebAuthAdapter,
  exchangeFirebaseToken,
  initializeFirebase,
  readFirebasePublicConfig,
} from "../src/auth/firebase";

const config = {
  VITE_FIREBASE_API_KEY: "public-key",
  VITE_FIREBASE_AUTH_DOMAIN: "weymela-pilot.firebaseapp.com",
  VITE_FIREBASE_PROJECT_ID: "weymela-pilot",
  VITE_FIREBASE_APP_ID: "1:337027913350:web:test",
};

describe("V3 Firebase Web adapter", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getApps.mockReturnValue([]);
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 204 })));
  });

  it("requires all public client configuration and fails closed when missing", () => {
    expect(readFirebasePublicConfig(config).projectId).toBe("weymela-pilot");
    expect(() => readFirebasePublicConfig({ ...config, VITE_FIREBASE_APP_ID: "" })).toThrow(FirebaseConfigurationError);
  });

  it("initializes the approved project without accepting private credential fields", () => {
    const initialized = initializeFirebase(readFirebasePublicConfig(config));
    expect(initializeApp).toHaveBeenCalledWith(expect.objectContaining({ projectId: "weymela-pilot" }));
    expect(getAuth).toHaveBeenCalledWith(firebaseApp);
    expect(setPersistence).toHaveBeenCalledWith(auth, inMemoryPersistence);
    expect(initialized.auth).toBe(auth);
  });

  it("signs in with a server-issued custom token and exchanges the verified ID token", async () => {
    await new FirebaseWebAuthAdapter(auth as never).signInWithCustomToken("server-custom-token");
    expect(signInWithCustomToken).toHaveBeenCalledWith(auth, "server-custom-token");
    expect(fetch).toHaveBeenCalledWith("/api/auth/firebase/session", expect.objectContaining({ method: "POST" }));
  });

  it("posts only the ID token to the existing session exchange endpoint", async () => {
    await exchangeFirebaseToken("one-time-id-token");
    expect(fetch).toHaveBeenCalledWith("/api/auth/firebase/session", expect.objectContaining({
      method: "POST", credentials: "same-origin",
      body: JSON.stringify({ idToken: "one-time-id-token" }),
    }));
  });

  it("starts email verification without exposing identifiers in a URL", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await adapter.startEmailCode(" owner@example.com ", "Signup", "+251900000000");
    expect(fetch).toHaveBeenCalledWith("/api/auth/email/start", expect.objectContaining({ method: "POST" }));
  });

  it("uses verified email code -> custom token -> Firebase ID token exchange for signup", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await adapter.startEmailCode("owner@example.com", "Signup", "+251900000000");
    expect(signInWithCustomToken).not.toHaveBeenCalled();
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce(new Response(JSON.stringify({ customToken: "verified-signup-token" }), { status: 200 }));
    await adapter.verifyEmailCode("owner@example.com", "Signup", "123456");
    expect(signInWithCustomToken).toHaveBeenCalledWith(auth, "verified-signup-token");
    // The real UID/account invariant is covered by EmailAuthServiceTests and MultiRoleIdentityTests.
    expect(fetch).toHaveBeenLastCalledWith("/api/auth/firebase/session", expect.objectContaining({ method: "POST" }));
  });

  it("uses email-code verification for a phone identifier and makes no SMS/Phone OTP call", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await adapter.startEmailCode("owner@example.com", "Signup", "+251900000000");
    const request = JSON.parse((fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0][1].body as string);
    expect(request).toEqual({ identifier: "owner@example.com", phone: "+251900000000", purpose: "Signup" });
    expect(signInWithCustomToken).not.toHaveBeenCalled();
    expect((fetch as unknown as ReturnType<typeof vi.fn>).mock.calls).toHaveLength(1);
  });

  it("sends a phone identifier to the server without supplying a replacement email", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await adapter.startEmailCode("+251 900 000000", "DeviceEnrollment");
    const request = JSON.parse((fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0][1].body as string);
    expect(request).toEqual({ identifier: "+251900000000", phone: null, purpose: "DeviceEnrollment" });
    expect(signInWithCustomToken).not.toHaveBeenCalled();
  });

  it("verifies a phone-identifier challenge through the same custom-token session path", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await adapter.startEmailCode("+251900000000", "DeviceEnrollment");
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce(new Response(JSON.stringify({ customToken: "same-account-token" }), { status: 200 }));
    await adapter.verifyEmailCode("+251900000000", "DeviceEnrollment", "123456");
    expect(signInWithCustomToken).toHaveBeenCalledWith(auth, "same-account-token");
  });

  it("does not establish a session when the server rejects conflicting identifiers", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce(new Response(null, { status: 409 }));
    await expect(adapter.startEmailCode("owner@example.com", "Signup", "+251900000000")).rejects.toThrow();
    expect(signInWithCustomToken).not.toHaveBeenCalled();
    // Backend conflict/no-merge enforcement is exercised by EmailAuthServiceTests.
  });

  it("rejects failed custom-token authentication without exchanging an ID token", async () => {
    signInWithCustomToken.mockRejectedValueOnce(new Error("invalid custom token"));
    const exchange = vi.fn(async () => undefined);
    await expect(new FirebaseWebAuthAdapter(auth as never, exchange).signInWithCustomToken("bad-token")).rejects.toThrow("invalid custom token");
    expect(exchange).not.toHaveBeenCalled();
  });

  it("rejects failed ID-token/session exchange without creating application state", async () => {
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce(new Response(null, { status: 401 }));
    await expect(exchangeFirebaseToken("untrusted-id-token")).rejects.toThrow("rejected");
    expect(signInWithCustomToken).not.toHaveBeenCalled();
  });

  it("signs out Firebase and clears the Weymela session", async () => {
    await new FirebaseWebAuthAdapter(auth as never).signOut();
    expect(firebaseSignOut).toHaveBeenCalledWith(auth);
    expect(fetch).toHaveBeenCalledWith("/api/session/sign-out", expect.objectContaining({ method: "POST", credentials: "same-origin" }));
  });

  it("requires an email verification exchange before a signup session is established", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await adapter.startEmailCode(" owner@example.com ", "Signup", "+251900000000");
    const startBody = JSON.parse((fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0][1].body as string);
    expect(startBody).toEqual({ identifier: "owner@example.com", phone: "+251900000000", purpose: "Signup" });
    expect(signInWithCustomToken).not.toHaveBeenCalled();

    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce(new Response(JSON.stringify({ customToken: "verified-signup-token" }), { status: 200 }));
    await adapter.verifyEmailCode("owner@example.com", "Signup", "123456");
    expect(signInWithCustomToken).toHaveBeenCalledWith(auth, "verified-signup-token");
  });

  it("does not permit phone-only verification or recovery", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await expect(adapter.startEmailCode("", "Signup", "+251900000000")).rejects.toThrow();
    await expect(adapter.startEmailCode("", "PinRecovery")).rejects.toThrow();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("rejects malformed and non-six-digit email codes before network exchange", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    await expect(adapter.verifyEmailCode("owner@example.com", "Signup", "12")).rejects.toThrow("invalid or expired");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("keeps PIN recovery fail-closed until a secure device reset provider exists", async () => {
    const adapter = new FirebaseWebAuthAdapter(auth as never);
    (fetch as unknown as ReturnType<typeof vi.fn>).mockResolvedValueOnce(new Response(null, { status: 503 }));
    await expect(adapter.resetPin("owner@example.com", "123456", "12345")).rejects.toThrow("invalid or expired");
  });
});
