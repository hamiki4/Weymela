import { useState } from "react";
import { Button, Notice } from "../ui/components";
import type { ViewAsRole } from "../api/types";
import { useSession } from "./Session";

export function viewAsRoleLabel(role: string) {
  return role === "OperationsAdmin" ? "Operations Admin" : role;
}

export function ViewAsEntry({
  viewedUserId,
  displayName,
  role,
  returnTo,
}: {
  viewedUserId: string;
  displayName: string;
  role: string;
  returnTo: string;
}) {
  const { startViewAs } = useSession();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const supported = ["Customer", "Creator", "Business", "OperationsAdmin"].includes(role);
  if (!supported) return null;

  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      await startViewAs(viewedUserId, displayName, returnTo);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "We could not start Admin View Mode.");
      setBusy(false);
    }
  };

  return (
    <>
      <Button onClick={() => { setError(null); setOpen(true); }}>
        View As
      </Button>
      {open && (
        <div className="view-as-confirmation" role="dialog" aria-modal="true" aria-labelledby="view-as-confirmation-title">
          <div className="view-as-confirmation-card">
            <h2 id="view-as-confirmation-title">View as {displayName}?</h2>
            <p>You will enter a read-only Admin View of this {viewAsRoleLabel(role)} account.</p>
            {error && <Notice error>{error}</Notice>}
            <div className="actions">
              <Button variant="secondary" disabled={busy} onClick={() => setOpen(false)}>Cancel</Button>
              <Button disabled={busy} onClick={() => void start()}>{busy ? "Opening…" : "View As"}</Button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

export function ViewAsBanner() {
  const { viewAs, viewAsNotice, endViewAs } = useSession();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (!viewAs && !viewAsNotice) return null;
  if (!viewAs) return <div className="view-as-notice" role="alert">{viewAsNotice}</div>;

  const end = async () => {
    setBusy(true);
    setError(null);
    try {
      await endViewAs();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "We could not exit Admin View Mode.");
      setBusy(false);
    }
  };

  return (
    <aside className="view-as-banner" aria-label="Admin View Mode">
      <div className="view-as-banner-copy">
        <strong>ADMIN VIEW MODE</strong>
        <span>Viewing: {viewAs.displayName}</span>
        <span>Role: {viewAsRoleLabel(viewAs.session.viewedRole as ViewAsRole)}</span>
        <span className="view-as-read-only">Read-only</span>
      </div>
      <div className="view-as-banner-action">
        <Button variant="secondary" disabled={busy} onClick={() => void end()}>
          {busy ? "Exiting…" : "Exit View Mode"}
        </Button>
        {error && <small role="alert">{error}</small>}
      </div>
    </aside>
  );
}
