import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./app/App";
import { SessionProvider } from "./app/Session";
import { LanguageProvider } from "./localization/Language";
import "./styles.css";
import "./ui/responsive.css";
import "./features/admin/admin.css";
import "./ui/product.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <LanguageProvider>
        <SessionProvider>
          <App />
        </SessionProvider>
      </LanguageProvider>
    </BrowserRouter>
  </StrictMode>,
);
if (import.meta.env.PROD && "serviceWorker" in navigator)
  void navigator.serviceWorker.register("/sw.js", { updateViaCache: "none" }).then(registration => {
    // Activate a worker already waiting at this natural application load. A
    // worker found during an active session remains waiting, so no form,
    // upload, checkout, or financial action is interrupted by a forced reload.
    if (registration.waiting && navigator.serviceWorker.controller)
      registration.waiting.postMessage({ type: "ACTIVATE_UPDATE" });
    window.addEventListener("focus", () => { void registration.update().catch(() => {}); });
  }).catch(() => {
    /* Online behavior does not depend on installation support. */
  });
