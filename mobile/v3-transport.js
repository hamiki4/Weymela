/* Android-only transport. The immutable V3 Web client and its security gates are unchanged. */
(function (scope) {
  "use strict";
  const apiOrigin = "https://api.weymela.com";

  function apiUrl(input, origin) {
    const url = new URL(input instanceof Request ? input.url : String(input), origin);
    if (url.origin !== origin || !url.pathname.startsWith("/api/")) return null;
    if (url.pathname.toLowerCase().startsWith("/api/v1/")) throw new Error("Legacy API is unavailable in Weymela V3.");
    if (url.pathname.includes("%")) throw new Error("Unsupported API path.");
    return apiOrigin + url.pathname + url.search;
  }

  function base64(bytes) {
    let text = "";
    for (let i = 0; i < bytes.length; i += 32768)
      text += String.fromCharCode(...bytes.subarray(i, i + 32768));
    return btoa(text);
  }

  async function serialize(body) {
    if (body === undefined || body === null) return {};
    if (typeof body === "string") return { data: body };
    if (body instanceof FormData) {
      const data = [];
      for (const [key, value] of body.entries()) {
        if (typeof value === "string") data.push({ key, value, type: "string" });
        else data.push({ key, value: base64(new Uint8Array(await value.arrayBuffer())), type: "base64File", contentType: value.type, fileName: value.name });
      }
      return { data, dataType: "formData" };
    }
    throw new Error("Unsupported API request body.");
  }

  function install(target) {
    const cap = target.Capacitor;
    if (!cap || cap.getPlatform() !== "android") return;
    const browserFetch = target.fetch.bind(target);
    target.fetch = async (input, options = {}) => {
      const url = apiUrl(input, target.location.origin);
      if (!url) return browserFetch(input, options);
      if (input instanceof Request) throw new Error("Use the Weymela API client for native requests.");
      if (options.signal?.aborted) throw new DOMException("Request aborted", "AbortError");
      const method = (options.method || "GET").toUpperCase();
      if (method !== "GET" && method !== "POST") throw new Error("Unsupported API method.");
      const headers = Object.fromEntries(new Headers(options.headers).entries());
      const payload = await serialize(options.body);
      if (payload.dataType === "formData") headers["content-type"] = "multipart/form-data";
      if (options.signal?.aborted) throw new DOMException("Request aborted", "AbortError");
      const pending = cap.nativePromise("WeymelaApi", "request", { url, method, headers, ...payload });
      let abort;
      const aborted = options.signal && new Promise((_, reject) => {
        abort = () => reject(new DOMException("Request aborted", "AbortError"));
        options.signal.addEventListener("abort", abort, { once: true });
        if (options.signal.aborted) abort();
      });
      let result;
      try { result = await (aborted ? Promise.race([pending, aborted]) : pending); }
      finally { if (abort) options.signal.removeEventListener("abort", abort); }
      const safeHeaders = new Headers();
      for (const [key, value] of Object.entries(result.headers || {}))
        if (!["set-cookie", "set-cookie2", "cookie", "authorization"].includes(key.toLowerCase())) safeHeaders.set(key, value);
      const json = (safeHeaders.get("content-type") || "").toLowerCase().includes("json");
      const empty = result.status === 204 || result.status === 205 || result.status === 304;
      const body = empty ? null : json ? JSON.stringify(result.data) : Uint8Array.from(atob(result.data || ""), c => c.charCodeAt(0));
      return new Response(body, { status: result.status, headers: safeHeaders });
    };
  }
  scope.WeymelaAndroidTransport = Object.freeze({ apiUrl, serialize, install });
  if (typeof window !== "undefined") install(window);
})(globalThis);
