import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { Navigate } from "react-router-dom";
import { post, request } from "../api/client";
import type { Role, SessionProfile, SessionUser } from "../api/types";
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
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
  switchProfile: (profile: SessionProfile) => Promise<SessionUser>;
}
const Context = createContext<SessionContextValue | null>(null);
export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null);
  const [loading, setLoading] = useState(true);
  const refresh = async () => {
    try {
      const next = await request<SessionUser>("/session");
      setUser(next);
      if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
    } catch {
      setUser(null);
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
      window.sessionStorage.removeItem("weymela.profile-key");
    }
  };
  const switchProfile = async (profile: SessionProfile) => {
    const next = await post<SessionUser>("/session/switch-profile", {
      role: profile.role,
      subjectId: profile.subjectId,
      businessId: profile.businessId,
    });
    setUser(next);
    if (next.activeProfileKey) window.sessionStorage.setItem("weymela.profile-key", next.activeProfileKey);
    window.dispatchEvent(new Event("weymela-profile-switched"));
    return next;
  };
  return (
    <Context.Provider value={{ user, loading, refresh, signOut, switchProfile }}>
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
