import {
  createContext,
  startTransition,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { post, request } from "../api/client";
import type { DeviceEnrollmentStatus, Role, SessionProfile, SessionUser } from "../api/types";
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
  refresh: () => Promise<void>;
  enrollDevice: (pin: string, confirmPin: string) => Promise<void>;
  signOut: () => Promise<void>;
  switchProfile: (profile: SessionProfile) => Promise<SessionUser>;
}
const Context = createContext<SessionContextValue | null>(null);
export function SessionProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const [user, setUser] = useState<SessionUser | null>(null);
  const [deviceEnrollment, setDeviceEnrollment] = useState<DeviceEnrollmentStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const refresh = async () => {
    setLoading(true);
    try {
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
      window.sessionStorage.removeItem("weymela.profile-key");
    } finally {
      setLoading(false);
    }
  };
  useEffect(() => {
    void refresh();
  }, []);
  const signOut = async () => {
    try {
      await createFirebaseWebAuthAdapter().signOut();
    } catch (error) {
      if (error instanceof FirebaseConfigurationError) await post("/session/sign-out");
      else throw error;
    } finally {
      setUser(null);
      setDeviceEnrollment(null);
      window.sessionStorage.removeItem("weymela.profile-key");
    }
  };
  const enrollDevice = async (pin: string, confirmPin: string) => {
    const status = await post<DeviceEnrollmentStatus>("/device/enrollment", { pin, confirmPin });
    if (status.state !== "Enrolled") throw new Error("Secure device setup did not complete.");
    setDeviceEnrollment(status);
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
    <Context.Provider value={{ user, loading, deviceEnrollment, refresh, enrollDevice, signOut, switchProfile }}>
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
