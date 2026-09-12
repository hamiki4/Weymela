import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import QRCode from "qrcode";
import { post, request, useAction, useResource } from "../../api/client";
import type { Money, Offer, Qr } from "../../api/types";
import {
  ActionLink,
  Button,
  Currency,
  Empty,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, date, safeExternal } from "../../ui/format";
import { Icon } from "../../ui/Icon";

export function CustomerOffers() {
  const resource = useResource<Offer[]>("/customer/offers");
  return (
    <>
      <PageHeader
        eyebrow="Good places. A little cashback."
        title="Offers for you"
        description="Explore local Businesses through Creators you can connect with."
      />
      <Resource resource={resource}>
        {(rows) =>
          rows.length ? (
            <div className="card-stack campaign-grid">
              {rows.map((r) => (
                <article className="campaign-card" key={r.id}>
                  <div className="campaign-art" aria-hidden="true">
                    <span>{r.business.displayName[0]}</span>
                    <Icon name="sparkle" />
                  </div>
                  <div className="card-head">
                    <h2>{r.business.displayName}</h2>
                    <span className="cashback">
                      {amount(r.cashbackPercent)}% Cashback
                    </span>
                  </div>
                  <p>Promoted by {r.creator.displayName}</p>
                  <div className="actions">
                    {safeExternal(r.watchUrl) && (
                      <a
                        className="text-link"
                        href={safeExternal(r.watchUrl)}
                        target="_blank"
                        rel="noreferrer"
                      >
                        Watch Promotion
                      </a>
                    )}
                    {safeExternal(r.business.directionsUrl) && (
                      <a
                        className="text-link"
                        href={safeExternal(r.business.directionsUrl)}
                        target="_blank"
                        rel="noreferrer"
                      >
                        Get Directions
                      </a>
                    )}
                  </div>
                  <div className="actions">
                    <ActionLink to={`/customer/offers/${r.id}`} icon="qr">
                      Get Offer QR
                    </ActionLink>
                  </div>
                </article>
              ))}
            </div>
          ) : (
            <Section title="Local offers">
              <Empty
                title="No offers available right now"
                message="Eligible cashback offers appear here when a Creator’s Campaign is active."
              />
            </Section>
          )
        }
      </Resource>
    </>
  );
}
function QrPanel({ offer }: { offer: Offer }) {
  const [qr, setQr] = useState<Qr | null>(null);
  const [image, setImage] = useState("");
  const [now, setNow] = useState(Date.now());
  const [used, setUsed] = useState(false);
  const [renderError, setRenderError] = useState("");
  const action = useAction();
  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, []);
  useEffect(() => {
    if (!qr) return;
    let alive = true;
    const check = () => {
      void request<{ status: string }>(`/customer/qr/${qr.id}`)
        .then((s) => {
          if (alive && s.status === "Used") {
            setUsed(true);
            setImage("");
          }
        })
        .catch(() => {
          /* Expiry remains enforced locally and by checkout. */
        });
    };
    const interval = setInterval(check, 5000);
    return () => {
      alive = false;
      clearInterval(interval);
    };
  }, [qr]);
  const remaining = qr
    ? Math.max(0, Math.ceil((new Date(qr.expiresAtUtc).getTime() - now) / 1000))
    : 0;
  useEffect(() => {
    if (qr && remaining === 0) setImage("");
  }, [qr, remaining]);
  return (
    <div className="offer-simple">
      <h1>{offer.business.displayName}</h1>
      <p>Promoted by {offer.creator.displayName}</p>
      <span className="cashback">
        {amount(offer.cashbackPercent)}% Cashback
      </span>
      {used ? (
        <Notice>Offer used. Your confirmed cashback is in your history.</Notice>
      ) : image && remaining > 0 ? (
        <>
          <img
            className="qr-image"
            src={image}
            alt="Offer QR for the cashier"
          />
          <p className="qr-instruction">Show this QR to the cashier.</p>
          <p className="qr-expiry" role="timer">
            Expires in {String(Math.floor(remaining / 60)).padStart(2, "0")}:
            {String(remaining % 60).padStart(2, "0")}
          </p>
        </>
      ) : (
        <>
          {qr && (
            <p className="fine-print">
              {remaining === 0
                ? "This QR has expired. Get a new one when you’re ready."
                : "This QR cannot be displayed again. Get a replacement."}
            </p>
          )}
          <Button
            icon="qr"
            disabled={action.busy}
            onClick={() =>
              void action.run(async (key) => {
                const result = await post<Qr>(
                  `/customer/offers/${offer.id}/qr`,
                  {},
                  key,
                );
                setQr({ ...result, token: null });
                setNow(Date.now());
                setRenderError("");
                // The raw token exists only long enough to render the QR. Never store it in URLs, storage or logs.
                if (result.token) {
                  try {
                    setImage(
                      await QRCode.toDataURL(result.token, {
                        width: 280,
                        margin: 2,
                        errorCorrectionLevel: "M",
                      }),
                    );
                  } catch {
                    setRenderError(
                      "We could not display this QR. Please get a replacement.",
                    );
                  }
                }
              })
            }
          >
            {action.busy ? "Preparing QR…" : "Get Offer QR"}
          </Button>
        </>
      )}
      {action.error && <Notice error>{action.error}</Notice>}
      {renderError && <Notice error>{renderError}</Notice>}
    </div>
  );
}
export function CustomerOfferQr() {
  const { id } = useParams();
  const resource = useResource<Offer[]>("/customer/offers");
  return (
    <div className="offer-page">
      <Link className="back-link" to="/customer/offers">
        ← Back
      </Link>
      <Resource resource={resource}>
        {(rows) => {
          const r = rows.find((row) => row.id === id);
          return r ? (
            <QrPanel key={r.id} offer={r} />
          ) : (
            <Section title="Offer unavailable">
              <Empty
                title="This offer isn’t available right now"
                message="Go back to find another active offer."
              />
            </Section>
          );
        }}
      </Resource>
    </div>
  );
}
export function CustomerHistory() {
  const resource = useResource<
    {
      saleId: string;
      campaign: string;
      business: { displayName: string };
      creator: { displayName: string };
      purchaseAmount: Money;
      cashback: Money;
      purchasedAtUtc: string;
    }[]
  >("/customer/history");
  return (
    <>
      <PageHeader
        eyebrow="Your confirmed purchases"
        title="Your Cashback"
        description="Cashback earned through your eligible offers."
      />
      <Resource resource={resource}>
        {(rows) => (
          <Section title="Purchase & cashback history" action={<Currency />}>
            {rows.length ? (
              rows.map((r) => (
                <article className="amount-row" key={r.saleId}>
                  <div>
                    <strong>{r.business.displayName}</strong>
                    <small>
                      Promoted by {r.creator.displayName} ·{" "}
                      {date(r.purchasedAtUtc)}
                    </small>
                    <small>Purchase {amount(r.purchaseAmount.amount)}</small>
                  </div>
                  <div>
                    <strong>+{amount(r.cashback.amount)}</strong>
                    <small>Cashback</small>
                  </div>
                </article>
              ))
            ) : (
              <Empty
                title="No cashback yet"
                message="Your verified purchases and earned cashback will appear here."
                action={
                  <ActionLink to="/customer/offers">Explore Offers</ActionLink>
                }
              />
            )}
          </Section>
        )}
      </Resource>
    </>
  );
}
