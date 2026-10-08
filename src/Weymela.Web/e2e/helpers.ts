import { expect, type BrowserContext, type Page } from "@playwright/test";
import QRCode from "qrcode";
import { readFileSync, mkdirSync } from "node:fs";
import { resolve } from "node:path";
const control = JSON.parse(
  readFileSync(resolve("../../.artifacts/browser-host.json"), "utf8"),
) as { accessKey: string };
export const evidence = resolve("../../.artifacts/phase6-screenshots");
mkdirSync(evidence, { recursive: true });
const cameraInstallOrder = new WeakMap<BrowserContext, number>();
const requestHeaders = { "X-Weymela-Request": "1" };
export const reviewMp4 = Buffer.from([
  0, 0, 0, 12,
  "f".charCodeAt(0), "t".charCodeAt(0), "y".charCodeAt(0), "p".charCodeAt(0),
  "i".charCodeAt(0), "s".charCodeAt(0), "o".charCodeAt(0), "m".charCodeAt(0),
]);

async function apiJson<T>(context: BrowserContext, path: string): Promise<T> {
  const response = await context.request.get(`/api${path}`, { headers: requestHeaders });
  if (!response.ok()) throw new Error(`${path}: ${response.status()} ${await response.text()}`);
  return await response.json() as T;
}

async function apiPost<T>(context: BrowserContext, path: string, data?: unknown): Promise<T> {
  const response = await context.request.post(`/api${path}`, {
    headers: { ...requestHeaders, "Idempotency-Key": crypto.randomUUID() },
    data,
  });
  if (!response.ok()) throw new Error(`${path}: ${response.status()} ${await response.text()}`);
  return await response.json() as T;
}

export async function login(context: BrowserContext, alias: string) {
  const response = await context.request.post("/api/development/session", {
    headers: { "X-Weymela-Request": "1" },
    data: { alias, accessKey: control.accessKey },
  });
  expect(response.status()).toBe(204);
}

export async function ensureBusinessFunds(context: BrowserContext, minimum: number) {
  const wallet = await apiJson<{ available: number; version: number }>(context, "/business/wallet");
  if (wallet.available >= minimum) return;
  await apiPost(context, "/business/wallet/deposits", {
    amount: minimum - wallet.available + 1000,
    expectedVersion: wallet.version,
  });
}

export async function saveFundPostAndOpenPromotion(page: Page, budget = 1000) {
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page).toHaveURL(/\/business\/campaigns\/[0-9a-f-]{36}$/i);
  const promotionId = page.url().split("/").pop()!;
  await ensureBusinessFunds(page.context(), budget);
  await page.reload();
  await expect(page.getByRole("button", { name: "Publish", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.locator("main .badge").filter({ hasText: "Active" }).first()).toBeVisible();
  return promotionId;
}

export async function createPublishedUgc(
  context: BrowserContext,
  title: string,
  creatorAlias: "creator" | "other-creator",
) {
  await login(context, "admin");
  const settings = await apiJson<{
    current: {
      viewOnly: unknown;
      viewPlusCommission: unknown;
      creatorCommissionPercent: number;
      customerCashbackPercent: number;
      platformPercent: number;
      creatorThreshold: number;
      customerThreshold: number;
      effectiveFromUtc: string | null;
      promotionLiveDurationDays: number;
      ugc: {
        minimumCreatorPayment: number;
        platformFeePercent: number;
        minimumUgcBudget: number | null;
        customerOfferPlatformSalePercent: number | null;
      } | null;
    };
    version: number;
  }>(context, "/admin/financial-settings");
  expect(settings.current.ugc).not.toBeNull();
  if (settings.current.ugc?.customerOfferPlatformSalePercent == null) {
    await apiPost(context, "/admin/financial-settings", {
      settings: {
        ...settings.current,
        effectiveFromUtc: null,
        ugc: {
          ...settings.current.ugc!,
          customerOfferPlatformSalePercent: 3,
        },
      },
      expectedVersion: settings.version,
    });
  }

  await login(context, "business");
  await ensureBusinessFunds(context, 750);
  const due = new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString();
  const created = await apiPost<{ id: string }>(context, "/business/ugc", {
    title,
    slogan: null,
    contentType: "Video",
    instructions: "Deliver one short product video.",
    resources: [],
    location: "Addis Ababa",
    dueDateUtc: due,
    applicationClosesAtUtc: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
    productProvided: true,
    creatorMustPurchase: false,
    usageRights: null,
    creatorPayment: 250,
    creatorsNeeded: 1,
    platformRequirements: [{ platform: "TikTok", format: "Social post", minimumAudience: null }],
    platformCapacities: [{ platform: "TikTok", capacity: 1, minimumAudience: null }],
    customerOfferEnabled: true,
    customerDiscountPercent: 2,
    customerOfferFundedAllocation: 500,
    customerFacingSlogan: title,
    customerOfferStartsAtUtc: new Date(Date.now() - 60_000).toISOString(),
    customerOfferEndsAtUtc: due,
  });
  const opportunityId = created.id;
  const detail = await apiJson<{ opportunity: { version: number } }>(context, `/business/ugc/${opportunityId}`);
  await apiPost(context, `/business/ugc/${opportunityId}/publish`, { version: detail.opportunity.version });

  await login(context, creatorAlias);
  const creatorDetail = await apiJson<{ opportunity: {
    eligibleSocialProfiles?: { id: string; platform: string }[];
  } }>(context, `/creator/ugc/${opportunityId}`);
  const profile = creatorDetail.opportunity.eligibleSocialProfiles?.find((row) => row.platform === "TikTok");
  expect(profile, `${creatorAlias} needs an eligible verified TikTok profile`).toBeDefined();
  await apiPost(context,
    `/creator/ugc/${opportunityId}/request?selectedPlatform=TikTok&verifiedSocialProfileId=${profile!.id}`);

  await login(context, "business");
  const requested = await apiJson<{ requests: { id: string; status: string; creatorId: string }[] }>(
    context,
    `/business/ugc/${opportunityId}`,
  );
  const pending = requested.requests.find((row) => row.status === "Pending");
  expect(pending).toBeDefined();
  const approved = await apiPost<{ id: string }>(context, `/business/ugc/requests/${pending!.id}/approve`, {
    reason: null,
  });

  await login(context, creatorAlias);
  const upload = await context.request.post(`/api/creator/ugc/assignments/${approved.id}/submit-review`, {
    headers: { ...requestHeaders, "Idempotency-Key": crypto.randomUUID() },
    multipart: {
      media: { name: "SAMPLE-WEYMELA-REVIEW-ONLY.mp4", mimeType: "video/mp4", buffer: reviewMp4 },
    },
  });
  if (!upload.ok()) throw new Error(`private UGC review upload: ${upload.status()} ${await upload.text()}`);

  await login(context, "business");
  await apiPost(context, `/business/ugc/assignments/${approved.id}/review/approve`, { reason: null });

  await login(context, creatorAlias);
  const publicPostId = `${Date.now()}${Math.floor(Math.random() * 100000)}`;
  const publication = await apiPost<{ status: string }>(context, `/creator/ugc/assignments/${approved.id}/publication`, {
    provider: "TikTok",
    externalContentId: publicPostId,
    creatorSocialProfileId: profile!.id,
  });
  expect(publication.status).toBe("Verified");
  await apiPost(context, `/creator/ugc/assignments/${approved.id}/go-live`, {});
  return opportunityId;
}
export async function open(page: Page, path: string) {
  await page.goto(path);
  await expect(page.locator("main h1")).toBeVisible();
  await expect(
    page.getByRole("status", { name: "Loading workspace" }),
  ).toHaveCount(0);
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
}
export async function screenshot(page: Page, name: string) {
  await page.screenshot({
    path: `${evidence}/${name}.png`,
    fullPage: true,
    animations: "disabled",
  });
}
export async function layout(page: Page) {
  const issues = await page.evaluate(() => {
    const problems: string[] = [];
    if (document.documentElement.scrollWidth > window.innerWidth + 1) {
      const overflowing = [...document.querySelectorAll<HTMLElement>("body *")]
        .map((element) => ({ element, right: element.getBoundingClientRect().right }))
        .filter(({ right }) => right > window.innerWidth + 1)
        .sort((a, b) => b.right - a.right)[0];
      const detail = overflowing
        ? `${overflowing.element.tagName.toLowerCase()}.${typeof overflowing.element.className === "string" ? overflowing.element.className.replaceAll(" ", ".") : ""} right=${overflowing.right.toFixed(1)}px`
        : "unknown element";
      problems.push(`Document overflows horizontally on ${location.pathname}: document=${document.documentElement.scrollWidth}px viewport=${window.innerWidth}px; ${detail}`);
    }
    for (const element of document.querySelectorAll<HTMLElement>(
      "main input,main select,main textarea,main button,main .button,main h1,main h2,main th",
    )) {
      if (!element.checkVisibility() || element.classList.contains("sr-only")) continue;
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      if (
        !element.closest(".admin-table") &&
        rect.width < 18 &&
        element.textContent!.trim().length > 3
      )
        problems.push(`Compressed control: ${element.tagName}`);
      if (style.wordBreak === "break-all")
        problems.push("Single-letter wrapping permitted");
      if (element.matches("input,select,textarea") && rect.width > 850)
        problems.push("Oversized input");
    }
    return problems;
  });
  expect(issues).toEqual([]);
}

export async function installQrCamera(context: BrowserContext, token: string) {
  const order = (cameraInstallOrder.get(context) ?? 0) + 1;
  cameraInstallOrder.set(context, order);
  const variants = Array.from({ length: 8 }, (_, maskPattern) => {
    const generated = QRCode.create(token, {
      errorCorrectionLevel: "M",
      maskPattern: maskPattern as 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7,
    });
    return {
      moduleCount: generated.modules.size,
      modules: Array.from(generated.modules.data, (value) => value === 1),
    };
  });
  await context.addInitScript(({ variants, order }) => {
    const scope = window as Window & { __weymelaQrCameraOrder?: number };
    if ((scope.__weymelaQrCameraOrder ?? 0) > order) return;
    scope.__weymelaQrCameraOrder = order;
    Object.defineProperty(navigator.mediaDevices, "getUserMedia", {
      configurable: true,
      value: async () => {
        const canvas = document.createElement("canvas");
        canvas.width = 1600;
        canvas.height = 1000;
        const drawing = canvas.getContext("2d")!;
        canvas.style.position = "fixed";
        canvas.style.left = "-10000px";
        document.body.append(canvas);
        const moduleCount = variants[0].moduleCount;
        const quiet = 4;
        const scale = Math.min(16, Math.floor(Math.min(canvas.width, canvas.height) / (moduleCount + quiet * 2 + 8)));
        const size = (moduleCount + quiet * 2) * scale;
        const left = Math.floor((canvas.width - size) / 2);
        const top = Math.floor((canvas.height - size) / 2);
        let frames = 0;
        const draw = () => {
          const variant = variants[Math.floor(frames / 30) % variants.length];
          drawing.imageSmoothingEnabled = false;
          drawing.fillStyle = "white";
          drawing.fillRect(0, 0, canvas.width, canvas.height);
          drawing.fillStyle = "black";
          for (let row = 0; row < moduleCount; row += 1)
            for (let column = 0; column < moduleCount; column += 1)
              if (variant.modules[row * moduleCount + column])
                drawing.fillRect(left + (column + quiet) * scale, top + (row + quiet) * scale, scale, scale);
        };
        draw();
        const stream = canvas.captureStream(0);
        const track = stream.getVideoTracks()[0] as CanvasCaptureMediaStreamTrack;
        let running = true;
        let ready!: () => void;
        const readyFrames = new Promise<void>((resolve) => { ready = resolve; });
        let last = -1000;
        const render = (timestamp: number) => {
          if (!running) return;
          if (timestamp - last >= 32) {
            draw();
            track.requestFrame();
            frames += 1;
            last = timestamp;
            if (frames === 3) ready();
          }
          requestAnimationFrame(render);
        };
        render(0);
        track.addEventListener("ended", () => {
          running = false;
          canvas.remove();
        });
        await readyFrames;
        return stream;
      },
    });
  }, { variants, order });
}

export async function scanQr(page: Page) {
  const resolved = page.waitForResponse((response) =>
    response.request().method() === "POST" && response.url().endsWith("/api/checkout/resolve"));
  await page.getByRole("button", { name: "Scan QR", exact: true }).click();
  return resolved;
}
