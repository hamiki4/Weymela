import { getApps, initializeApp, type FirebaseApp, type FirebaseOptions } from "firebase/app";
import {
  getAuth,
  getIdToken,
  getRedirectResult,
  inMemoryPersistence,
  onAuthStateChanged,
  setPersistence,
  signInWithCustomToken,
  signOut as firebaseSignOut,
  type Auth,
  type User,
} from "firebase/auth";
import type { SessionProfile } from "../api/types";

export interface FirebasePublicConfig extends FirebaseOptions {
  apiKey: string;
  authDomain: string;
  projectId: string;
  appId: string;
}

export class FirebaseConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "FirebaseConfigurationError";
  }
}
export class ProfileSelectionRequiredError extends Error {
  constructor(public readonly profiles: SessionProfile[]) {
    super("Choose an approved profile to continue.");
    this.name = "ProfileSelectionRequiredError";
  }
}

type PublicEnv = Record<string, string | undefined>;

/** Reads only the public Firebase Web configuration. No service-account fields are accepted. */
export function readFirebasePublicConfig(env: PublicEnv = import.meta.env): FirebasePublicConfig {
  const values = {
    apiKey: env.VITE_FIREBASE_API_KEY,
    authDomain: env.VITE_FIREBASE_AUTH_DOMAIN,
    projectId: env.VITE_FIREBASE_PROJECT_ID,
    appId: env.VITE_FIREBASE_APP_ID,
  };
  const missing = Object.entries(values).filter(([, value]) => !value?.trim()).map(([key]) => key);
  if (missing.length > 0) {
    throw new FirebaseConfigurationError(`Firebase Web configuration is incomplete (${missing.join(", ")}).`);
  }
  return values as FirebasePublicConfig;
}

export function initializeFirebase(config: FirebasePublicConfig): { app: FirebaseApp; auth: Auth; persistenceReady: Promise<void> } {
  const app = getApps().find((candidate) => candidate.options.projectId === config.projectId)
    ?? initializeApp(config);
  const auth = getAuth(app);
  // The server-owned session cookie is authoritative; keep the Firebase refresh token in memory only.
  return { app, auth, persistenceReady: setPersistence(auth, inMemoryPersistence) };
}

export function createFirebaseWebAuthAdapter(): FirebaseWebAuthAdapter {
  const initialized = initializeFirebase(readFirebasePublicConfig());
  return new FirebaseWebAuthAdapter(initialized.auth, exchangeFirebaseToken, initialized.persistenceReady);
}

export async function exchangeFirebaseToken(idToken: string, profile?: SessionProfile): Promise<void> {
  const response = await fetch("/api/auth/firebase/session", {
    method: "POST",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json", "X-Weymela-Request": "1" },
    body: JSON.stringify({ idToken, profileRole: profile?.role, profileSubjectId: profile?.subjectId, profileBusinessId: profile?.businessId }),
  });
  if (!response.ok) {
    if (response.status === 409) {
      const body = await response.json().catch(() => null) as { code?: string; profiles?: SessionProfile[] } | null;
      if (body?.code === "ProfileSelectionRequired" && Array.isArray(body.profiles))
        throw new ProfileSelectionRequiredError(body.profiles);
    }
    throw new Error(response.status === 401 ? "Firebase sign-in was rejected." : "We could not sign you in. Please try again.");
  }
}

export type FirebaseTokenExchange = (idToken: string, profile?: SessionProfile) => Promise<void>;

/** Firebase is used after Weymela email verification, not as a phone/SMS login. */
export class FirebaseWebAuthAdapter {
  constructor(private readonly auth: Auth, private readonly exchange: FirebaseTokenExchange = exchangeFirebaseToken,
    private readonly persistenceReady: Promise<void> = Promise.resolve()) {}

  onUserChanged(callback: (user: User | null) => void): () => void {
    return onAuthStateChanged(this.auth, callback);
  }

  async signInWithCustomToken(customToken: string): Promise<void> {
    if (!customToken || customToken.length > 8192) throw new Error("The verification session is invalid.");
    await this.persistenceReady;
    const credential = await signInWithCustomToken(this.auth, customToken);
    await this.exchange(await getIdToken(credential.user, true));
  }

  async selectProfile(profile: SessionProfile): Promise<void> {
    await this.persistenceReady;
    if (!this.auth.currentUser) throw new Error("Your sign-in session has expired. Start again.");
    await this.exchange(await getIdToken(this.auth.currentUser, true), profile);
  }

  async completeRedirect(): Promise<boolean> {
    await this.persistenceReady;
    const result = await getRedirectResult(this.auth);
    if (!result) return false;
    await this.exchange(await getIdToken(result.user, true));
    return true;
  }

  async startEmailCode(identifier: string, purpose: "Signup" | "DeviceEnrollment" | "PinRecovery", phone?: string): Promise<void> {
    const normalized = normalizeLoginIdentifier(identifier);
    if (purpose === "Signup" && !normalized.includes("@")) throw new Error("Signup requires an email address and phone number.");
    if (purpose === "Signup" && !phone?.trim()) throw new Error("Phone number and email are required.");
    const response = await fetch("/api/auth/email/start", { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-Weymela-Request": "1" }, body: JSON.stringify({ identifier: normalized, phone: phone?.trim() || null, purpose }) });
    if (!response.ok) throw new Error(response.status === 429 ? "Too many attempts. Please wait and try again." : "Email verification is temporarily unavailable.");
  }

  async verifyEmailCode(identifier: string, purpose: "Signup" | "DeviceEnrollment" | "PinRecovery", code: string): Promise<void> {
    const normalized = normalizeLoginIdentifier(identifier);
    if (purpose === "Signup" && !normalized.includes("@")) throw new Error("The code is invalid or expired.");
    if (!/^\d{6}$/.test(code.trim())) throw new Error("The code is invalid or expired.");
    const response = await fetch("/api/auth/email/verify", { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-Weymela-Request": "1" }, body: JSON.stringify({ identifier: normalized, purpose, code: code.trim() }) });
    if (!response.ok) throw new Error("The code is invalid or expired.");
    const result = await response.json() as { customToken?: string };
    if (!result.customToken) throw new Error("The verification session is invalid.");
    await this.signInWithCustomToken(result.customToken);
  }

  async signOut(): Promise<void> {
    await this.persistenceReady;
    let firebaseFailure: unknown;
    try { await firebaseSignOut(this.auth); } catch (cause) { firebaseFailure = cause; }
    const response = await fetch("/api/session/sign-out", { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-Weymela-Request": "1" } });
    if (!response.ok && response.status !== 401) throw new Error("We could not sign you out. Please try again.");
    if (firebaseFailure) throw new Error("Firebase sign-out could not be completed.");
  }
}

function normalizeLoginIdentifier(identifier: string): string {
  const normalized = identifier.trim().toLowerCase();
  if (/^[^@\s]{1,96}@[^@\s]{1,96}$/.test(normalized)) return normalized;
  const phone = normalized.replace(/\s+/g, "");
  if (/^\+[1-9][0-9]{6,14}$/.test(phone)) return phone;
  throw new Error("Enter a registered phone number or email address.");
}
