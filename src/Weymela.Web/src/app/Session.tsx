import {
  createContext,
  startTransition,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { ApiError, post, request } from "../api/client";
import type { AccountSecurityStatus, DeviceAccessStatus, DeviceEnrollmentStatus, EmailCodeStartStatus, Role, SessionProfile, SessionUser, ViewAsSession } from "../api/types";
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
interface SessionContextValue {
  user: SessionUser | null;
  loading: boolean;
  loadFailed: boolean;
  deviceEnrollment: DeviceEnrollmentStatus | null;
  deviceAccess: DeviceAccessStatus | null;
  accountSecurity: AccountSecurityStatus | null;
  refresh: () => Promise<void>;
  enrollDevice: (pin: string, confirmPin: string) => Promise<void>;
  enrollPassword: (phone: string | null, password: string, confirmPassword: string) => Promise<void>;
  unlockDevice: (pin: string) => Promise<void>;
  startPinRecovery: (identifier: string) => Promise<EmailCodeStartStatus>;
  completePinRecovery: (identifier: string, code: string, newPin: string, confirmPin: string) => Promise<void>;
  signOut: () => Promise<void>;
  switchProfile: (profile: SessionProfile) => Promise<SessionUser>;
  viewAs: ViewAsState | null;
  viewAsNotice: string | null;
  startViewAs: (viewedUserId: string, displayName: string, returnTo: string) => Promise<ViewAsSession>;
  endViewAs: () => Promise<void>;
}
export interface ViewAsState {
  session: ViewAsSession;
  displayName: string;
  returnTo: string;
}
const viewAsDisplayNameKey = "weymela.view-as.display-name";
const viewAsReturnPathKey = "weymela.view-as.return-path";
const safeAdminReturnPath = (path: string | null | undefined) =>
  path && path.startsWith("/admin/accounts") ? path : "/admin/accounts";
const Context = createContext<SessionContextValue | null>(null);
export function SessionProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const [user, setUser] = useState<SessionUser | null>(null);
  const [deviceEnrollment, setDeviceEnrollment] = useState<DeviceEnrollmentStatus | null>(null);
  const [deviceAccess, setDeviceAccess] = useState<DeviceAccessStatus | null>(null);
  const [accountSecurity, setAccountSecurity] = useState<AccountSecurityStatus | null>(null);
  const accessState = useRef<DeviceAccessStatus["state"] | null>(null);
  const refreshGeneration = useRef(0);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [viewAs, setViewAs] = useState<ViewAsState | null>(null);
  const [viewAsNotice, setViewAsNotice] = useState<string | null>(null);
  const viewAsRef = useRef<ViewAsState | null>(null);
  const handlingViewAsExpiry = useRef(false);
  const pendingViewAsNavigation = useRef<string | null>(null);
  const updateViewAs = (next: ViewAsState | null) => {
    viewAsRef.current = next;
    setViewAs(next);
  };
  const clearViewAsMetadata = () => {
    window.sessionStorage.removeItem(viewAsDisplayNameKey);
    window.sessionStorage.removeItem(viewAsReturnPathKey);
  };
  const clearSessionState = () => {
    setUser(null);
    setDeviceEnrollment(null);
    setDeviceAccess(null);
    setAccountSecurity(null);
    accessState.current = null;
    window.sessionStorage.removeItem("weymela.profile-key");
    clearViewAsMetadata();
    updateViewAs(null);
  };
  const refresh = async () => {
    const generation = ++refreshGeneration.current;
    const isCurrent = () => refreshGeneration.current === generation;
    setLoading(true);
    setLoadFailed(false);
    try {
      let currentViewAs: ViewAsSession | null = null;
      const viewAsBootstrapHint = Boolean(
        viewAsRef.current
        || window.sessionStorage.getItem(viewAsDisplayNameKey)
        || window.sessionStorage.getItem(viewAsReturnPathKey),
      );
      if (viewAsBootstrapHint) {
        try {
          currentViewAs = await request<ViewAsSession | null>("/admin/view-as/current");
        } catch (error) {
          if (!(error instanceof ApiError && error.code === "InvalidViewAsSession")) throw error;
          clearViewAsMetadata();
          setViewAsNotice("Admin View Mode expired. You are back in the Platform Admin workspace.");
        }
      }
      if (!isCurrent()) return;
      let next: SessionUser;
      let security: AccountSecurityStatus | null;
      let enrollment: DeviceEnrollmentStatus | null;
      if (currentViewAs) {
        // B4 deliberately blocks device/security bootstrap reads while the
        // support session is active. The server-authoritative session and
        // workspace responses are sufficient to enter the existing workspace.
        next = await request<SessionUser>("/session");
        if (!isCurrent()) return;
        security = next.developmentMode
          ? { passwordEnrolled: true, phoneEnrolled: true }
          : accountSecurity;
        enrollment = deviceEnrollment;
      } else {
        const access = await request<DeviceAccessStatus>("/device/access");
        if (!isCurrent()) return;
        const previousAccess = accessState.current;
        accessState.current = access.state;
        setDeviceAccess(access);
        if (["Locked", "Cooldown", "RecoveryRequired", "FullAuthenticationRequired"].includes(access.state)
            && previousAccess !== access.state && typeof BroadcastChannel !== "undefined") {
          const channel = new BroadcastChannel("weymela-v3-access");
          channel.postMessage(access.state === "FullAuthenticationRequired" ? "full-authentication-required" : "locked");
          channel.close();
        }
        if (!["Unlocked", "EnrollmentRequired"].includes(access.state)) return;
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
        // An un-enrolled device cannot have an active View As session and the
        // B4 device gate intentionally rejects this control read. Defer it
        // until the normal workspace bootstrap is unlocked.
        if (access.state === "Unlocked") {
          try {
            currentViewAs = await request<ViewAsSession | null>("/admin/view-as/current");
          } catch (error) {
            if (!(error instanceof ApiError && error.code === "InvalidViewAsSession")) throw error;
            clearViewAsMetadata();
            setViewAsNotice("Admin View Mode expired. You are back in the Platform Admin workspace.");
          }
        }
      }
      if (!isCurrent()) return;
      if (currentViewAs) {
        const restored: ViewAsState = {
          session: currentViewAs,
          displayName: window.sessionStorage.getItem(viewAsDisplayNameKey) ?? "Viewed account",
          returnTo: safeAdminReturnPath(window.sessionStorage.getItem(viewAsReturnPathKey)),
        };
        window.sessionStorage.setItem(viewAsReturnPathKey, restored.returnTo);
        updateViewAs(restored);
      } else {
        updateViewAs(null);
        clearViewAsMetadata();
      }
      setUser(next);
      setAccountSecurity(security);
      if (enrollment) setDeviceEnrollment(enrollment);
      if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
    } catch (error) {
      if (!isCurrent()) return;
      clearSessionState();
      setLoadFailed(!(error instanceof ApiError && error.status === 401));
    } finally {
      if (isCurrent()) setLoading(false);
    }
  };
  useEffect(() => {
    void refresh();
  }, []);
  useEffect(() => {
    const target = pendingViewAsNavigation.current;
    if (!target || loading || !user) return;
    if (target.startsWith("/admin/accounts") && viewAs) return;
    if (!target.startsWith("/admin/accounts") && !viewAs) return;
    pendingViewAsNavigation.current = null;
    navigate(target, { replace: true });
  }, [loading, navigate, user, viewAs]);
  useEffect(() => {
    const expired = () => {
      if (handlingViewAsExpiry.current) return;
      handlingViewAsExpiry.current = true;
      const returnTo = safeAdminReturnPath(viewAsRef.current?.returnTo);
      updateViewAs(null);
      clearViewAsMetadata();
      setViewAsNotice("Admin View Mode expired. You are back in the Platform Admin workspace.");
      pendingViewAsNavigation.current = returnTo;
      void refresh().finally(() => { handlingViewAsExpiry.current = false; });
    };
    window.addEventListener("weymela-view-as-expired", expired);
    return () => window.removeEventListener("weymela-view-as-expired", expired);
  }, []);
  useEffect(() => {
    const accessChanged = () => void refresh();
    window.addEventListener("weymela-device-access", accessChanged);
    const channel = typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel("weymela-v3-access");
    if (channel) channel.onmessage = (event) => {
      if (event.data === "signed-out") {
        ++refreshGeneration.current;
        clearSessionState();
        setLoadFailed(false);
        setLoading(false);
      } else void refresh();
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
    setDeviceEnrollment(status);
    await refresh();
  };
  const enrollPassword = async (phone: string | null, password: string, confirmPassword: string) => {
    await post<void>("/account/password-credential", { phone, password, confirmPassword });
    await refresh();
  };
  const unlockDevice = async (pin: string) => {
    const status = await post<DeviceAccessStatus>("/device/unlock", { pin });
    accessState.current = status.state;
    setDeviceAccess(status);
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
    setDeviceAccess(status);
    await refresh();
    if (typeof BroadcastChannel !== "undefined") {
      const channel = new BroadcastChannel("weymela-v3-access");
      channel.postMessage("recovery-completed");
      channel.close();
    }
  };
  const switchProfile = async (profile: SessionProfile) => {
    const next = await post<SessionUser>("/session/switch-profile", {
      role: profile.role,
      subjectId: profile.subjectId,
      businessId: profile.businessId,
    });
    if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
    // BrowserRouter transitions location updates. Commit the authoritative role
    // in that same transition so the old route never sees the new role alone.
    startTransition(() => {
      setUser(next);
      navigate(roleHome[next.role], { replace: true });
      window.dispatchEvent(new Event("weymela-profile-switched"));
    });
    return next;
  };
  const startViewAs = async (viewedUserId: string, displayName: string, returnTo: string) => {
    const started = await post<ViewAsSession>("/admin/view-as/start", { viewedUserId });
    const next: ViewAsState = {
      session: started,
      displayName,
      returnTo: safeAdminReturnPath(returnTo),
    };
    window.sessionStorage.setItem(viewAsDisplayNameKey, displayName);
    window.sessionStorage.setItem(viewAsReturnPathKey, next.returnTo);
    setViewAsNotice(null);
    updateViewAs(next);
    pendingViewAsNavigation.current = roleHome[started.viewedRole];
    await refresh();
    return started;
  };
  const endViewAs = async () => {
    const returnTo = safeAdminReturnPath(viewAsRef.current?.returnTo);
    await post<void>("/admin/view-as/end", {});
    updateViewAs(null);
    clearViewAsMetadata();
    setViewAsNotice(null);
    pendingViewAsNavigation.current = returnTo;
    await refresh();
  };
  return (
    <Context.Provider value={{ user, loading, loadFailed, deviceEnrollment, deviceAccess, accountSecurity, refresh, enrollDevice, enrollPassword, unlockDevice, startPinRecovery, completePinRecovery, signOut, switchProfile, viewAs, viewAsNotice, startViewAs, endViewAs }}>
      {children}
    </Context.Provider>
  );
}
export function useSession() {
  const value = useContext(Context);
  if (!value) throw new Error("SessionProvider required");
  return value;
}
export function RoleGate({
  roles,
  children,
}: {
  roles: Role[];
  children: ReactNode;
}) {
  const { user, loading } = useSession();
  if (loading)
    return (
      <div className="loading" role="status">
        Opening your workspace…
      </div>
    );
  if (!user) return <Navigate to="/sign-in" replace />;
  if (!roles.includes(user.role))
    return <Navigate to="/unauthorized" replace />;
  return <>{children}</>;
}
