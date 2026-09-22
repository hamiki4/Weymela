import type { Offer } from "../../api/types";

export interface Coordinates {
  latitude: number;
  longitude: number;
}

export function validCoordinates(value: Coordinates | null | undefined): value is Coordinates {
  return Boolean(
    value &&
      Number.isFinite(value.latitude) &&
      Number.isFinite(value.longitude) &&
      value.latitude >= -90 &&
      value.latitude <= 90 &&
      value.longitude >= -180 &&
      value.longitude <= 180,
  );
}

export function haversineKm(from: Coordinates, to: Coordinates): number | null {
  if (!validCoordinates(from) || !validCoordinates(to)) return null;
  const radians = (degrees: number) => (degrees * Math.PI) / 180;
  const latitudeDelta = radians(to.latitude - from.latitude);
  const longitudeDelta = radians(to.longitude - from.longitude);
  const a =
    Math.sin(latitudeDelta / 2) ** 2 +
    Math.cos(radians(from.latitude)) *
      Math.cos(radians(to.latitude)) *
      Math.sin(longitudeDelta / 2) ** 2;
  const bounded = Math.min(1, Math.max(0, a));
  return 6371.0088 * 2 * Math.atan2(Math.sqrt(bounded), Math.sqrt(1 - bounded));
}

export function offerDistanceKm(offer: Offer, from: Coordinates | null): number | null {
  if (!from) return null;
  const { latitude, longitude } = offer.business;
  if (latitude === null || longitude === null) return null;
  return haversineKm(from, { latitude, longitude });
}

export function nearestOffers(offers: Offer[], from: Coordinates): Offer[] {
  return offers
    .map((offer, index) => ({ offer, index, distance: offerDistanceKm(offer, from) }))
    .sort((a, b) => {
      if (a.distance === null) return b.distance === null ? a.index - b.index : 1;
      if (b.distance === null) return -1;
      return a.distance - b.distance || a.index - b.index;
    })
    .map(({ offer }) => offer);
}
