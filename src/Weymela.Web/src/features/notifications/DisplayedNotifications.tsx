import { useEffect } from "react";
import { notifyNotificationsChanged, request } from "../../api/client";

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A rendered, visible record is the acknowledgement boundary; prefetch is not. */
export function DisplayedNotifications({ profileKey, pathname }: { profileKey: string | null | undefined; pathname?: string }) {
  useEffect(() => {
    if (!profileKey || typeof IntersectionObserver === "undefined") return;
    const pending = new Set<string>();
    const acknowledged = new Map<string, number>();
    const visible = new Map<Element, string[]>();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let active = true;
    let routePending = false;
    const flush = () => {
      timer = undefined;
      const ids = [...pending].slice(0, 100);
      ids.forEach(id => pending.delete(id));
      const displayedPath = routePending ? pathname : undefined;
      routePending = false;
      if (!ids.length && !displayedPath || window.sessionStorage.getItem("weymela.profile-key") !== profileKey) return;
      void request("/notifications/read-related", { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({ displayedIds: ids, ...(displayedPath ? { displayedPath } : {}) }) }).then(() => {
        if (!active || window.sessionStorage.getItem("weymela.profile-key") !== profileKey) return;
        ids.forEach(id => acknowledged.set(id, Date.now()));
        notifyNotificationsChanged();
      }).catch(() => ids.forEach(id => acknowledged.delete(id)));
      if (pending.size) timer = setTimeout(flush, 100);
    };
    const observer = new IntersectionObserver(entries => {
      for (const entry of entries) {
        const ids = (entry.target.getAttribute("data-notification-resource") ?? "").split("|").filter(id => uuid.test(id));
        if (entry.isIntersecting && !entry.target.closest('[aria-busy="true"]')) {
          visible.set(entry.target, ids);
          if (!ids.length && pathname) routePending = true;
          for (const id of ids) if (Date.now() - (acknowledged.get(id) ?? 0) > 1000) pending.add(id);
        } else visible.delete(entry.target);
      }
      if ((pending.size || routePending) && !timer) timer = setTimeout(flush, 80);
    });
    const watched = new Map<Element, string>();
    const scan = () => document.querySelectorAll("[data-notification-resource]").forEach(element => {
      const identity = element.getAttribute("data-notification-resource") ?? "";
      if (element.closest('[aria-busy="true"]')) { observer.unobserve(element); watched.delete(element); visible.delete(element); return; }
      if (watched.get(element) !== identity) { observer.unobserve(element); watched.set(element, identity); observer.observe(element); }
    });
    const mutation = new MutationObserver(scan);
    mutation.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["data-notification-resource", "aria-busy"] });
    scan();
    const refresh = () => {
      for (const [element, ids] of visible) if (element.isConnected && !element.closest('[aria-busy="true"]')) { ids.forEach(id => pending.add(id)); if (!ids.length && pathname) routePending = true; }
      if ((pending.size || routePending) && !timer) timer = setTimeout(flush, 80);
    };
    const interval = setInterval(() => { if (document.visibilityState === "visible") refresh(); }, 15000);
    window.addEventListener("focus", refresh);
    return () => { active = false; if (timer) clearTimeout(timer); clearInterval(interval); window.removeEventListener("focus", refresh); observer.disconnect(); mutation.disconnect(); };
  }, [profileKey, pathname]);
  return null;
}
