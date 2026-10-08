import type { Offer } from "../../api/types";
import { validCoordinates } from "./customerLocation";

const googleMapsHosts = new Set([
  "google.com",
  "www.google.com",
  "maps.google.com",
  "maps.app.goo.gl",
  "goo.gl",
]);

function savedDirectionsUrl(value: string | null | undefined): string | undefined {
  try {
    const url = new URL(value ?? "");
    return url.protocol === "https:" && !url.username && !url.password && googleMapsHosts.has(url.hostname.toLowerCase())
      ? url.href
      : undefined;
  } catch {
    return undefined;
  }
}

function googleDirections(destination: string): string {
  const url = new URL("https://www.google.com/maps/dir/");
  url.searchParams.set("api", "1");
  url.searchParams.set("destination", destination);
  return url.href;
}

/** Builds directions from saved Business/offer data only. It never reads Customer geolocation. */
export function offerDirectionsUrl(offer: Offer): string | undefined {
  const saved = savedDirectionsUrl(offer.business.directionsUrl);
  if (saved) return saved;

  const coordinates = offer.business.latitude !== null && offer.business.longitude !== null
    ? { latitude: offer.business.latitude, longitude: offer.business.longitude }
    : null;
  if (validCoordinates(coordinates))
    return googleDirections(`${coordinates.latitude},${coordinates.longitude}`);

  const address = offer.business.address?.trim() || offer.location?.trim();
  return address ? googleDirections(address) : undefined;
}
