import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { Navigate } from "react-router-dom";
import { post, request } from "../api/client";
import type { Role, SessionUser } from "../api/types";

export const roleHome: Record<Role, string> = {
  Business: "/business",
  Creator: "/creator",
  PlatformAdmin: "/admin",
  Customer: "/customer/offers",
  Cashier: "/checkout",
};
interface SessionContextValue {
  user: SessionUser | null;
  loading: boolean;
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}
const Context = createContext<SessionContextValue | null>(null);
export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null);
  const [loading, setLoading] = useState(true);
  const refresh = async () => {
    try {
      setUser(await request<SessionUser>("/session"));
    } catch {
      setUser(null);
    } finally {
      setLoading(false);
    }
  };
  useEffect(() => {
    void refresh();
  }, []);
  const signOut = async () => {
    await post("/session/sign-out");
    setUser(null);
  };
  return (
    <Context.Provider value={{ user, loading, refresh, signOut }}>
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
