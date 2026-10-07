import { useEffect, useRef, useState } from "react";
import { post, useAction, useResource } from "../../api/client";
import type {
  CheckoutOffer,
  CheckoutSaleRow,
  ManualCheckoutChoice,
  ManualCheckoutResolution,
  SaleResult,
} from "../../api/types";
import {
  Button,
  Field,
  MoneyInput,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, date, dateTime } from "../../ui/format";
import { Icon } from "../../ui/Icon";
import { useSession } from "../../app/Session";

function CheckoutTransactionList({ rows }: { rows: CheckoutSaleRow[] }) {
  return rows.length ? <div className="business-transaction-list">{rows.map((row) => <article className="business-transaction-card" key={`${row.source}-${row.id}`}>
    <div className="business-transaction-main"><div><strong>{row.offer}</strong><small>{row.source === "UGC_CUSTOMER_OFFER" ? "UGC + Sale" : "View + Sale"} · {dateTime(row.createdAtUtc)} · {row.status}</small></div>
      <strong>{amount(row.purchaseAmount)} ETB</strong></div>
    <div className="business-transaction-context">
      <span>Creator: {row.creator ?? "—"}{row.creatorNumber ? ` · #${row.creatorNumber}` : ""}</span>
      <span>Customer: {row.customerMasked}</span>
      {row.cashier && <span>Cashier: {row.cashier}</span>}
      <span>{row.benefitMode === "LegacyDiscount" ? "Customer discount" : "Customer cashback"}: {amount(row.customerDiscount)} ETB</span>
      {row.creatorEarning != null && <span>Creator earning: {amount(row.creatorEarning)} ETB</span>}
      <span>Business charge: {amount(row.businessCharge)} ETB</span>
    </div>
  </article>)}</div> : <p className="muted">No transactions yet.</p>;
}

export function CheckoutTransactions() {
  const { user } = useSession();
  const [source, setSource] = useState("ALL");
  const business = user?.role === "Business";
  const recent = useResource<CheckoutSaleRow[]>(user?.role === "Business" ? "/business/transactions" : "/checkout/recent");
  return <div className="checkout-panel"><PageHeader title="Transactions" compact={business} />
    {business && <div className="transaction-filters" role="group" aria-label="Transaction source">
      {[["ALL", "All"], ["VIEW_AND_SALE_PROMOTION", "View + Sale"], ["UGC_CUSTOMER_OFFER", "UGC + Sale"]].map(([value, label]) =>
        <button key={value} type="button" aria-pressed={source === value} onClick={() => setSource(value)}>{label}</button>)}
    </div>}
    <Section title={business ? "Purchases" : "Recent purchases"}><Resource resource={recent}>{(rows) => <CheckoutTransactionList rows={Array.isArray(rows) ? rows.filter((row) => !business || source === "ALL" || row.source === source) : []} />}</Resource></Section>
  </div>;
}

export function Checkout() {
  const { user } = useSession();
  const [token, setToken] = useState("");
  const [offer, setOffer] = useState<CheckoutOffer | null>(null);
  const [purchase, setPurchase] = useState("");
  const [result, setResult] = useState<SaleResult | null>(null);
  const [camera, setCamera] = useState(false);
  const [cameraError, setCameraError] = useState("");
  const [manual, setManual] = useState(false);
  const [creatorId, setCreatorId] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [manualOffers, setManualOffers] = useState<ManualCheckoutChoice[]>([]);
  const [selectedManualOffer, setSelectedManualOffer] = useState("");
  const video = useRef<HTMLVideoElement>(null);
  const stop = useRef<(() => void) | null>(null);
  const resolve = useAction();
  const confirm = useAction();
  const manualResolve = useAction();
  const manualConfirm = useAction();
  const recent = useResource<CheckoutSaleRow[]>("/checkout/recent");

  const clearCheckout = () => {
    setResult(null);
    setOffer(null);
    setToken("");
    setPurchase("");
    setManualOffers([]);
    setSelectedManualOffer("");
    setManual(false);
  };

  const resolveToken = async (value: string) => {
    await resolve.run(async (key) => {
      const r = await post<CheckoutOffer>("/checkout/resolve", { token: value }, key);
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
    const release = () => {
      stop.current?.();
      stop.current = null;
      stream?.getTracks().forEach((track) => track.stop());
      if (preview) preview.srcObject = null;
    };
    const hidden = () => {
      if (document.hidden) {
        closed = true;
        release();
        setCamera(false);
      }
    };
    document.addEventListener("visibilitychange", hidden);
    void import("@zxing/browser")
      .then(async ({ BrowserQRCodeReader }) => {
        if (closed || !preview) return;
        if (!navigator.mediaDevices?.getUserMedia) throw new Error("CameraUnavailable");
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: "environment" },
          audio: false,
        });
        if (closed) {
          release();
          return;
        }
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
          const failure =
            error && typeof error === "object" && "name" in error ? error.name : "";
          setCameraError(
            failure === "NotAllowedError"
              ? "Camera unavailable. Allow camera access and try again, or use manual checkout."
              : "Camera unavailable. Try again or use manual checkout.",
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
        eyebrow={user?.role === "Cashier" ? "Cashier checkout" : "Business checkout"}
        title="Checkout"
        description="Scan the Customer’s Offer QR, then enter the total purchase amount."
      />
      {result ? (
        <Section title="Payment recorded">
          <Notice>Payment recorded successfully.</Notice>
          <p>
            Purchase total: <strong>{amount(result.purchaseAmount.amount)}</strong>
          </p>
          <Button onClick={clearCheckout}>Scan Another QR</Button>
        </Section>
      ) : offer ? (
        <Section title="Enter total purchase amount">
          <dl className="detail-list">
            <div>
              <dt>Business</dt>
              <dd>{offer.business.displayName}</dd>
            </div>
            <div>
              <dt>Promotion</dt>
              <dd>{offer.offer}</dd>
            </div>
            {offer.creator ? (
              <div>
                <dt>Creator</dt>
                <dd>{offer.creator.displayName}</dd>
              </div>
            ) : null}
            <div>
              <dt>Customer</dt>
              <dd>{offer.customer}</dd>
            </div>
          </dl>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void confirm.run(async (key) => {
                const r = await post<SaleResult>(
                  "/checkout/confirm",
                  { token, purchaseAmount: Number(purchase) },
                  key,
                );
                setToken("");
                setResult(r);
                recent.reload();
              });
            }}
          >
            <fieldset disabled={confirm.busy}>
              <Field label="Total Purchase Amount">
                <MoneyInput
                  value={purchase}
                  onChange={(event) => setPurchase(event.target.value)}
                />
              </Field>
              {confirm.error ? <Notice error>{confirm.error}</Notice> : null}
              <div className="actions">
                <Button type="submit" disabled={confirm.busy || !purchase}>
                  {confirm.busy ? "Submitting…" : "Submit"}
                </Button>
                <Button
                  variant="secondary"
                  type="button"
                  onClick={() => {
                    setOffer(null);
                    setToken("");
                    setPurchase("");
                  }}
                >
                  Scan Another QR
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
                <video ref={video} autoPlay playsInline muted aria-label="QR camera preview" />
              ) : (
                <div className="scanner-placeholder">
                  <Icon name="qr" size={52} />
                  <span>Position the Customer’s QR inside the camera view.</span>
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
            {cameraError ? <Notice error>{cameraError}</Notice> : null}
            {resolve.error ? <Notice error>{resolve.error}</Notice> : null}
          </Section>
          <Section title="Can’t scan QR?">
            <p className="fine-print">
              Enter the Creator ID and Customer phone. Weymela will show only
              currently eligible offers for this Business.
            </p>
            <Button
              variant="secondary"
              onClick={() => {
                setManual(!manual);
                setManualOffers([]);
                setSelectedManualOffer("");
              }}
            >
              {manual ? "Close manual checkout" : "Enter Manually"}
            </Button>
            {manual ? (
              <form
                onSubmit={(event) => {
                  event.preventDefault();
                  void manualResolve.run(async (key) => {
                    const response = await post<ManualCheckoutResolution>(
                      "/checkout/manual-resolve",
                      { creatorId, customerPhone },
                      key,
                    );
                    setManualOffers(response.offers);
                    setSelectedManualOffer(
                      response.offers.length === 1 ? response.offers[0].id : "",
                    );
                  });
                }}
              >
                <fieldset disabled={manualResolve.busy}>
                  <Field label="Creator ID">
                    <input
                      value={creatorId}
                      onChange={(event) => setCreatorId(event.target.value)}
                      inputMode="numeric"
                      pattern="[0-9]+"
                      autoComplete="off"
                      required
                    />
                  </Field>
                  <Field label="Customer phone">
                    <input
                      type="tel"
                      inputMode="tel"
                      value={customerPhone}
                      onChange={(event) => setCustomerPhone(event.target.value)}
                      autoComplete="tel"
                      required
                    />
                  </Field>
                  {manualResolve.error ? <Notice error>{manualResolve.error}</Notice> : null}
                  <Button type="submit" variant="secondary">
                    {manualResolve.busy ? "Finding offers…" : "Find eligible offers"}
                  </Button>
                </fieldset>
              </form>
            ) : null}
            {manualOffers.length > 0 ? (
              <form
                onSubmit={(event) => {
                  event.preventDefault();
                  void manualConfirm.run(async (key) => {
                    const response = await post<SaleResult>(
                      "/checkout/manual-confirm",
                      {
                        creatorId,
                        customerPhone,
                        offerId: selectedManualOffer,
                        purchaseAmount: Number(purchase),
                      },
                      key,
                    );
                    setResult(response);
                    setManualOffers([]);
                    recent.reload();
                  });
                }}
              >
                <fieldset disabled={manualConfirm.busy}>
                  <fieldset>
                    <legend>Eligible offer</legend>
                    {manualOffers.map((choice) => (
                      <label className="choice-row" key={choice.id}>
                        <input
                          type="radio"
                          name="manual-offer"
                          value={choice.id}
                          checked={selectedManualOffer === choice.id}
                          onChange={() => setSelectedManualOffer(choice.id)}
                        />
                        <span>
                          {choice.label}
                          {choice.benefitPercent !== null
                            ? " · " + choice.benefitPercent + "% " +
                              (choice.benefitMode === "LegacyDiscount" ? "off" : "cashback")
                            : ""}
                        </span>
                      </label>
                    ))}
                  </fieldset>
                  <Field label="Total Purchase Amount">
                    <MoneyInput
                      value={purchase}
                      onChange={(event) => setPurchase(event.target.value)}
                    />
                  </Field>
                  {manualConfirm.error ? <Notice error>{manualConfirm.error}</Notice> : null}
                  <Button
                    type="submit"
                    disabled={manualConfirm.busy || !selectedManualOffer || !purchase}
                  >
                    {manualConfirm.busy ? "Submitting…" : "Submit"}
                  </Button>
                </fieldset>
              </form>
            ) : null}
          </Section>
        </>
      )}
      <Section title="Recent Transactions">
        <Resource resource={recent}>{(rows) => <CheckoutTransactionList rows={Array.isArray(rows) ? rows.slice(0, 10) : []} />}</Resource>
      </Section>
    </div>
  );
}
