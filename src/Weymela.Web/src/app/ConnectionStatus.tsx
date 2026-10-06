import { useEffect, useState } from "react";
import { Notice } from "../ui/components";

export function ConnectionStatus() {
  const [online, setOnline] = useState(navigator.onLine);
  useEffect(() => {
    const changed = () => setOnline(navigator.onLine);
    window.addEventListener("online", changed); window.addEventListener("offline", changed);
    return () => { window.removeEventListener("online", changed); window.removeEventListener("offline", changed); };
  }, []);
  return <>{!online && <Notice error>You’re offline. Displayed information may be out of date. Reconnect before using QR, payments or other actions. Nothing is queued.</Notice>}</>;
}
