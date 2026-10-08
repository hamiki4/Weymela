import { describe, expect, it } from "vitest";
import type { Offer } from "../src/api/types";
import { offerDirectionsUrl } from "../src/features/commerce/customerDirections";

const base: Offer = {
  id: "offer",
  source: "VIEW_AND_SALE_PROMOTION",
  offer: "Coffee",
  business: {
    displayName: "Coffee Business",
    directionsUrl: null,
    latitude: null,
    longitude: null,
    address: null,
  },
  creator: null,
  benefitPercent: 2,
  watchUrl: null,
  slogan: null,
  location: null,
};

describe("Customer offer directions", () => {
  it("uses the saved HTTPS Google Maps link", () => {
    const url = offerDirectionsUrl({ ...base, business: { ...base.business, directionsUrl: "https://maps.app.goo.gl/AbCd1234" } });
    expect(url).toBe("https://maps.app.goo.gl/AbCd1234");
  });

  it("builds directions from the saved coordinate pair without a Customer origin", () => {
    const url = new URL(offerDirectionsUrl({ ...base, business: { ...base.business, latitude: 9.03, longitude: 38.74 } })!);
    expect(url.origin + url.pathname).toBe("https://www.google.com/maps/dir/");
    expect(url.searchParams.get("destination")).toBe("9.03,38.74");
    expect(url.searchParams.has("origin")).toBe(false);
  });

  it("uses the saved Business address, then the Business-entered offer location", () => {
    const address = new URL(offerDirectionsUrl({ ...base, business: { ...base.business, address: "Bole Road, Addis Ababa" } })!);
    expect(address.searchParams.get("destination")).toBe("Bole Road, Addis Ababa");
    const offerLocation = new URL(offerDirectionsUrl({ ...base, location: "Addis Ababa" })!);
    expect(offerLocation.searchParams.get("destination")).toBe("Addis Ababa");
  });

  it("omits directions when no valid saved location exists", () => {
    expect(offerDirectionsUrl(base)).toBeUndefined();
    expect(offerDirectionsUrl({ ...base, business: { ...base.business, directionsUrl: "https://example.com/not-maps" } })).toBeUndefined();
  });
});
