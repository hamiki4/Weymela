import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./app/App";
import { SessionProvider } from "./app/Session";
import "./styles.css";
import "./ui/responsive.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <SessionProvider>
        <App />
      </SessionProvider>
    </BrowserRouter>
  </StrictMode>,
);
if (import.meta.env.PROD && "serviceWorker" in navigator)
  void navigator.serviceWorker.register("/sw.js", { updateViaCache: "none" }).then(registration => {
    const announce = () => { if (registration.waiting && navigator.serviceWorker.controller) window.dispatchEvent(new CustomEvent("weymela-update", { detail: registration.waiting })); };
    announce();
    registration.addEventListener("updatefound", () => registration.installing?.addEventListener("statechange", announce));
    window.addEventListener("focus", () => { void registration.update().catch(() => {}); announce(); });
  }).catch(() => {
    /* Online behavior does not depend on installation support. */
  });
