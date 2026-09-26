import {
  createContext,
  startTransition,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { Navigate, Outlet, useNavigate } from "react-router-dom";
import { ApiError, invalidateResourceCache, post, request } from "../api/client";
import type { AccountSecurityStatus, DeviceAccessStatus, DeviceEnrollmentStatus, EmailCodeStartStatus, Role, SessionProfile, SessionUser } from "../api/types";
import { createFirebaseWebAuthAdapter, FirebaseConfigurationError } from "../auth/firebase";

export const roleHome: Record<Role, string> = {
  Business: "/business",
  Creator: "/creator",
  PlatformAdmin: "/admin",
  OperationsAdmin: "/admin/operations",
  Customer: "/customer/offers",
  Cashier: "/checkout",
  Onboarding: "/onboarding",
};
export type SessionResolution = "resolving" | "anonymous" | "authenticated" | "failed";
interface SessionContextValue {
  user: SessionUser | null;
  resolution: SessionResolution;
  confirmedAnonymous: boolean;
  loading: boolean;
  loadFailed: boolean;
  deviceEnrollment: DeviceEnrollmentStatus | null;
  deviceAccess: DeviceAccessStatus | null;
  accountSecurity: AccountSecurityStatus | null;
  refresh: (propagateError?: boolean) => Promise<void>;
  enrollDevice: (pin: string, confirmPin: string) => Promise<void>;
  enrollPassword: (phone: string | null, password: string, confirmPassword: string) => Promise<void>;
  unlockDevice: (pin: string) => Promise<void>;
  startPinRecovery: (identifier: string) => Promise<EmailCodeStartStatus>;
  completePinRecovery: (identifier: string, code: string, newPin: string, confirmPin: string) => Promise<void>;
  signOut: () => Promise<void>;
  switchProfile: (profile: SessionProfile) => Promise<SessionUser>;
}
const Context = createContext<SessionContextValue | null>(null);
export function SessionProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const [user, setUser] = useState<SessionUser | null>(null);
  const [deviceEnrollment, setDeviceEnrollment] = useState<DeviceEnrollmentStatus | null>(null);
  const [deviceAccess, setDeviceAccess] = useState<DeviceAccessStatus | null>(null);
  const [accountSecurity, setAccountSecurity] = useState<AccountSecurityStatus | null>(null);
  const accessState = useRef<DeviceAccessStatus["state"] | null>(null);
  const refreshGeneration = useRef(0);
  const switching = useRef(false);
  const contextKey = useRef<string | null>(null);
  // `user === null` is ambiguous until the authoritative bootstrap finishes.
  // Keep that unresolved state explicit so routes cannot mistake it for an
  // unauthenticated session and render Sign In prematurely.
  const [resolution, setResolution] = useState<SessionResolution>("resolving");
  const [confirmedAnonymous, setConfirmedAnonymous] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const clearSessionState = () => {
    invalidateResourceCache();
    setUser(null);
    setDeviceEnrollment(null);
    setDeviceAccess(null);
    setAccountSecurity(null);
    accessState.current = null;
    contextKey.current = null;
    window.sessionStorage.removeItem("weymela.profile-key");
  };
  const refresh = async (propagateError = false) => {
    const generation = ++refreshGeneration.current;
    const isCurrent = () => refreshGeneration.current === generation;
    setResolution("resolving");
    setLoading(true);
    setLoadFailed(false);
    try {
      let next: SessionUser;
      let security: AccountSecurityStatus | null;
      let enrollment: DeviceEnrollmentStatus | null;
      const access = await request<DeviceAccessStatus>("/device/access");
        if (!isCurrent()) return;
        const previousAccess = accessState.current;
        accessState.current = access.state;
        if (["Locked", "Cooldown", "RecoveryRequired", "FullAuthenticationRequired"].includes(access.state)
            && previousAccess !== access.state && typeof BroadcastChannel !== "undefined") {
          const channel = new BroadcastChannel("weymela-v3-access");
          channel.postMessage(access.state === "FullAuthenticationRequired" ? "full-authentication-required" : "locked");
          channel.close();
        }
        if (!["Unlocked", "EnrollmentRequired"].includes(access.state)) {
          setDeviceAccess(access);
          setResolution(user ? "authenticated" : "anonymous");
          return;
        }
        next = await request<SessionUser>("/session");
        if (!isCurrent()) return;
        security = next.developmentMode
          ? { passwordEnrolled: true, phoneEnrolled: true }
          : await request<AccountSecurityStatus>("/account/security");
        if (!isCurrent()) return;
        try {
          enrollment = await request<DeviceEnrollmentStatus>("/device/enrollment");
        } catch {
          // Authenticated workspaces fail closed when device state cannot be read.
          enrollment = { state: "Unavailable", expiresAtUtc: null };
        }
        if (!isCurrent()) return;
      const nextContext = JSON.stringify([next.role, next.publicId, next.activeProfileKey ?? null]);
      if (contextKey.current !== nextContext) invalidateResourceCache();
      contextKey.current = nextContext;
      setUser(next);
      setDeviceAccess(access);
      setAccountSecurity(security);
      if (enrollment) setDeviceEnrollment(enrollment);
      if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
      setResolution("authenticated");
      setConfirmedAnonymous(false);
    } catch (error) {
      if (!isCurrent()) return;
      clearSessionState();
      const unauthenticated = error instanceof ApiError && error.status === 401;
      setConfirmedAnonymous(unauthenticated);
      setLoadFailed(!unauthenticated);
      setResolution(unauthenticated ? "anonymous" : "failed");
      if (propagateError) throw error;
    } finally {
      if (isCurrent()) setLoading(false);
    }
  };
  useEffect(() => {
    void refresh();
  }, []);
  useEffect(() => {
    const accessChanged = (event: Event) => {
      const code = (event as CustomEvent<{ state?: string }>).detail?.state;
      const state = code === "PinCooldown" ? "Cooldown"
        : code === "PinRecoveryRequired" ? "RecoveryRequired"
        : code === "FullAuthenticationRequired" ? "FullAuthenticationRequired"
        : "Locked";
      setDeviceAccess({ state, idleExpiresAtUtc: null, sessionExpiresAtUtc: null, retryAfterSeconds: null });
      void refresh();
    };
    window.addEventListener("weymela-device-access", accessChanged);
    const channel = typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel("weymela-v3-access");
    if (channel) channel.onmessage = (event) => {
      if (event.data === "signed-out") {
        ++refreshGeneration.current;
        clearSessionState();
        setLoadFailed(false);
        setResolution("anonymous");
        setConfirmedAnonymous(true);
        setLoading(false);
      } else {
        if (event.data === "locked" || event.data === "full-authentication-required")
          setDeviceAccess({ state: event.data === "locked" ? "Locked" : "FullAuthenticationRequired",
            idleExpiresAtUtc: null, sessionExpiresAtUtc: null, retryAfterSeconds: null });
        void refresh();
      }
    };
    return () => {
      window.removeEventListener("weymela-device-access", accessChanged);
      channel?.close();
    };
  }, []);
  useEffect(() => {
    if (deviceAccess?.state !== "Unlocked" || !deviceAccess.idleExpiresAtUtc) return;
    const delay = Math.max(0, new Date(deviceAccess.idleExpiresAtUtc).getTime() - Date.now());
    const timer = window.setTimeout(() => void refresh(), Math.min(delay + 50, 2_147_000_000));
    return () => window.clearTimeout(timer);
  }, [deviceAccess?.state, deviceAccess?.idleExpiresAtUtc]);
  const signOut = async () => {
    // Invalidate every in-flight refresh before beginning intentional logout.
    ++refreshGeneration.current;
    invalidateResourceCache();
    setResolution("resolving");
    setLoading(true);
    try {
      await createFirebaseWebAuthAdapter().signOut();
    } catch (error) {
      if (error instanceof FirebaseConfigurationError) await post("/session/sign-out");
      else throw error;
    } finally {
      // Also invalidate a refresh that may have started while remote sign-out
      // was in progress; logout remains the final authoritative transition.
      ++refreshGeneration.current;
      clearSessionState();
      setLoadFailed(false);
      setResolution("anonymous");
      setConfirmedAnonymous(true);
      setLoading(false);
      if (typeof BroadcastChannel !== "undefined") {
        const channel = new BroadcastChannel("weymela-v3-access");
        channel.postMessage("signed-out");
        channel.close();
      }
    }
  };
  const enrollDevice = async (pin: string, confirmPin: string) => {
    const status = await post<DeviceEnrollmentStatus>("/device/enrollment", { pin, confirmPin });
    if (status.state !== "Enrolled") throw new Error("Secure device setup did not complete.");
    await refresh();
  };
  const enrollPassword = async (phone: string | null, password: string, confirmPassword: string) => {
    await post<void>("/account/password-credential", { phone, password, confirmPassword });
    await refresh();
  };
  const unlockDevice = async (pin: string) => {
    const status = await post<DeviceAccessStatus>("/device/unlock", { pin });
    accessState.current = status.state;
    await refresh();
    if (typeof BroadcastChannel !== "undefined") {
      const channel = new BroadcastChannel("weymela-v3-access");
      channel.postMessage("unlocked");
      channel.close();
    }
  };
  const startPinRecovery = (identifier: string) => request<EmailCodeStartStatus>("/auth/email/start", {
    method: "POST",
    body: JSON.stringify({ identifier, phone: null, purpose: "PinRecovery" }),
  });
  const completePinRecovery = async (identifier: string, code: string, newPin: string, confirmPin: string) => {
    const status = await post<DeviceAccessStatus>("/device/pin-recovery/complete", {
      identifier, code, newPin, confirmPin,
    });
    if (status.state !== "Unlocked") throw new Error("Secure PIN recovery did not complete.");
    accessState.current = status.state;
    await refresh();
    if (typeof BroadcastChannel !== "undefined") {
      const channel = new BroadcastChannel("weymela-v3-access");
      channel.postMessage("recovery-completed");
      channel.close();
    }
  };
  const switchProfile = async (profile: SessionProfile) => {
    if (switching.current) throw new Error("A profile switch is already in progress.");
    switching.current = true;
    try {
      const next = await post<SessionUser>("/session/switch-profile", {
        role: profile.role,
        subjectId: profile.subjectId,
        businessId: profile.businessId,
      });
      ++refreshGeneration.current;
      // The server response is authoritative. Commit role and route together.
      startTransition(() => {
        if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
        // The old page unmounts with this route change. A synchronous cache
        // notification would repaint it as a skeleton before the new route commits.
        invalidateResourceCache(false);
        contextKey.current = JSON.stringify([next.role, next.publicId, next.activeProfileKey ?? null]);
        setUser(next);
        setResolution("authenticated");
        setLoading(false);
        setLoadFailed(false);
        navigate(roleHome[next.role], { replace: true });
      });
      return next;
    } finally {
      switching.current = false;
    }
  };
  return (
    <Context.Provider value={{ user, resolution, confirmedAnonymous, loading, loadFailed, deviceEnrollment, deviceAccess, accountSecurity, refresh, enrollDevice, enrollPassword, unlockDevice, startPinRecovery, completePinRecovery, signOut, switchProfile }}>
      {children}
    </Context.Provider>
  );
}
export function useSession() {
  const value = useContext(Context);
  if (!value) throw new Error("SessionProvider required");
  return value;
}
export function SessionTransition() {
  return (
    <main className="sign-in">
      <div className="sign-in-story">
        <span className="brand" role="img" aria-label="Weymela">
          <img className="brand-mark" src="/brand/weymela-mark.png" width="38" height="38" alt="" />
          <img className="brand-wordmark" src="/brand/weymela-wordmark.png" width="154" height="36" alt="" />
        </span>
      </div>
      <div className="sign-in-form">
        <div className="sign-in-secure" role="status" aria-live="polite">Preparing your secure session…</div>
      </div>
    </main>
  );
}
export function SessionLoadFailure({ retry }: { retry: () => Promise<void> }) {
  return (
    <main className="pin-setup-page">
      <section className="pin-setup-card" aria-labelledby="session-load-error-title">
        <h1 id="session-load-error-title">We couldn’t verify your session.</h1>
        <p className="muted">Your account is not being opened until its security state can be confirmed.</p>
        <button type="button" className="button primary" onClick={() => void retry()}>Try again</button>
      </section>
    </main>
  );
}
export function RoleGate({
  roles,
  children,
}: {
  roles: Role[];
  children?: ReactNode;
}) {
  const { user, loading, resolution } = useSession();
  if (!user && (loading || resolution === "resolving"))
    return <div role="status" aria-live="polite">Checking your secure session…</div>;
  if (!user) return <Navigate to="/sign-in" replace />;
  if (!roles.includes(user.role))
    return <Navigate to="/unauthorized" replace />;
  return children ? <>{children}</> : <Outlet />;
}
