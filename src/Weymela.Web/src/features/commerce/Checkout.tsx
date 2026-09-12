import { useEffect, useRef, useState } from "react";
import { post, useAction } from "../../api/client";
import type { CheckoutOffer, SaleResult } from "../../api/types";
import {
  Button,
  Field,
  MoneyInput,
  Notice,
  PageHeader,
  Section,
} from "../../ui/components";
import { amount } from "../../ui/format";
import { Icon } from "../../ui/Icon";

export function Checkout() {
  const [token, setToken] = useState("");
  const [offer, setOffer] = useState<CheckoutOffer | null>(null);
  const [purchase, setPurchase] = useState("");
  const [result, setResult] = useState<SaleResult | null>(null);
  const [camera, setCamera] = useState(false);
  const [cameraError, setCameraError] = useState("");
  const video = useRef<HTMLVideoElement>(null);
  const stop = useRef<(() => void) | null>(null);
  const resolve = useAction();
  const confirm = useAction();
  const resolveToken = async (value: string) => {
    await resolve.run(async (key) => {
      const r = await post<CheckoutOffer>(
        "/checkout/resolve",
        { token: value },
        key,
      );
      setToken(value);
      setOffer(r);
      setResult(null);
      setCamera(false);
    });
  };
  useEffect(() => {
    if (!camera) {
      stop.current?.();
      stop.current = null;
      return;
    }
    let closed = false;
    let stream: MediaStream | null = null;
    const preview = video.current;
    const release = () => { stop.current?.(); stop.current = null; stream?.getTracks().forEach(track => track.stop()); if (preview) preview.srcObject = null; };
    const hidden = () => { if (document.hidden) { closed = true; release(); setCamera(false); } };
    document.addEventListener("visibilitychange", hidden);
    void import("@zxing/browser")
      .then(async ({ BrowserQRCodeReader }) => {
        if (closed || !preview) return;
        if (!navigator.mediaDevices?.getUserMedia) throw new Error("CameraUnavailable");
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" }, audio: false });
        if (closed) { release(); return; }
        const reader = new BrowserQRCodeReader();
        const controls = await reader.decodeFromStream(
          stream,
          preview,
          (decoded, _error, controls) => {
            if (decoded && !closed) {
              closed = true;
              controls.stop();
              void resolveToken(decoded.getText());
              setCamera(false);
            }
          },
        );
        if (closed) controls.stop();
        else stop.current = () => controls.stop();
      })
      .catch((error: unknown) => {
        if (!closed) {
          release();
          const failure = error && typeof error === "object" && "name" in error ? error.name : "";
          setCameraError(
            failure === "NotAllowedError"
              ? "Camera permission was denied. Allow camera access in your browser settings, or paste the scanned QR code below."
              : failure === "NotFoundError"
                ? "No camera was found on this device. Paste the scanned QR code below."
                : "Camera access is unavailable. Close other camera apps and try again, or paste the scanned QR code below.",
          );
          setCamera(false);
        }
      });
    return () => {
      closed = true;
      document.removeEventListener("visibilitychange", hidden);
      release();
    };
  }, [camera]);
  return (
    <div className="checkout-panel">
      <PageHeader
        eyebrow="Business checkout"
        title="Checkout"
        description="Scan the Customer’s Offer QR, then enter only the purchase amount."
      />
      {result ? (
        <Section title="Purchase confirmed">
          <Notice>
            Purchase of {amount(result.purchaseAmount.amount)} ETB confirmed.
          </Notice>
          <p>The eligible rewards have been recorded.</p>
          <Button
            onClick={() => {
              setResult(null);
              setOffer(null);
              setToken("");
              setPurchase("");
            }}
          >
            Next Customer
          </Button>
        </Section>
      ) : offer ? (
        <Section title="Confirm purchase">
          <dl className="detail-list">
            <div>
              <dt>Business</dt>
              <dd>{offer.business.displayName}</dd>
            </div>
            <div>
              <dt>Campaign</dt>
              <dd>{offer.campaign}</dd>
            </div>
            <div>
              <dt>Creator</dt>
              <dd>
                {offer.creator.displayName} · {offer.creator.publicId}
              </dd>
            </div>
            <div>
              <dt>Customer</dt>
              <dd>{offer.customer}</dd>
            </div>
          </dl>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void confirm.run(async (key) => {
                const r = await post<SaleResult>(
                  "/checkout/confirm",
                  { token, purchaseAmount: Number(purchase) },
                  key,
                );
                setToken("");
                setResult(r);
              });
            }}
          >
            <fieldset disabled={confirm.busy}>
              <Field label="Purchase Amount (ETB)">
                <MoneyInput
                  value={purchase}
                  onChange={(e) => setPurchase(e.target.value)}
                />
              </Field>
              {confirm.error && <Notice error>{confirm.error}</Notice>}
              <div className="actions">
                <Button type="submit" disabled={confirm.busy || !purchase}>
                  {confirm.busy ? "Confirming…" : "Confirm Purchase"}
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setOffer(null);
                    setToken("");
                    setPurchase("");
                  }}
                >
                  Back
                </Button>
              </div>
            </fieldset>
          </form>
        </Section>
      ) : (
        <>
          <Section title="Scan QR">
            <div className="scanner-frame">
              {camera ? (
                <video
                  ref={video}
                  autoPlay
                  playsInline
                  muted
                  aria-label="QR camera preview"
                />
              ) : (
                <div className="scanner-placeholder">
                  <Icon name="qr" size={52} />
                  <span>
                    Position the Customer’s QR inside the camera view.
                  </span>
                </div>
              )}
            </div>
            <Button
              icon="qr"
              onClick={() => {
                setCameraError("");
                setCamera(!camera);
              }}
            >
              {camera ? "Stop Camera" : "Scan QR"}
            </Button>
            {cameraError && <Notice error>{cameraError}</Notice>}
            {resolve.error && <Notice error>{resolve.error}</Notice>}
            <details className="audit-detail">
              <summary>Use a scanned code instead</summary>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void resolveToken(token.trim());
                }}
              >
                <Field
                  label="Scanned QR code"
                  help="Paste only the opaque code from the QR. No phone number or payment information."
                >
                  <input
                    type="password"
                    autoComplete="off"
                    spellCheck={false}
                    value={token}
                    onChange={(e) => setToken(e.target.value)}
                    maxLength={256}
                    required
                  />
                </Field>
                <Button
                  type="submit"
                  variant="secondary"
                  disabled={resolve.busy || !token}
                >
                  Resolve Offer
                </Button>
              </form>
            </details>
          </Section>
          <Section title="Manual checkout">
            <p className="fine-print">
              Creator/customer lookup is not connected in this phase. Use the
              Customer’s Offer QR. Both paths will use this same checkout
              service.
            </p>
            <Button variant="secondary" disabled>
              Manual Lookup Unavailable
            </Button>
          </Section>
        </>
      )}
    </div>
  );
}
