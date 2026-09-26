import { useLayoutEffect } from "react";
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

/** The account gate owns the only redirect after authoritative checks finish. */
export function AccountRedirect({ to }: { to: string }) {
  const navigate = useNavigate();
  useLayoutEffect(() => { void navigate(to, { replace: true, flushSync: true }); }, [navigate, to]);
  return null;
}
