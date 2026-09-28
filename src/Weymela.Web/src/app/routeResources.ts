/** First-paint resources for authenticated product routes. Detail pages retain
 * their existing loaders; this list covers the persistent mobile navigation. */
export function routeResources(pathname: string): string[] {
  switch (pathname) {
    case "/business": return ["/business/home", "/business/ugc"];
    case "/business/campaigns": return ["/business/campaigns", "/business/promotion-content-submissions"];
    case "/business/requests": return ["/business/campaigns"];
    case "/business/ugc": return ["/business/ugc", "/business/wallet"];
    case "/business/ugc/new": return ["/legal/current", "/business/wallet", "/business/ugc-pricing"];
    case "/business/legal": return ["/legal/current"];
    case "/business/wallet": return ["/business/wallet", "/business/deposit-method", "/business/deposit-requests"];
    case "/business/pricing": return ["/business/pricing", "/business/ugc-pricing"];
    case "/business/cashiers": return ["/business/cashiers"];
    case "/business/campaigns/new": return ["/business/pricing"];
    case "/creator": return ["/creator/home", "/creator/campaigns"];
    case "/creator/discover": return ["/creator/discover", "/creator/ugc"];
    case "/creator/promotions": return ["/creator/campaigns", "/creator/requests", "/creator/ugc/assignments", "/creator/ugc/requests"];
    case "/creator/earnings": return ["/creator/earnings"];
    case "/profile": return ["/profile"];
    case "/creator/profile": return ["/profile"];
    case "/customer/offers": return ["/customer/offers", "/customer/cashback", "/customer/transactions"];
    case "/customer/discover": return ["/customer/offers"];
    case "/customer/cashback": return ["/customer/cashback"];
    case "/customer/transactions": return ["/customer/transactions"];
    case "/checkout":
    case "/checkout/transactions": return ["/checkout/recent"];
    case "/notifications": return ["/notifications"];
    default:
      if (/^\/business\/campaigns\/[^/]+$/.test(pathname)) return [pathname, "/business/wallet"];
      if (/^\/creator\/discover\/[^/]+$/.test(pathname)) return [pathname];
      if (/^\/creator\/promotions\/[^/]+$/.test(pathname)) return ["/creator/campaigns"];
      if (/^\/customer\/offers\/[^/]+$/.test(pathname)) return ["/customer/offers"];
      return [];
  }
}
