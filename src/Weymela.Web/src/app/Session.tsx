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
import { post, request } from "../api/client";
import type { DeviceAccessStatus, DeviceEnrollmentStatus, EmailCodeStartStatus, Role, SessionProfile, SessionUser } from "../api/types";
import { createFirebaseWebAuthAdapter, FirebaseConfigurationError } from "../auth/firebase";

export const roleHome: Record<Role, string> = {
  Business: "/business",
  Creator: "/creator",
  PlatformAdmin: "/admin",
  Customer: "/customer/offers",
  Cashier: "/checkout",
  Onboarding: "/onboarding",
};
interface SessionContextValue {
  user: SessionUser | null;
  loading: boolean;
  deviceEnrollment: DeviceEnrollmentStatus | null;
  deviceAccess: DeviceAccessStatus | null;
  refresh: () => Promise<void>;
  enrollDevice: (pin: string, confirmPin: string) => Promise<void>;
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
  const accessState = useRef<DeviceAccessStatus["state"] | null>(null);
  const [loading, setLoading] = useState(true);
  const refresh = async () => {
    setLoading(true);
    try {
      const access = await request<DeviceAccessStatus>("/device/access");
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
      const next = await request<SessionUser>("/session");
      let enrollment: DeviceEnrollmentStatus;
      try {
        enrollment = await request<DeviceEnrollmentStatus>("/device/enrollment");
      } catch {
        // Authenticated workspaces fail closed when device state cannot be read.
        enrollment = { state: "Unavailable", expiresAtUtc: null };
      }
      setUser(next);
      setDeviceEnrollment(enrollment);
      if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
    } catch {
      setUser(null);
      setDeviceEnrollment(null);
      setDeviceAccess(null);
      accessState.current = null;
      window.sessionStorage.removeItem("weymela.profile-key");
    } finally {
      setLoading(false);
    }
  };
  useEffect(() => {
    void refresh();
  }, []);
  useEffect(() => {
    const accessChanged = () => void refresh();
    window.addEventListener("weymela-device-access", accessChanged);
    const channel = typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel("weymela-v3-access");
    if (channel) channel.onmessage = () => void refresh();
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
    try {
      await createFirebaseWebAuthAdapter().signOut();
    } catch (error) {
      if (error instanceof FirebaseConfigurationError) await post("/session/sign-out");
      else throw error;
    } finally {
      setUser(null);
      setDeviceEnrollment(null);
      setDeviceAccess(null);
      accessState.current = null;
      window.sessionStorage.removeItem("weymela.profile-key");
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
  return (
    <Context.Provider value={{ user, loading, deviceEnrollment, deviceAccess, refresh, enrollDevice, unlockDevice, startPinRecovery, completePinRecovery, signOut, switchProfile }}>
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
