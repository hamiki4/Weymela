import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import type { AccountSecurityStatus, DeviceEnrollmentStatus, SessionUser } from "../api/types";
import { roleHome } from "./Session";

export function accountEntryPath(
  user: SessionUser,
  security: AccountSecurityStatus,
  enrollment: DeviceEnrollmentStatus | null,
) {
  if (!security.passwordEnrolled) return "/security-setup";
  if (!enrollment || !["Enrolled", "NotRequired"].includes(enrollment.state))
    return "/pin-setup";
  return roleHome[user.role];
}

/** Keeps account-state transitions visible while React Router commits the new route. */
export function AccountRedirect({ to }: { to: string }) {
  const navigate = useNavigate();
  useEffect(() => { void navigate(to, { replace: true }); }, [navigate, to]);
  return <main className="pin-setup-page">
    <section className="pin-setup-card" role="status" aria-live="polite">
      Opening your account…
    </section>
  </main>;
}
