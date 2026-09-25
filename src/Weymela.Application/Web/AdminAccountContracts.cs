using Weymela.Application;

namespace Weymela.Application.Web;

public sealed record AccountPreauthorizationInput(
    string Role,
    string? Email,
    string? Phone,
    string DisplayName,
    string? PublicId = null,
    string? Region = null,
    string? Category = null,
    string? Submission = null,
    int? ExpiryDays = null,
    string? Reason = null);

public sealed record AccountPreauthorizationResult(
    Guid PreauthorizationId,
    Guid UserId,
    string Role,
    string Status,
    DateTime ExpiresAtUtc,
    bool ActivationInstructionsSent);

public sealed record AdminAccountSummary(
    Guid Id,
    Guid? UserId,
    string Name,
    string Role,
    string Status,
    string? ApprovalState,
    string SafeIdentifier,
    string? Association,
    DateTime? LastActivityAtUtc,
    bool CanManage);

public sealed record AdminAuditItem(
    Guid Id,
    string Operation,
    string Action,
    DateTime OccurredAtUtc,
    Guid ActorUserId,
    Guid? TargetUserId,
    string? TargetRole,
    Guid? TargetSubjectId,
    Guid CorrelationId);

public sealed record AdminAccountProfile(
    Guid SubjectId,
    string Role,
    string DisplayName,
    string PublicId,
    string? Region,
    string? Category,
    bool Active,
    Guid? BusinessId);

public sealed record AdminCommerceTransaction(
    Guid Id,
    DateTime OccurredAtUtc,
    decimal PurchaseAmount,
    string Currency,
    string Status,
    Guid BusinessId,
    Guid? CreatorId,
    Guid? CustomerId,
    Guid? CashierId);

public sealed record AdminBusinessData(
    decimal? AvailableWallet,
    decimal? ReservedWallet,
    int Promotions,
    int UgcOpportunities,
    int UgcCustomerOffers,
    int Cashiers,
    int Deposits,
    int Transactions);

public sealed record AdminCreatorSocialProfile(
    Guid Id,
    string Platform,
    string ProfileUrl,
    long SelfReportedAudience,
    string VerificationStatus,
    long? VerifiedAudience);

public sealed record AdminCreatorData(
    int PromotionRequests,
    int Allocations,
    int ContentSubmissions,
    int LiveParticipations,
    decimal? Earnings,
    int Payouts,
    IReadOnlyList<AdminCreatorSocialProfile> SocialProfiles);

public sealed record AdminCustomerData(
    decimal? Cashback,
    int Purchases,
    int QrHistory,
    int Payouts);

public sealed record AdminCashierData(
    Guid? BusinessId,
    string? BusinessName,
    string ActivationState,
    int TransactionsProcessed);

public sealed record AdminAdminData(string Role, bool Active, DateTime? GrantedAtUtc, int RoleHistoryEntries);

public sealed record AdminAccountDetail(
    AdminAccountSummary Account,
    IReadOnlyList<string> Roles,
    IReadOnlyList<AdminAccountProfile> Profiles,
    IReadOnlyList<AdminAuditItem> Audit,
    IReadOnlyList<AdminCommerceTransaction> Transactions,
    IReadOnlyList<object> Activity,
    object? RoleData = null);

public sealed record AccountLifecycleInput(string Action, string Reason, long? ExpectedVersion = null);
public sealed record RevokeAccountProfileInput(string Role, Guid? SubjectId, string Reason, long? ExpectedVersion = null);

public sealed record AdminAccountFilterInput(string? Role = null, string? Status = null, string? Search = null, string? Approval = null);
