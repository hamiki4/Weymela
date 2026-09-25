using Weymela.Domain;
namespace Weymela.Application;
public enum ActorRole { PlatformAdmin, OperationsAdmin, Business, Creator, Customer, Cashier }
public sealed record Actor(Guid UserId, ActorRole Role, Guid? BusinessId = null, Guid? CreatorId = null, Guid? CustomerId = null);
public sealed record RealActor(Actor Identity)
{
    public Guid UserId => Identity.UserId;
    public ActorRole Role => Identity.Role;
}

public sealed record EffectiveSubject(Actor Identity, bool IsViewed)
{
    public Guid UserId => Identity.UserId;
    public ActorRole Role => Identity.Role;

    public static EffectiveSubject Real(RealActor actor) => new(actor.Identity, false);
    public static EffectiveSubject Viewed(Actor actor) => new(actor, true);
}

public enum AdministrativeCapability
{
    OperationsWorkspace,
    AccountReview,
    DepositReview,
    BusinessOperationalVisibility,
    CreatorOperationalVisibility,
    CustomerOperationalVisibility,
    CampaignOperationalVisibility,
    UgcOperationalVisibility,
    CreatorPayoutProcessing,
    CustomerPayoutProcessing,
    PlatformDashboard,
    PlatformFinancialReports,
    PlatformFinancialConfiguration,
    PlatformFinancialConfigurationHistory,
    PlatformSettlement,
    PlatformReconciliation,
    PlatformAccountManagement,
    PlatformRoleGrant,
    ProtectedPlatformVariables,
    ViewAs
}

public sealed record AdministrativeAuthority(ActorRole Role)
{
    public bool IsPlatformAdmin => Role == ActorRole.PlatformAdmin;
    public bool IsOperationsAdmin => Role == ActorRole.OperationsAdmin;
    public bool IsAdmin => IsPlatformAdmin || IsOperationsAdmin;
    public bool CanManagePlatform => IsPlatformAdmin;
    public bool CanStartViewAs => IsPlatformAdmin;

    public bool Allows(AdministrativeCapability capability) => IsPlatformAdmin ||
        IsOperationsAdmin && capability is
            AdministrativeCapability.OperationsWorkspace or
            AdministrativeCapability.AccountReview or
            AdministrativeCapability.DepositReview or
            AdministrativeCapability.BusinessOperationalVisibility or
            AdministrativeCapability.CreatorOperationalVisibility or
            AdministrativeCapability.CustomerOperationalVisibility or
            AdministrativeCapability.CampaignOperationalVisibility or
            AdministrativeCapability.UgcOperationalVisibility or
            AdministrativeCapability.CreatorPayoutProcessing or
            AdministrativeCapability.CustomerPayoutProcessing;

    public static AdministrativeAuthority For(RealActor actor) => new(actor.Role);

    public static bool CanBeViewedAs(ActorRole role) => role is
        ActorRole.Customer or ActorRole.Creator or ActorRole.Business or ActorRole.OperationsAdmin;
}

public static class AuthorityClaimTypes
{
    public const string RealActorUserId = "authority.real-actor-user-id";
    public const string ViewedUserId = "authority.viewed-user-id";
    public const string ViewedRole = "authority.viewed-role";
    public const string ViewedBusinessId = "authority.viewed-business-id";
    public const string ViewedCreatorId = "authority.viewed-creator-id";
    public const string ViewedCustomerId = "authority.viewed-customer-id";
    public const string EffectiveRole = "authority.effective-role";
    public const string SupportSessionId = "authority.support-session-id";

    public static bool IsReserved(string? type) => type is RealActorUserId or ViewedUserId or ViewedRole
        or ViewedBusinessId or ViewedCreatorId or ViewedCustomerId or EffectiveRole or SupportSessionId;
}

public sealed record SupportSessionContext(Guid SessionId, Guid RealActorUserId,
    EffectiveSubject ViewedSubject, DateTime ExpiresAtUtc)
{
    public bool IsActiveAt(DateTime nowUtc) => SessionId != Guid.Empty
        && RealActorUserId != Guid.Empty
        && ViewedSubject.IsViewed
        && ExpiresAtUtc.Kind == DateTimeKind.Utc
        && nowUtc.Kind == DateTimeKind.Utc
        && ExpiresAtUtc > nowUtc;
}

public sealed record AuthorityContext(RealActor RealActor, EffectiveSubject EffectiveSubject,
    AdministrativeAuthority Authority, SupportSessionContext? SupportSession)
{
    /// <summary>Commands must always use this actor, including during a future View As session.</summary>
    public Actor CommandActor => RealActor.Identity;
    public bool IsViewAsActive => SupportSession is not null;

    public static AuthorityContext ForAuthenticatedActor(Actor actor)
    {
        var realActor = new RealActor(actor);
        return new(realActor, EffectiveSubject.Real(realActor), AdministrativeAuthority.For(realActor), null);
    }

    /// <summary>
    /// Creates a context only after a server-side support session has been validated.
    /// The viewed subject never becomes the command actor.
    /// </summary>
    public static AuthorityContext ForValidatedViewAs(RealActor realActor, EffectiveSubject viewedSubject,
        SupportSessionContext supportSession, DateTime nowUtc)
    {
        if (!AdministrativeAuthority.For(realActor).CanStartViewAs
            || !viewedSubject.IsViewed
            || !AdministrativeAuthority.CanBeViewedAs(viewedSubject.Role)
            || supportSession.RealActorUserId != realActor.UserId
            || supportSession.ViewedSubject.Identity != viewedSubject.Identity
            || !supportSession.IsActiveAt(nowUtc))
            throw new ApplicationFailure(FailureKind.Forbidden, "The administrative support session is invalid.", code: "InvalidAuthorityContext");

        return new(realActor, viewedSubject, AdministrativeAuthority.For(realActor), supportSession);
    }
}
public enum FailureKind { Validation, InsufficientFunds, Forbidden, NotFound, ConcurrencyConflict, IdempotencyConflict }
public class ApplicationFailure(FailureKind kind, string message, Exception? innerException = null, string? code = null) : Exception(message, innerException)
{
    public FailureKind Kind { get; } = kind;
    public string? Code { get; } = code;
}
public sealed record IdempotencyRecord(string Key, string OperationType, Guid ActorId, string RequestFingerprint, object? ResultReference, DateTime CreatedAtUtc);
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct);
    Task<IdempotencyRecord?> FindAsync(string key, string operationType, Guid actorId, CancellationToken ct) => FindAsync(key, ct);
    Task SaveAsync(IdempotencyRecord record, CancellationToken ct);
}
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken ct);
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
public interface IEventPublisher { Task PublishAsync(IReadOnlyCollection<DomainEvent> events, CancellationToken ct); }
public sealed record PromotionConfigurationSnapshot(PricingSnapshot Pricing, int PromotionLiveDurationDays);
public interface IFinancialConfigurationResolver
{
    Task<PricingSnapshot> ResolveAsync(PromotionType type, DateTime at, CancellationToken ct);
    Task<PromotionConfigurationSnapshot> ResolvePromotionAsync(PromotionType type, DateTime at, CancellationToken ct);
}
public interface IPromotionRepository { Task<Promotion?> GetAsync(Guid id, CancellationToken ct); Task AddAsync(Promotion promotion, CancellationToken ct); Task SaveAsync(Promotion promotion, long expectedVersion, CancellationToken ct); Task<IReadOnlyList<Promotion>> QueryAsync(CancellationToken ct); }
public interface IWalletRepository { Task<BusinessWallet?> GetAsync(Guid businessId, CancellationToken ct); Task SaveAsync(BusinessWallet wallet, long expectedVersion, CancellationToken ct); }
public interface IBusinessWalletRepository : IWalletRepository { }
public interface ILegalAcceptanceGate { Task EnsureCurrentAcceptedAsync(Guid userId, LegalRole role, IReadOnlyCollection<LegalDocumentType> required, CancellationToken ct); }
public interface ICreatorEligibility { bool IsEligible(Guid creatorId, CreatorEligibilityCriteria criteria, CreatorVerifiedProfile profile); }
public sealed record CreatorVerifiedProfile(string? Category, string? Region, long VerifiedFollowers, bool SocialConnectionVerified);
