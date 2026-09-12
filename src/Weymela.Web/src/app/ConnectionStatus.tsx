import { useEffect, useState } from "react";
import { Button, Notice } from "../ui/components";

export function ConnectionStatus() {
  const [online, setOnline] = useState(navigator.onLine);
  const [update, setUpdate] = useState<ServiceWorker | null>(null);
  useEffect(() => {
    const changed = () => setOnline(navigator.onLine);
    const ready = (event: Event) => setUpdate((event as CustomEvent<ServiceWorker>).detail);
    window.addEventListener("online", changed); window.addEventListener("offline", changed);
    window.addEventListener("weymela-update", ready);
    return () => { window.removeEventListener("online", changed); window.removeEventListener("offline", changed); window.removeEventListener("weymela-update", ready); };
  }, []);
  return <>{!online && <Notice error>You’re offline. Displayed information may be out of date. Reconnect before using QR, payments or other actions. Nothing is queued.</Notice>}
    {update && <Notice>A Weymela update is ready. Finish your current action before reloading. <Button variant="secondary" onClick={() => {
      navigator.serviceWorker.addEventListener("controllerchange", () => window.location.reload(), { once: true });
      update.postMessage({ type: "ACTIVATE_UPDATE" });
    }}>Reload when ready</Button></Notice>}</>;
}
