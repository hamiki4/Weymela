/** Strict syntactic allowlist only; short links are never fetched or treated as verified publication. */
export function normalizeTikTokVideoLink(value: string): string | null {
  const raw = value.trim();
  if (!raw || raw.length > 1000) return null;
  let url: URL;
  try { url = new URL(raw); } catch { return null; }
  if (url.protocol !== "https:" || url.username || url.password || url.port || url.search || url.hash) return null;
  const host = url.hostname.toLowerCase();
  if (["www.tiktok.com", "tiktok.com", "m.tiktok.com"].includes(host)) {
    const canonical = url.pathname.match(/^\/@([A-Za-z0-9._-]{1,50})\/video\/([0-9]{6,30})\/?$/);
    if (canonical) return `https://www.tiktok.com/@${canonical[1]}/video/${canonical[2]}`;
    const short = url.pathname.match(/^\/t\/([A-Za-z0-9_-]{6,80})\/?$/);
    return short ? `https://www.tiktok.com/t/${short[1]}` : null;
  }
  if (["vm.tiktok.com", "vt.tiktok.com"].includes(host)) {
    const short = url.pathname.match(/^\/([A-Za-z0-9_-]{6,80})\/?$/);
    return short ? `https://${host}/${short[1]}` : null;
  }
  return null;
}
