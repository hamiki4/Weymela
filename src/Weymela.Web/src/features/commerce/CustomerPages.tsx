import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import QRCode from "qrcode";
import { post, request, useAction, useResource } from "../../api/client";
import type { CustomerCashbackSummary as CashbackSummary, CustomerTransaction, Offer, Qr } from "../../api/types";
import { useSession } from "../../app/Session";
import {
  ActionLink,
  Button,
  Empty,
  Notice,
  PageHeader,
  Resource,
  Section,
} from "../../ui/components";
import { amount, dateTime, safeExternal } from "../../ui/format";
import { Icon } from "../../ui/Icon";
import { Coordinates, nearestOffers, offerDistanceKm, validCoordinates } from "./customerLocation";

type CustomerOffersProps = { discover?: boolean };

const sourceLabel = (source: string) =>
  source === "UGC_CUSTOMER_OFFER" ? "Customer offer" : "Creator promotion";

const sourceFilterLabel = (source: string) =>
  source === "UGC_CUSTOMER_OFFER" ? "Customer offers" : "Creator picks";

const benefitLabel = (offer: Offer) =>
  `${amount(offer.benefitPercent)}% ${offer.source === "UGC_CUSTOMER_OFFER" ? "off" : "cashback"}`;

const initial = (value: string) => value.trim().slice(0, 1).toUpperCase() || "W";

const CUSTOMER_LOCATION_KEY = "weymela.customer-location";

function useCustomerLocation() {
  const [coordinates, setCoordinates] = useState<Coordinates | null>(() => {
    try {
      const stored = sessionStorage.getItem(CUSTOMER_LOCATION_KEY);
      const parsed = stored ? JSON.parse(stored) as Coordinates : null;
      return validCoordinates(parsed) ? parsed : null;
    } catch {
      return null;
    }
  });
  const [error, setError] = useState<string | null>(null);

  const requestLocation = () => {
    setError(null);
    if (!navigator.geolocation) {
      setError("Location isn’t available in this browser. You can still browse promotions.");
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (position) => {
        const next = {
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
        };
        if (!validCoordinates(next)) {
          setError("We couldn’t verify your location. Promotions are still available.");
          return;
        }
        setCoordinates(next);
        try {
          sessionStorage.setItem(CUSTOMER_LOCATION_KEY, JSON.stringify(next));
        } catch {
          // Location remains usable for this page even if session storage is unavailable.
        }
      },
      (failure) => {
        setError(failure.code === failure.PERMISSION_DENIED
          ? "Location access is off. You can still browse and filter promotions by location."
          : "We couldn’t get your location. Promotions are still available; try again when ready.");
      },
      { enableHighAccuracy: false, maximumAge: 30_000, timeout: 10_000 },
    );
  };

  return { coordinates, error, requestLocation };
}

function CustomerLocationCard({
  discover,
  coordinates,
  error,
  onUseLocation,
}: {
  discover: boolean;
  coordinates: Coordinates | null;
  error: string | null;
  onUseLocation: () => void;
}) {
  return (
    <section className="customer-location-card" aria-label="Promotion location">
      <span className="customer-location-icon" aria-hidden="true"><Icon name="location" size={23} /></span>
      <div className="customer-location-copy">
        <h2>{discover ? "Find promotions near you" : "See what is close to you"}</h2>
        <p>Use your location to sort eligible live promotions by distance.</p>
        {coordinates && <p className="customer-location-note">Location is on. Offers without Business coordinates remain available without a distance.</p>}
        {error && <p className="customer-location-error" role="status">{error}</p>}
      </div>
      <Button variant="secondary" icon="location" onClick={onUseLocation}>Use Location</Button>
    </section>
  );
}

function CustomerPromotionCard({ offer, distanceKm }: { offer: Offer; distanceKm: number | null }) {
  const watchUrl = safeExternal(offer.watchUrl);
  const directionsUrl = safeExternal(offer.business.directionsUrl);
  const headingId = `customer-offer-${offer.id}`;
  const customerFacingTitle = offer.slogan?.trim() || offer.offer;

  return (
    <article className="customer-promotion-card" aria-labelledby={headingId}>
      <header className="customer-promotion-header">
        <div className="customer-promotion-business">
          <span className="customer-business-avatar" aria-hidden="true">
            {initial(offer.business.displayName)}
          </span>
          <div>
            <span className="customer-promotion-source">
              {sourceLabel(offer.source)}
            </span>
            <h2 id={headingId}>{offer.business.displayName}</h2>
          </div>
        </div>
        <span className="customer-benefit-badge">{benefitLabel(offer)}</span>
      </header>

      <p className="customer-promotion-title">{customerFacingTitle}</p>

      <div className="customer-promotion-meta">
        {offer.location && (
          <span>
            <Icon name="location" size={16} />
            {offer.location}
          </span>
        )}
        {distanceKm !== null && (
          <span><Icon name="location" size={16} />{distanceKm < 0.05 ? "Under 0.1 km" : `${distanceKm.toFixed(1)} km`}</span>
        )}
        {offer.source === "VIEW_AND_SALE_PROMOTION" && offer.remainingDays != null && offer.remainingDays > 0 && (
          <span className="customer-live-days">{offer.remainingDays} days left</span>
        )}
        {offer.creator && (
          <span className="customer-promotion-creator">
            <span className="customer-creator-avatar" aria-hidden="true">
              {initial(offer.creator.displayName)}
            </span>
            By {offer.creator.displayName}
          </span>
        )}
      </div>

      <div className="customer-promotion-actions">
        {watchUrl && (
          <a
            className="button secondary"
            href={watchUrl}
            target="_blank"
            rel="noreferrer"
          >
            <Icon name="video" />
            Watch Promotion
          </a>
        )}
        {directionsUrl && (
          <a
            className="button secondary"
            href={directionsUrl}
            target="_blank"
            rel="noreferrer"
          >
            <Icon name="location" />
            Get Directions
          </a>
        )}
        <ActionLink to={`/customer/offers/${offer.id}`} icon="qr">
          Get Offer
        </ActionLink>
      </div>
    </article>
  );
}

function CustomerOfferBrowser({
  rows,
  discover,
  coordinates,
}: {
  rows: Offer[];
  discover: boolean;
  coordinates: Coordinates | null;
}) {
  const [locationFilter, setLocationFilter] = useState("all");
  const [sourceFilter, setSourceFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<"recommended" | "nearest">("recommended");
  const [view, setView] = useState<"list" | "map">("list");
  const locations = useMemo(
    () =>
      Array.from(
        new Set(
          rows
            .map((row) => row.location?.trim())
            .filter((location): location is string => Boolean(location)),
        ),
      ).sort((a, b) => a.localeCompare(b)),
    [rows],
  );
  const sources = useMemo(
    () => Array.from(new Set(rows.map((row) => row.source))),
    [rows],
  );
  const filteredRows = rows.filter((row) => {
    const normalizedSearch = search.trim().toLocaleLowerCase();
    const matchesSearch = !normalizedSearch ||
      row.business.displayName.toLocaleLowerCase().includes(normalizedSearch) ||
      row.creator?.displayName.toLocaleLowerCase().includes(normalizedSearch);
    return matchesSearch &&
      (locationFilter === "all" || row.location?.trim() === locationFilter) &&
      (sourceFilter === "all" || row.source === sourceFilter);
  });
  const visibleRows = sort === "nearest" && coordinates
    ? nearestOffers(filteredRows, coordinates)
    : filteredRows;

  return (
    <>
      <div className="customer-filter-bar" aria-label="Promotion filters">
        {discover && (
          <label className="customer-filter-control customer-search-control">
            <Icon name="search" size={17} />
            <span className="sr-only">Search business or creator</span>
            <input
              type="search"
              aria-label="Search business or creator"
              placeholder="Search business or creator"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </label>
        )}
        <label className="customer-filter-control">
          <Icon name="location" size={17} />
          <span className="sr-only">Filter by city</span>
          <select
            aria-label="Filter by city"
            value={locationFilter}
            onChange={(event) => setLocationFilter(event.target.value)}
          >
            <option value="all">All locations</option>
            {locations.map((location) => (
              <option key={location} value={location}>
                {location}
              </option>
            ))}
          </select>
        </label>
        <label className="customer-filter-control">
          <Icon name="settings" size={17} />
          <span className="sr-only">Filter promotions</span>
          <select
            aria-label="Filter promotions"
            value={sourceFilter}
            onChange={(event) => setSourceFilter(event.target.value)}
          >
            <option value="all">All promotions</option>
            {sources.map((source) => (
              <option key={source} value={source}>
                {sourceFilterLabel(source)}
              </option>
            ))}
          </select>
        </label>
        {discover && (
          <label className="customer-filter-control">
            <Icon name="location" size={17} />
            <span className="sr-only">Sort promotions</span>
            <select aria-label="Sort promotions" value={sort} onChange={(event) => setSort(event.target.value as "recommended" | "nearest")}>
              <option value="recommended">Recommended</option>
              <option value="nearest">Nearest</option>
            </select>
          </label>
        )}
      </div>

      {discover && !coordinates && (
        <div className="customer-location-off-note">
          <p>Location access is off. Enable location to see promotions near you.</p>
        </div>
      )}

      {sources.length > 1 && (
        <div className="customer-category-chips" role="group" aria-label="Promotion types">
          <button
            className={`customer-category-chip ${sourceFilter === "all" ? "active" : ""}`}
            type="button"
            aria-pressed={sourceFilter === "all"}
            onClick={() => setSourceFilter("all")}
          >
            All
          </button>
          {sources.map((source) => (
            <button
              className={`customer-category-chip ${sourceFilter === source ? "active" : ""}`}
              key={source}
              type="button"
              aria-pressed={sourceFilter === source}
              onClick={() => setSourceFilter(source)}
            >
              {sourceFilterLabel(source)}
            </button>
          ))}
        </div>
      )}

      <section className="customer-offer-section" aria-labelledby="customer-offers-title">
        <div className="customer-offer-section-heading">
          <div>
            <p className="eyebrow">Promotions</p>
            <h2 id="customer-offers-title">{discover ? "Discover Promotions" : "Recommended Promotions"}</h2>
          </div>
          <span className="customer-offer-count">
            {visibleRows.length} {visibleRows.length === 1 ? "offer" : "offers"}
          </span>
        </div>
        {discover && (
          <div className="customer-view-switch" role="group" aria-label="Promotion view">
            <button type="button" aria-pressed={view === "list"} onClick={() => setView("list")}>List</button>
            <button type="button" aria-pressed={view === "map"} onClick={() => setView("map")}>Map</button>
          </div>
        )}
        {view === "map" ? (
          <Empty
            icon="location"
            title="Map view isn’t configured yet"
            message="A map provider hasn’t been configured. You can still browse eligible offers and sort by calculated distance."
          />
        ) : visibleRows.length ? (
          <div className="customer-promotion-grid">
            {visibleRows.map((offer) => (
              <CustomerPromotionCard key={offer.id} offer={offer} distanceKm={offerDistanceKm(offer, coordinates)} />
            ))}
          </div>
        ) : (
          <Empty
            title="No promotions match that filter"
            message="Try another location or browse all available promotions."
            action={
              <Button
                variant="secondary"
                onClick={() => {
                  setLocationFilter("all");
                  setSourceFilter("all");
                  setSearch("");
                }}
              >
                Clear filters
              </Button>
            }
          />
        )}
      </section>
    </>
  );
}

export function CustomerOffers({ discover = false }: CustomerOffersProps = {}) {
  const { user } = useSession();
  const customerLocation = useCustomerLocation();
  const resource = useResource<Offer[]>("/customer/offers");
  const name = user?.displayName?.trim();
  const firstName = name ? name.split(/\s+/)[0] : "there";

  return (
    <div className="customer-home-page">
      {discover ? (
        <PageHeader
          title="Discover Promotions"
        />
      ) : (
        <header className="customer-home-hero">
          <div>
            <h1>Offers for {firstName}</h1>
          </div>
          <span className="customer-hero-mark" aria-hidden="true">
            <Icon name="sparkle" size={32} />
          </span>
        </header>
      )}
      <CustomerLocationCard
        discover={discover}
        coordinates={customerLocation.coordinates}
        error={customerLocation.error}
        onUseLocation={customerLocation.requestLocation}
      />
      <Resource resource={resource}>
        {(rows) => (
          <CustomerOfferBrowser
            rows={rows}
            discover={discover}
            coordinates={customerLocation.coordinates}
          />
        )}
      </Resource>
    </div>
  );
}

export function CustomerDiscover() {
  return <CustomerOffers discover />;
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
      {offer.creator ? (
        <p>By {offer.creator.displayName}</p>
      ) : (
        <p>Available from this Business</p>
      )}
      <span className="cashback">{benefitLabel(offer)}</span>
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
            {action.busy ? "Preparing offer…" : "Get Offer"}
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
          const offer = rows.find((row) => row.id === id);
          return offer ? (
            <QrPanel key={offer.id} offer={offer} />
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

export function CustomerTransactions() {
  const resource = useResource<CustomerTransaction[]>("/customer/transactions");
  return (
    <div className="customer-ledger-page">
      <PageHeader
        eyebrow="Your purchase activity"
        title="Transactions"
        description="Purchases confirmed at participating Businesses."
      />
      <Resource resource={resource}>
        {(rows) => rows.length ? (
          <section className="customer-ledger-list" aria-label="Customer transactions">
            {rows.map((row, index) => {
              const ugc = row.source === "UGC_CUSTOMER_OFFER";
              return (
                <article className="customer-transaction-card" key={`${row.source}-${row.purchasedAtUtc}-${index}`}>
                  <header>
                    <div>
                      <span className="customer-transaction-source">{ugc ? "Customer Offer" : "View + Sale Promotion"}</span>
                      <h2>{row.business}</h2>
                    </div>
                    <time dateTime={row.purchasedAtUtc}>{dateTime(row.purchasedAtUtc)}</time>
                  </header>
                  <p className="customer-transaction-offer">{row.offer}</p>
                  {row.creator && <p className="customer-transaction-creator">Promoted by {row.creator}</p>}
                  <div className="customer-transaction-values">
                    {ugc ? (
                      <>
                        <div><small>Original purchase</small><strong>{amount(row.purchaseAmount.amount)}</strong></div>
                        <div><small>Discount</small><strong>{row.discountReceived ? `−${amount(row.discountReceived.amount)}` : "—"}</strong></div>
                        <div><small>Paid</small><strong>{row.customerPaidAmount ? amount(row.customerPaidAmount.amount) : "—"}</strong></div>
                      </>
                    ) : (
                      <>
                        <div><small>Paid</small><strong>{amount(row.purchaseAmount.amount)}</strong></div>
                        <div><small>Cashback earned</small><strong>{row.cashbackEarned ? `+${amount(row.cashbackEarned.amount)}` : "—"}</strong></div>
                      </>
                    )}
                  </div>
                </article>
              );
            })}
          </section>
        ) : (
          <Empty
            title="No transactions yet"
            message="Confirmed purchases will appear here after checkout."
            action={<ActionLink to="/customer/offers">Explore Offers</ActionLink>}
          />
        )}
      </Resource>
    </div>
  );
}

const cashbackStatusLabel = (status: CashbackSummary["status"]) => {
  if (status === "PayoutPrepared") return "Payout prepared";
  if (status === "Eligible") return "Eligible to cash out";
  return "Below minimum cash-out";
};

export function CustomerCashback() {
  const resource = useResource<CashbackSummary>("/customer/cashback");
  return (
    <div className="customer-ledger-page">
      <PageHeader
        eyebrow="Your cashback balance"
        title="Cashback"
        description="See what is available and review recorded payouts."
      />
      <Resource resource={resource}>
        {(summary) => {
          const available = summary.availableCashback.amount;
          const threshold = summary.minimumCashOut.amount;
          const progress = threshold > 0 ? Math.min(100, (available / threshold) * 100) : 0;
          return (
            <>
              <section className="customer-cashback-summary" aria-label="Cashback summary">
                <div className="customer-cashback-available">
                  <span>Available Cashback</span>
                  <strong>{amount(available)}</strong>
                  <small>{cashbackStatusLabel(summary.status)}</small>
                </div>
                <div className="customer-cashback-progress">
                  <div className="customer-cashback-threshold">
                    <span>Minimum cash-out</span>
                    <strong>{amount(threshold)}</strong>
                  </div>
                  <div className="customer-cashback-track" role="progressbar" aria-label="Progress toward minimum cash-out" aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress}>
                    <span style={{ width: `${progress}%` }} />
                  </div>
                  <p>{summary.eligible ? "You’ve reached the current minimum." : `${amount(summary.remainingToCashOut.amount)} remaining to cash out.`}</p>
                </div>
              </section>
              <section className="customer-payout-history" aria-labelledby="customer-payout-title">
                <div className="customer-ledger-section-heading">
                  <div><p className="eyebrow">Authoritative payout records</p><h2 id="customer-payout-title">Payout history</h2></div>
                </div>
                {summary.payoutHistory.length ? summary.payoutHistory.map((payout, index) => (
                  <article className="customer-payout-row" key={`${payout.eligibleAtUtc}-${index}`}>
                    <div>
                      <strong>{payout.status === "Paid" ? "Paid out" : "Payout prepared"}</strong>
                      <small>{dateTime(payout.paidAtUtc ?? payout.eligibleAtUtc)}</small>
                    </div>
                    <strong>{amount(payout.amount.amount)}</strong>
                  </article>
                )) : (
                  <Empty title="No payouts recorded yet" message="When a payout is recorded, its status and date will appear here." />
                )}
              </section>
            </>
          );
        }}
      </Resource>
    </div>
  );
}
