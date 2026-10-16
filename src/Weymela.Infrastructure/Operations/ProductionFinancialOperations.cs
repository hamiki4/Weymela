using Weymela.Infrastructure.Identity;

namespace Weymela.Infrastructure.Operations;

// These are operator controls, separate from versioned prices and transaction authorization.
// Enabling a route never replaces the product's provider evidence or financial checks.
public static class ProductionFinancialOperations
{
    public const string DepositRequests = "DepositRequests";
    public const string DepositReview = "DepositReview";
    public const string WalletFunding = "WalletFunding";
    public const string PromotionReservations = "PromotionReservations";
    public const string Checkout = "Checkout";
    public const string Payouts = "Payouts";
    public const string ViewRewards = "ViewRewards";
    public static bool Known(string operation) => operation is DepositRequests or DepositReview or WalletFunding
        or PromotionReservations or Checkout or Payouts or ViewRewards;
    public static bool Valid(string? reference, string[] operations) =>
        PlatformAdminBootstrapTarget.IsValidProductionAuthorizationReference(reference)
        && operations.Length > 0 && operations.All(Known)
        && operations.Distinct(StringComparer.Ordinal).Count() == operations.Length;
}
