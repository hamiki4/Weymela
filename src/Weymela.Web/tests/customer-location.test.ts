import { describe, expect, it } from "vitest";
import type { Offer } from "../src/api/types";
import { haversineKm, nearestOffers, offerDistanceKm } from "../src/features/commerce/customerLocation";

const makeOffer = (id: string, latitude: number | null, longitude: number | null): Offer => ({
  id,
  source: "VIEW_AND_SALE_PROMOTION",
  offer: "Promotion",
  business: { displayName: id, directionsUrl: null, latitude, longitude },
  creator: null,
  benefitPercent: 1,
  watchUrl: null,
  slogan: null,
  location: null,
});

describe("Customer location distance", () => {
  it("uses Haversine distance in kilometers deterministically", () => {
    expect(haversineKm({ latitude: 0, longitude: 0 }, { latitude: 0, longitude: 1 }))
      .toBeCloseTo(111.19508, 4);
    expect(haversineKm({ latitude: 0, longitude: 0 }, { latitude: 0, longitude: 180 }))
      .toBeCloseTo(20015.114, 2);
  });

  it("rejects invalid and incomplete coordinates instead of showing a distance", () => {
    expect(haversineKm({ latitude: 91, longitude: 0 }, { latitude: 0, longitude: 0 })).toBeNull();
    expect(offerDistanceKm(makeOffer("unmapped", null, null), { latitude: 0, longitude: 0 })).toBeNull();
  });

  it("sorts calculable offers nearest-first and leaves coordinate-less offers visible last", () => {
    const rows = [
      makeOffer("far", 0, 1),
      makeOffer("unknown", null, null),
      makeOffer("near", 0, 0.1),
    ];
    expect(nearestOffers(rows, { latitude: 0, longitude: 0 }).map((row) => row.id))
      .toEqual(["near", "far", "unknown"]);
  });
});
