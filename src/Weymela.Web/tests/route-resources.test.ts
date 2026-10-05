import { describe, expect, it } from "vitest";
import { routeResources } from "../src/app/routeResources";

describe("authenticated route resource contracts", () => {
  it.each([
    ["/business/campaigns/new", ["/business/wallet", "/business/pricing", "/business/ugc-pricing"]],
    ["/business/ugc", ["/business/campaigns", "/business/ugc", "/business/promotion-content-submissions"]],
    ["/business/ugc/new", ["/business/wallet", "/business/ugc-pricing"]],
    ["/creator", ["/creator/home", "/creator/campaigns", "/creator/discover", "/creator/ugc"]],
    ["/creator/ugc", ["/creator/discover", "/creator/ugc"]],
    ["/creator/ugc/ugc-1", ["/creator/ugc/ugc-1"]],
    ["/creator/campaigns", ["/creator/campaigns", "/creator/requests", "/creator/ugc/assignments", "/creator/ugc/requests"]],
    ["/creator/requests", ["/creator/campaigns", "/creator/requests", "/creator/ugc/assignments", "/creator/ugc/requests"]],
    ["/creator/campaigns/campaign-1", ["/creator/campaigns"]],
  ])("prefetches the data needed by %s", (pathname, expected) => {
    expect(routeResources(pathname)).toEqual(expected);
  });
});
