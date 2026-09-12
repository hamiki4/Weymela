// Release identity is replaced after the production build. Only the anonymous offline document is cached.
const CACHE = "weymela-static-__BUILD_ID__";
self.addEventListener("install", event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(["/offline.html", "/offline.css"]))));
self.addEventListener("message", event => { if (event.data?.type === "ACTIVATE_UPDATE") self.skipWaiting(); });
self.addEventListener("activate", event => event.waitUntil((async () => {
  for (const key of await caches.keys()) if (key.startsWith("weymela-static-") && key !== CACHE) await caches.delete(key);
  await self.clients.claim();
})()));
self.addEventListener("fetch", event => {
  if (event.request.method === "GET" && new URL(event.request.url).origin === self.location.origin && new URL(event.request.url).pathname === "/offline.css") { event.respondWith(caches.match("/offline.css").then(cached => cached || fetch(event.request))); return; }
  // No APIs, tokens, account pages, QR responses, POSTs, or offline write queue are persisted.
  if (event.request.method !== "GET" || event.request.mode !== "navigate" || new URL(event.request.url).origin !== self.location.origin || new URL(event.request.url).pathname.startsWith("/api/")) return;
  event.respondWith(fetch(event.request).catch(async () => (await caches.match("/offline.html")) || Response.error()));
});
