using Weymela.Infrastructure.Identity;

namespace Weymela.Infrastructure.Operations;

// These are operator controls, separate from versioned prices and transaction authorization.
// View rewards have no approved live provider and deliberately cannot be enabled here.
public static class ProductionFinancialOperations
{
    public const string DepositRequests = "DepositRequests";
    public const string WalletFunding = "WalletFunding";
    public const string PromotionReservations = "PromotionReservations";
    public const string Checkout = "Checkout";
    public const string Payouts = "Payouts";
    public const string ViewRewards = "ViewRewards";
    public static bool Known(string operation) => operation is DepositRequests or WalletFunding
        or PromotionReservations or Checkout or Payouts;
    public static bool Valid(string? reference, string[] operations) =>
        PlatformAdminBootstrapTarget.IsValidProductionAuthorizationReference(reference)
        && operations.Length > 0 && operations.All(Known)
        && operations.Distinct(StringComparer.Ordinal).Count() == operations.Length;
}
