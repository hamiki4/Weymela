import { test, expect } from "@playwright/test";
import { login, open, screenshot } from "./helpers";

test("real QR rejects wrong Business then confirms the same offer at its Business", async ({
  page,
  context,
}) => {
  await login(context, "customer");
  await open(page, "/customer/offers");
  await page
    .getByRole("link", { name: "Get Offer", exact: true })
    .first()
    .click();
  const issuing = page.waitForResponse(
    (r) => r.url().endsWith("/qr") && r.request().method() === "POST",
  );
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const qr = await (await issuing).json();
  await expect(
    page.getByRole("img", { name: "Offer QR for the cashier" }),
  ).toBeVisible();
  await screenshot(page, "checkout-customer-issued");
  await login(context, "other-cashier");
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve QR" }).click();
  await expect(page.getByRole("alert")).toHaveText(
    "This QR belongs to another business",
  );
  await expect(
    page.getByLabel("Total Purchase Amount", { exact: true }),
  ).toHaveCount(0);
  await login(context, "cashier");
  await open(page, "/checkout");
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await page.getByLabel("QR code", { exact: true }).fill(qr.token);
  await page.getByRole("button", { name: "Resolve QR" }).click();
  await expect(
    page.getByLabel("Total Purchase Amount", { exact: true }),
  ).toBeVisible();
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("1000");
  await screenshot(page, "checkout-confirmation");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Payment recorded", exact: true }),
  ).toBeVisible();
  await screenshot(page, "checkout-confirmed");
  await login(context, "customer");
  const status = await (
    await context.request.get(`/api/customer/qr/${qr.id}`)
  ).json();
  expect(status.status).toBe("Used");
  await open(page, "/customer/history");
  await expect(page).toHaveURL(/\/customer\/transactions$/);
  await open(page, "/customer/transactions");
  await expect(page.locator("main")).toContainText("Abc Coffee");
  await expect(page.locator("main")).toContainText("Cashback earned");
  await open(page, "/customer/cashback");
  await expect(page.locator("main")).toContainText("Cashback");
  expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([]);
});

test("camera scanner decodes the real issued QR and owner uses the same checkout", async ({
  page,
  context,
}, testInfo) => {
  let resolveRequested = false;
  page.on("request", (request) => {
    if (
      request.method() === "POST" &&
      request.url().endsWith("/api/checkout/resolve")
    ) {
      resolveRequested = true;
    }
  });

  await login(context, "customer");
  await open(page, "/customer/offers");
  await page
    .getByRole("link", { name: "Get Offer", exact: true })
    .first()
    .click();
  await page.getByRole("button", { name: "Get Offer", exact: true }).click();
  const image = page.getByRole("img", { name: "Offer QR for the cashier" });
  await expect(image).toBeVisible();
  await expect(image).toHaveAttribute("src", /^data:image\/png;base64,/);
  const src = await image.getAttribute("src");
  // Synthetic camera frames exercise the actual ZXing decoder. No API/finance response is mocked.
  await context.addInitScript(
    ({ src }) => {
      Object.defineProperty(navigator.mediaDevices, "getUserMedia", {
        value: async () => {
          const canvas = document.createElement("canvas");
          canvas.width = 1600;
          canvas.height = 1000;
          const ctx = canvas.getContext("2d")!;
          canvas.style.position = "fixed";
          canvas.style.left = "-10000px";
          canvas.style.top = "0";
          document.body.append(canvas);
          const image = new Image();
          image.decoding = "sync";
          await new Promise<void>((resolve, reject) => {
            image.onload = () => resolve();
            image.onerror = () =>
              reject(new Error("Synthetic QR image failed to load"));
            image.src = src!;
          });
          const qrCanvas = document.createElement("canvas");
          qrCanvas.width = image.naturalWidth;
          qrCanvas.height = image.naturalHeight;
          const qrContext = qrCanvas.getContext("2d")!;
          qrContext.imageSmoothingEnabled = false;
          qrContext.drawImage(image, 0, 0);
          const qrPixels = qrContext.getImageData(
            0,
            0,
            qrCanvas.width,
            qrCanvas.height,
          );
          for (let index = 0; index < qrPixels.data.length; index += 4) {
            const luminance =
              (qrPixels.data[index] +
                qrPixels.data[index + 1] +
                qrPixels.data[index + 2]) /
              3;
            const value = luminance < 128 ? 0 : 255;
            qrPixels.data[index] = value;
            qrPixels.data[index + 1] = value;
            qrPixels.data[index + 2] = value;
            qrPixels.data[index + 3] = 255;
          }
          qrContext.putImageData(qrPixels, 0, 0);
          if (qrCanvas.width !== qrCanvas.height) {
            throw new Error("Synthetic QR image is not square");
          }
          const quietZonePixels = 80;
          const sourceScale = 3;
          const largeQrCanvas = document.createElement("canvas");
          largeQrCanvas.width =
            qrCanvas.width * sourceScale + quietZonePixels * 2;
          largeQrCanvas.height = largeQrCanvas.width;
          const framedQrContext = largeQrCanvas.getContext("2d")!;
          framedQrContext.imageSmoothingEnabled = false;
          framedQrContext.fillStyle = "white";
          framedQrContext.fillRect(
            0,
            0,
            largeQrCanvas.width,
            largeQrCanvas.height,
          );
          const scaledQrPixels = framedQrContext.createImageData(
            qrCanvas.width * sourceScale,
            qrCanvas.height * sourceScale,
          );
          for (let sourceY = 0; sourceY < qrCanvas.height; sourceY += 1) {
            for (let sourceX = 0; sourceX < qrCanvas.width; sourceX += 1) {
              const sourceIndex = (sourceY * qrCanvas.width + sourceX) * 4;
              for (let offsetY = 0; offsetY < sourceScale; offsetY += 1) {
                for (let offsetX = 0; offsetX < sourceScale; offsetX += 1) {
                  const targetX = sourceX * sourceScale + offsetX;
                  const targetY = sourceY * sourceScale + offsetY;
                  const targetIndex =
                    (targetY * scaledQrPixels.width + targetX) * 4;
                  scaledQrPixels.data[targetIndex] = qrPixels.data[sourceIndex];
                  scaledQrPixels.data[targetIndex + 1] =
                    qrPixels.data[sourceIndex + 1];
                  scaledQrPixels.data[targetIndex + 2] =
                    qrPixels.data[sourceIndex + 2];
                  scaledQrPixels.data[targetIndex + 3] =
                    qrPixels.data[sourceIndex + 3];
                }
              }
            }
          }
          framedQrContext.putImageData(
            scaledQrPixels,
            quietZonePixels,
            quietZonePixels,
          );
          const qrSize = largeQrCanvas.width;
          const qrX = Math.floor((canvas.width - qrSize) / 2);
          const qrY = Math.floor((canvas.height - qrSize) / 2);
          const state = ((
            window as Window & {
              __weymelaSyntheticCamera?: {
                frameReady: boolean;
                framesRendered: number;
                trackEnded: boolean;
                canvasWidth: number;
                canvasHeight: number;
                sourceWidth: number;
                sourceHeight: number;
                quietZone: number;
                sourceScale: number;
                qrFrameWidth: number;
                qrFrameHeight: number;
                qrDrawX: number;
                qrDrawY: number;
                qrDrawSize: number;
                imageSmoothingEnabled: boolean;
              };
            }
          ).__weymelaSyntheticCamera ??= {
            frameReady: false,
            framesRendered: 0,
            trackEnded: false,
            canvasWidth: canvas.width,
            canvasHeight: canvas.height,
            sourceWidth: qrCanvas.width,
            sourceHeight: qrCanvas.height,
            quietZone: quietZonePixels,
            sourceScale,
            qrFrameWidth: largeQrCanvas.width,
            qrFrameHeight: largeQrCanvas.height,
            qrDrawX: qrX,
            qrDrawY: qrY,
            qrDrawSize: qrSize,
            imageSmoothingEnabled: false,
          });
          const drawFrame = () => {
            const framedQrCanvas = largeQrCanvas;
            const currentQrSize = framedQrCanvas.width;
            const currentQrX = Math.floor((canvas.width - currentQrSize) / 2);
            const currentQrY = Math.floor((canvas.height - currentQrSize) / 2);
            state.qrFrameWidth = currentQrSize;
            state.qrFrameHeight = currentQrSize;
            state.qrDrawX = currentQrX;
            state.qrDrawY = currentQrY;
            state.qrDrawSize = currentQrSize;
            state.quietZone = quietZonePixels;
            state.sourceScale = sourceScale;
            ctx.fillStyle = "white";
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.imageSmoothingEnabled = false;
            ctx.drawImage(
              framedQrCanvas,
              currentQrX,
              currentQrY,
              currentQrSize,
              currentQrSize,
            );
          };
          drawFrame();
          const stream = canvas.captureStream(0);
          const track =
            stream.getVideoTracks()[0] as CanvasCaptureMediaStreamTrack;
          let running = true;
          let resolveReady!: () => void;
          const ready = new Promise<void>((resolve) => {
            resolveReady = resolve;
          });
          let lastFrameAt = -1000;
          const render = (timestamp: number) => {
            if (!running) return;
            if (timestamp - lastFrameAt >= 32) {
              drawFrame();
              track.requestFrame();
              state.framesRendered += 1;
              lastFrameAt = timestamp;
              if (state.framesRendered === 3) {
                state.frameReady = true;
                resolveReady();
              }
            }
            window.requestAnimationFrame(render);
          };
          render(0);
          track.addEventListener("ended", () => {
            running = false;
            canvas.remove();
            state.trackEnded = true;
          });
          await ready;
          return stream;
        },
      });
    },
    { src },
  );
  await login(context, "business");
  await open(page, "/checkout");
  const resolved = page.waitForResponse(
    (response) =>
      response.request().method() === "POST" &&
      response.url().endsWith("/api/checkout/resolve") &&
      response.status() === 200,
    { timeout: 12000 },
  );
  await page.getByRole("button", { name: "Scan QR", exact: true }).click();
  const preview = page.getByLabel("QR camera preview", { exact: true });
  await expect(preview).toBeVisible();
  try {
    const videoReady = page
      .waitForFunction(
        () => {
          const state = (
            window as Window & {
              __weymelaSyntheticCamera?: { frameReady: boolean };
            }
          ).__weymelaSyntheticCamera;
          const video = document.querySelector<HTMLVideoElement>(
            'video[aria-label="QR camera preview"]',
          );
          return (
            state?.frameReady === true &&
            video !== null &&
            video.readyState > 2 &&
            !video.paused &&
            video.videoWidth > 0 &&
            video.videoHeight > 0
          );
        },
        undefined,
        { timeout: 12000 },
      )
      .then(
        () => "video" as const,
        () => "scanner" as const,
      );
    const readiness = await Promise.race([
      videoReady,
      resolved.then(() => "scanner" as const),
    ]);
    if (readiness === "video") await resolved;
    await expect(
      page.getByLabel("Total Purchase Amount", { exact: true }),
    ).toBeVisible();
  } catch (error) {
    const diagnostics = await page.evaluate(() => {
      const video = document.querySelector<HTMLVideoElement>(
        'video[aria-label="QR camera preview"]',
      );
      const stream = video?.srcObject as MediaStream | null;
      const state = (
        window as Window & {
          __weymelaSyntheticCamera?: {
            frameReady: boolean;
            framesRendered: number;
            trackEnded: boolean;
            canvasWidth: number;
            canvasHeight: number;
            sourceWidth: number;
            sourceHeight: number;
            quietZone: number;
            sourceScale: number;
            qrFrameWidth: number;
            qrFrameHeight: number;
            qrDrawX: number;
            qrDrawY: number;
            qrDrawSize: number;
            imageSmoothingEnabled: boolean;
          };
        }
      ).__weymelaSyntheticCamera;
      let videoFrame = null;
      if (video && video.videoWidth > 0 && video.videoHeight > 0) {
        const frameCanvas = document.createElement("canvas");
        frameCanvas.width = video.videoWidth;
        frameCanvas.height = video.videoHeight;
        const frameContext = frameCanvas.getContext("2d")!;
        frameContext.drawImage(video, 0, 0);
        const framePixels = frameContext.getImageData(
          0,
          0,
          frameCanvas.width,
          frameCanvas.height,
        );
        let darkPixels = 0;
        let minX = frameCanvas.width;
        let minY = frameCanvas.height;
        let maxX = -1;
        let maxY = -1;
        for (let index = 0; index < framePixels.data.length; index += 4) {
          const luminance =
            (framePixels.data[index] +
              framePixels.data[index + 1] +
              framePixels.data[index + 2]) /
            3;
          if (luminance < 128) {
            darkPixels += 1;
            const pixel = index / 4;
            const x = pixel % frameCanvas.width;
            const y = Math.floor(pixel / frameCanvas.width);
            minX = Math.min(minX, x);
            minY = Math.min(minY, y);
            maxX = Math.max(maxX, x);
            maxY = Math.max(maxY, y);
          }
        }
        videoFrame = {
          width: frameCanvas.width,
          height: frameCanvas.height,
          darkPixels,
          darkBounds: maxX >= 0 ? { minX, minY, maxX, maxY } : null,
        };
      }
      return {
        url: window.location.href,
        scannerVisible: Boolean(video),
        video: video
          ? {
              readyState: video.readyState,
              paused: video.paused,
              videoWidth: video.videoWidth,
              videoHeight: video.videoHeight,
            }
          : null,
        stream: stream
          ? {
              active: stream.active,
              tracks: stream.getVideoTracks().map((track) => ({
                readyState: track.readyState,
                enabled: track.enabled,
              })),
            }
          : null,
        syntheticCamera: state ?? null,
        videoFrame,
      };
    });
    const diagnosticText = JSON.stringify(
      { ...diagnostics, resolveRequested },
      null,
      2,
    );
    await testInfo.attach("camera-diagnostics", {
      body: diagnosticText,
      contentType: "application/json",
    });
    throw new Error(
      `${error instanceof Error ? error.message : String(error)}\nCamera diagnostics: ${diagnosticText}`,
    );
  }
  await page.getByLabel("Total Purchase Amount", { exact: true }).fill("50");
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Payment recorded", exact: true }),
  ).toBeVisible();
});

test("camera permission failure has a usable alternative", async ({
  page,
  context,
}) => {
  await context.addInitScript(() =>
    Object.defineProperty(navigator.mediaDevices, "getUserMedia", {
      value: async () => {
        throw new DOMException("Denied", "NotAllowedError");
      },
    }),
  );
  await login(context, "cashier");
  await open(page, "/checkout");
  await page.getByRole("button", { name: "Scan QR", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText(
    "Camera permission was denied",
  );
  await page.getByText("Enter an opaque QR code", { exact: true }).click();
  await expect(page.getByLabel("QR code", { exact: true })).toBeVisible();
});
