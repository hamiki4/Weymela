namespace Weymela.Domain;

public readonly record struct Money
{
    public decimal Amount { get; } public string Currency { get; }
    public Money(decimal amount, string currency = "ETB") { if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required."); if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount)); Amount = amount; Currency = currency.Trim().ToUpperInvariant(); }
    public static Money Zero(string currency = "ETB") => new(0m, currency);
    public Money Add(Money other) { Same(other); return new(Amount + other.Amount, Currency); }
    public Money Subtract(Money other) { Same(other); if (other.Amount > Amount) throw new InvalidOperationException("Money cannot become negative."); return new(Amount - other.Amount, Currency); }
    private void Same(Money other) { if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Currency mismatch."); }
    public static implicit operator Money(decimal amount) => new(amount);
}

public enum PromotionType { ViewOnly, ViewPlusCommission }
public enum PromotionStatus { Draft, Funded, Published, Active, BudgetExhausted, Completed, Cancelled }
public enum CreatorPlatform { TikTok, Instagram, YouTube, Facebook }
public enum CreatorApplicationStatus { Pending, Approved, Rejected, Withdrawn }
public enum CreatorAllocationStatus { Active, Exhausted, Completed, Cancelled }
public enum VerifiedSaleStatus { Recorded, Reversed }
public enum EarningSource { ViewReward, SaleCommission, Ugc, AuthorizedAdjustment }
public enum CashbackSource { VerifiedSale, AuthorizedAdjustment }
public enum PlatformRevenueSource { ViewRewardPlatformShare, SalePlatformShare, UgcFee, UgcCustomerOfferSaleFee, AuthorizedAdjustment }
public enum RevenueStatus { Accrued, Settled }
public enum JournalLineType { Debit, Credit }
public enum JournalSourceType { Deposit, PromotionReservation, UgcReservation, UgcCustomerOfferReservation, Allocation, UgcApproval, ViewReward, VerifiedSale, UgcCustomerOfferSale, Payout, Adjustment, Settlement }
public enum LegalRole { Account, Business, Creator }
public enum LegalDocumentType { TermsOfService, BusinessAgreement, CreatorAgreement, AntiCircumventionAgreement, PrivacyPolicy }

public abstract record DomainEvent;
public sealed record BusinessWalletCredited(Guid BusinessId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record PromotionFunded(Guid PromotionId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record PromotionPublished(Guid PromotionId, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CreatorApplied(Guid PromotionId, Guid CreatorId, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CreatorApplicationApproved(Guid ApplicationId, Guid OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CreatorAllocationCreated(Guid AllocationId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record PromotionActivated(Guid PromotionId, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record VerifiedViewsRecorded(Guid PromotionId, Guid CreatorId, long RewardedViews, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record ViewRewardEarned(Guid AllocationId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record VerifiedSaleRecorded(Guid SaleId, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CreatorCommissionEarned(Guid CreatorId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CustomerCashbackEarned(Guid CustomerId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record PlatformRevenueEarned(Guid BusinessId, Money Amount, PlatformRevenueSource Source, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record PromotionBudgetExhausted(Guid PromotionId, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record PromotionCompleted(Guid PromotionId, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CreatorPayoutEligible(Guid CreatorId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;
public sealed record CustomerPayoutEligible(Guid CustomerId, Money Amount, DateTime OccurredAtUtc, Guid CorrelationId) : DomainEvent;

public sealed class BusinessWallet
{
    private BusinessWallet() { }
    public long Version { get; private set; }
    private readonly List<DomainEvent> events = [];
    public Guid Id { get; } = Guid.NewGuid(); public Guid BusinessId { get; }
    public Money AvailableBalance { get; private set; } public Money ReservedBalance { get; private set; }
    public Money TotalBalance => AvailableBalance.Add(ReservedBalance);
    public IReadOnlyList<DomainEvent> DomainEvents => events;
    public BusinessWallet(Guid businessId, string currency = "ETB") { BusinessId = businessId; AvailableBalance = Money.Zero(currency); ReservedBalance = Money.Zero(currency); }
    public void CreditDeposit(Money amount, DateTime at, Guid correlation) { Positive(amount); AvailableBalance = AvailableBalance.Add(amount); events.Add(new BusinessWalletCredited(BusinessId, amount, at, correlation)); }
    public void ReserveForPromotion(Money amount, DateTime at, Guid correlation) { Positive(amount); AvailableBalance = AvailableBalance.Subtract(amount); ReservedBalance = ReservedBalance.Add(amount); }
    public void ReleasePromotionReserve(Money amount, DateTime at, Guid correlation) { Positive(amount); ReservedBalance = ReservedBalance.Subtract(amount); AvailableBalance = AvailableBalance.Add(amount); }
    public void ConsumeReservedFunds(Money amount, DateTime at, Guid correlation) { Positive(amount); ReservedBalance = ReservedBalance.Subtract(amount); }
    public void Reserve(Money amount, DateTime at, Guid correlation) => ReserveForPromotion(amount, at, correlation);
    public void ReleaseReserve(Money amount, DateTime at, Guid correlation) => ReleasePromotionReserve(amount, at, correlation);
    public void ApplyAuthorizedAdjustment(Money amount, bool credit, DateTime at, Guid correlation) { Positive(amount); if (credit) AvailableBalance = AvailableBalance.Add(amount); else AvailableBalance = AvailableBalance.Subtract(amount); }
    private static void Positive(Money amount) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); }
}

public sealed record PricingSnapshot(PromotionType PromotionType, int ViewsPerReward, Money BusinessCharge, Money CreatorEarning, Money PlatformEarning, decimal CreatorCommissionPercent, decimal CustomerCashbackPercent, decimal PlatformPercent, DateTime EffectiveFromUtc, Guid ConfigurationVersionId, Money? MinimumPromotionBudget = null)
{ public bool IsValid => ViewsPerReward > 0 && CreatorCommissionPercent >= 0 && CustomerCashbackPercent >= 0 && PlatformPercent >= 0; }
public sealed record CreatorEligibilityCriteria(string? Category, long? MinimumVerifiedFollowers, string? Market, string? Requirements);

public sealed class Promotion
{
    private Promotion() { Title = null!; Description = null!; Eligibility = null!; PricingSnapshot = null!; }
    private readonly List<DomainEvent> events = [];
    private readonly List<CreatorAllocation> allocations = [];
    private readonly List<PromotionPlatform> platforms = [];
    public Guid Id { get; } = Guid.NewGuid(); public string PublicPromotionId { get; } = $"PROM-{Guid.NewGuid():N}"[..13]; public Guid BusinessId { get; }
    public string Title { get; private set; } public string Description { get; private set; } public string? Slogan { get; private set; }
    public string? Location { get; private set; } public string? ResourcesJson { get; private set; }
    public PromotionType PromotionType { get; }
    public Money TotalBudget { get; } public Money ReservedBudget { get; private set; } public Money UsedBudget { get; private set; }
    // Active/exhausted allocations remain committed; completed allocations retain only consumed funds.
    public Money AllocatedBudget => allocations.Where(x => x.Status is not CreatorAllocationStatus.Cancelled).Aggregate(Money.Zero(TotalBudget.Currency), (a, x) => a.Add(x.Status == CreatorAllocationStatus.Completed ? x.UsedAmount : x.OriginalAllocation));
    public Money RemainingBudget => TotalBudget.Subtract(UsedBudget); public Money UnallocatedBudget => TotalBudget.Subtract(AllocatedBudget);
    public CreatorEligibilityCriteria Eligibility { get; } public DateTime StartDateUtc { get; } public DateTime EndDateUtc { get; } public PricingSnapshot PricingSnapshot { get; } public int PromotionLiveDurationDays { get; } public PromotionStatus Status { get; private set; } = PromotionStatus.Draft; public DateTime CreatedAtUtc { get; } public DateTime? PublishedAtUtc { get; private set; } public DateTime? ActivatedAtUtc { get; private set; } public DateTime? CompletedAtUtc { get; private set; } public long Version { get; private set; }
    public IReadOnlyList<CreatorAllocation> Allocations => allocations; public IReadOnlyList<PromotionPlatform> Platforms => platforms; public IReadOnlyList<DomainEvent> DomainEvents => events;
    public Promotion(Guid businessId, string title, string description, PromotionType type, Money totalBudget, CreatorEligibilityCriteria eligibility, DateTime start, DateTime end, PricingSnapshot pricing, DateTime createdAt, int promotionLiveDurationDays) { if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required."); if (end <= start) throw new ArgumentException("End must follow start."); if (totalBudget.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(totalBudget)); if (pricing.PromotionType != type) throw new ArgumentException("Pricing snapshot type mismatch."); PromotionLiveDurationPolicy.Validate(promotionLiveDurationDays); if (pricing.MinimumPromotionBudget is { } minimum && totalBudget.Amount < minimum.Amount) throw new InvalidOperationException("Promotion budget is below the effective minimum for this Promotion type."); BusinessId = businessId; Title = title.Trim(); Description = description?.Trim() ?? ""; PromotionType = type; TotalBudget = totalBudget; ReservedBudget = Money.Zero(totalBudget.Currency); UsedBudget = Money.Zero(totalBudget.Currency); Eligibility = eligibility; StartDateUtc = start; EndDateUtc = end; PricingSnapshot = pricing; PromotionLiveDurationDays = promotionLiveDurationDays; CreatedAtUtc = createdAt; }
    public Promotion(Guid businessId, string title, string description, PromotionType type, Money totalBudget,
        CreatorEligibilityCriteria eligibility, DateTime start, DateTime end, PricingSnapshot pricing, DateTime createdAt,
        string? slogan, string? location, string? resourcesJson,
        IReadOnlyCollection<(CreatorPlatform Platform, int Capacity)> selectedPlatforms, int promotionLiveDurationDays)
        : this(businessId, title, description, type, totalBudget, eligibility, start, end, pricing, createdAt, promotionLiveDurationDays)
    {
        Slogan = Clean(slogan, 160); Location = Clean(location, 160); ResourcesJson = Clean(resourcesJson, 4000);
        if (selectedPlatforms.Count == 0 || selectedPlatforms.GroupBy(x => x.Platform).Any(x => x.Count() > 1))
            throw new ArgumentException("Select at least one supported platform once.");
        foreach (var item in selectedPlatforms)
        {
            if (item.Capacity is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(selectedPlatforms));
            platforms.Add(new PromotionPlatform(Id, item.Platform, item.Capacity));
        }
    }
    public void Fund(BusinessWallet wallet, DateTime at, Guid correlation) { Ensure(PromotionStatus.Draft); wallet.ReserveForPromotion(TotalBudget, at, correlation); ReservedBudget = TotalBudget; Status = PromotionStatus.Funded; Version++; events.Add(new PromotionFunded(Id, TotalBudget, at, correlation)); }
    public void Publish(DateTime at, Guid correlation) { Ensure(PromotionStatus.Funded); Status = PromotionStatus.Published; PublishedAtUtc = at; Version++; events.Add(new PromotionPublished(Id, at, correlation)); }
    public void Activate(DateTime at, Guid correlation) { Ensure(PromotionStatus.Published); Status = PromotionStatus.Active; ActivatedAtUtc = at; Version++; events.Add(new PromotionActivated(Id, at, correlation)); }
    public CreatorAllocation Allocate(Guid creatorId, Money amount, DateTime at, Guid correlation,
        CreatorPlatform? platform = null, Guid? creatorSocialProfileId = null)
    {
        Ensure(PromotionStatus.Active, PromotionStatus.Published, PromotionStatus.Funded);
        if (amount.Currency != TotalBudget.Currency || amount.Amount <= 0 || amount.Amount > UnallocatedBudget.Amount)
            throw new InvalidOperationException("Allocation exceeds unallocated Promotion budget.");
        if (platform is { } selected)
        {
            var slot = platforms.SingleOrDefault(x => x.Platform == selected)
                ?? throw new InvalidOperationException("The selected platform is not part of this Promotion.");
            if (creatorSocialProfileId is null) throw new InvalidOperationException("A Creator social profile is required.");
            slot.Approve();
        }
        var allocation = new CreatorAllocation(Id, creatorId, amount, at, platform, creatorSocialProfileId);
        allocations.Add(allocation); Version++; events.Add(new CreatorAllocationCreated(allocation.Id, amount, at, correlation)); return allocation;
    }
    public void IncreaseAllocation(Guid allocationId, Money amount, DateTime at, Guid correlation) { Ensure(PromotionStatus.Funded, PromotionStatus.Published, PromotionStatus.Active); var allocation = allocations.SingleOrDefault(x => x.Id == allocationId) ?? throw new KeyNotFoundException(); if (amount.Amount > UnallocatedBudget.Amount) throw new InvalidOperationException("Allocation exceeds unallocated Promotion budget."); allocation.Increase(amount, at); Version++; }
    public void CompleteParticipation(Guid allocationId, DateTime at, Guid correlation) { var allocation = allocations.SingleOrDefault(x => x.Id == allocationId) ?? throw new KeyNotFoundException(); allocation.Complete(at); Version++; }
    public void Consume(Guid allocationId, Money amount, DateTime at, Guid correlation) { Ensure(PromotionStatus.Active); var allocation = allocations.SingleOrDefault(x => x.Id == allocationId) ?? throw new KeyNotFoundException(); allocation.Consume(amount, at); UsedBudget = UsedBudget.Add(amount); ReservedBudget = ReservedBudget.Subtract(amount); Version++; if (RemainingBudget.Amount == 0 || (UnallocatedBudget.Amount == 0 && allocations.All(x => x.RemainingAmount.Amount == 0))) { Status = PromotionStatus.BudgetExhausted; events.Add(new PromotionBudgetExhausted(Id, at, correlation)); } }
    public void Complete(BusinessWallet wallet, DateTime at, Guid correlation) { Ensure(PromotionStatus.Active, PromotionStatus.BudgetExhausted); foreach (var allocation in allocations.Where(x => x.Status is CreatorAllocationStatus.Active or CreatorAllocationStatus.Exhausted)) allocation.Complete(at); Status = PromotionStatus.Completed; CompletedAtUtc = at; Version++; events.Add(new PromotionCompleted(Id, at, correlation)); }
    public void CancelExceptional(BusinessWallet wallet, DateTime at, Guid correlation) { Ensure(PromotionStatus.Draft, PromotionStatus.Funded, PromotionStatus.Published); if (allocations.Any(x => x.Status is not CreatorAllocationStatus.Cancelled)) throw new InvalidOperationException("A Promotion with approved Creators cannot be cancelled."); if (ReservedBudget.Amount > 0) wallet.ReleasePromotionReserve(ReservedBudget, at, correlation); ReservedBudget = Money.Zero(TotalBudget.Currency); Status = PromotionStatus.Cancelled; Version++; }
    public void UpdatePresentation(string description, string? slogan, string? location, string? resourcesJson)
    {
        Ensure(PromotionStatus.Draft, PromotionStatus.Funded, PromotionStatus.Published, PromotionStatus.Active);
        if (description.Length > 3000) throw new ArgumentException("Keep Promotion instructions concise.");
        Description = description.Trim(); Slogan = Clean(slogan, 160); Location = Clean(location, 160);
        ResourcesJson = Clean(resourcesJson, 4000); Version++;
    }
    private static string? Clean(string? value, int maximum)
    { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); if (result.Length > maximum) throw new ArgumentException("Promotion information is too long."); return result; }
    private void Ensure(params PromotionStatus[] allowed) { if (!allowed.Contains(Status)) throw new InvalidOperationException($"Promotion cannot transition from {Status}."); }
}

public sealed class CreatorApplication
{
    private CreatorApplication() { Message = null!; }
    public Guid Id { get; } = Guid.NewGuid(); public Guid PromotionId { get; } public Guid CreatorId { get; }
    public Guid? CreatorSocialProfileId { get; } public CreatorPlatform? Platform { get; }
    public string Message { get; } public string? ContentConcept { get; } public CreatorApplicationStatus Status { get; private set; } = CreatorApplicationStatus.Pending; public DateTime AppliedAtUtc { get; } public DateTime? ReviewedAtUtc { get; private set; } public Guid? ReviewedByBusinessUserId { get; private set; }
    public CreatorApplication(Guid promotionId, Guid creatorId, string? message, string? contentConcept, Promotion promotion, DateTime at, Guid correlation) { if (promotion.Status is not (PromotionStatus.Published or PromotionStatus.Active)) throw new InvalidOperationException("Only published or active Promotions accept applications."); PromotionId = promotionId; CreatorId = creatorId; Message = message?.Trim() ?? ""; ContentConcept = contentConcept?.Trim(); AppliedAtUtc = at; }
    public CreatorApplication(Guid promotionId, Guid creatorId, Guid creatorSocialProfileId, CreatorPlatform platform,
        string? message, string? contentConcept, Promotion promotion, DateTime at, Guid correlation)
        : this(promotionId, creatorId, message, contentConcept, promotion, at, correlation)
    { CreatorSocialProfileId = creatorSocialProfileId; Platform = platform; }
    public void Approve(Guid businessUserId, DateTime at) { if (Status != CreatorApplicationStatus.Pending) throw new InvalidOperationException(); Status = CreatorApplicationStatus.Approved; ReviewedByBusinessUserId = businessUserId; ReviewedAtUtc = at; }
    public void Reject(Guid businessUserId, DateTime at) { if (Status != CreatorApplicationStatus.Pending) throw new InvalidOperationException(); Status = CreatorApplicationStatus.Rejected; ReviewedByBusinessUserId = businessUserId; ReviewedAtUtc = at; }
    public void Withdraw(DateTime at) { if (Status != CreatorApplicationStatus.Pending) throw new InvalidOperationException(); Status = CreatorApplicationStatus.Withdrawn; ReviewedAtUtc = at; }
}

public sealed class CreatorAllocation
{
    private CreatorAllocation() { }
    public long Version { get; private set; }
    public Guid Id { get; } = Guid.NewGuid(); public Guid PromotionId { get; } public Guid CreatorId { get; }
    public CreatorPlatform? Platform { get; } public Guid? CreatorSocialProfileId { get; }
    public Money OriginalAllocation { get; private set; } public Money UsedAmount { get; private set; } public Money RemainingAmount => OriginalAllocation.Subtract(UsedAmount); public CreatorAllocationStatus Status { get; private set; } = CreatorAllocationStatus.Active; public DateTime ApprovedAtUtc { get; } public DateTime? ActivatedAtUtc { get; private set; } public DateTime? CompletedAtUtc { get; private set; }
    public CreatorAllocation(Guid promotionId, Guid creatorId, Money amount, DateTime at,
        CreatorPlatform? platform = null, Guid? creatorSocialProfileId = null) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); PromotionId = promotionId; CreatorId = creatorId; OriginalAllocation = amount; UsedAmount = Money.Zero(amount.Currency); ApprovedAtUtc = at; Platform = platform; CreatorSocialProfileId = creatorSocialProfileId; }
    public void Activate(DateTime at) { if (Status != CreatorAllocationStatus.Active) throw new InvalidOperationException(); ActivatedAtUtc = at; }
    public void Consume(Money amount, DateTime at) { if (Status != CreatorAllocationStatus.Active || amount.Amount <= 0 || amount.Currency != OriginalAllocation.Currency || amount.Amount > RemainingAmount.Amount) throw new InvalidOperationException("Creator allocation is insufficient or closed."); UsedAmount = UsedAmount.Add(amount); if (RemainingAmount.Amount == 0) Status = CreatorAllocationStatus.Exhausted; }
    public void Increase(Money amount, DateTime at) { if (Status is not (CreatorAllocationStatus.Active or CreatorAllocationStatus.Exhausted) || amount.Amount <= 0 || amount.Currency != OriginalAllocation.Currency) throw new InvalidOperationException("Only an active allocation can be increased."); OriginalAllocation = OriginalAllocation.Add(amount); Status = CreatorAllocationStatus.Active; }
    public void Complete(DateTime at) { if (Status is CreatorAllocationStatus.Completed or CreatorAllocationStatus.Cancelled) throw new InvalidOperationException("Participation is already closed."); Status = CreatorAllocationStatus.Completed; CompletedAtUtc = at; }
}

public sealed class PromotionPlatform
{
    private PromotionPlatform() { }
    public Guid Id { get; } = Guid.NewGuid();
    public Guid PromotionId { get; }
    public CreatorPlatform Platform { get; }
    public int Capacity { get; private set; }
    public int ApprovedCount { get; private set; }
    public long Version { get; private set; }
    public int Available => Math.Max(0, Capacity - ApprovedCount);
    public PromotionPlatform(Guid promotionId, CreatorPlatform platform, int capacity)
    { if (capacity is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(capacity)); PromotionId = promotionId; Platform = platform; Capacity = capacity; }
    public void Approve()
    { if (ApprovedCount >= Capacity) throw new InvalidOperationException("This Promotion platform is full."); ApprovedCount++; Version++; }
}

public sealed record PromotionViewVerification(Guid PromotionId, Guid CreatorId, Guid CreatorAllocationId, string ExternalPlatform, string ExternalContentId, long PreviousVerifiedViews, long CurrentVerifiedViews, long RewardedViewCount, DateTime VerifiedAtUtc, string EvidenceReference, string IdempotencyKey, long? ReportedViews = null, bool IsAnomaly = false, Guid? ParticipationId = null, bool IsBaseline = false) { public bool IsValid => CurrentVerifiedViews >= PreviousVerifiedViews && RewardedViewCount >= 0; }
public sealed class VerifiedSale
{
    private VerifiedSale() { QrTokenReference = null!; IdempotencyKey = null!; }
    public Guid Id { get; } = Guid.NewGuid(); public Guid PromotionId { get; } public Guid CreatorId { get; } public Guid CreatorAllocationId { get; } public Guid BusinessId { get; } public Guid CustomerId { get; } public Guid CashierId { get; } public Money PurchaseAmount { get; } public Money CreatorCommissionAmount { get; } public Money CustomerCashbackAmount { get; } public Money PlatformRevenueAmount { get; } public Money TotalPromotionCharge { get; } public string QrTokenReference { get; } public DateTime CreatedAtUtc { get; } public VerifiedSaleStatus Status { get; private set; } = VerifiedSaleStatus.Recorded; public string IdempotencyKey { get; }
    public VerifiedSale(Promotion promotion, Guid creatorId, Guid allocationId, Guid customerId, Guid cashierId, Money purchaseAmount, Money creatorCommission, Money cashback, Money platformRevenue, string qrReference, string idempotencyKey, DateTime at) { if (promotion.PromotionType != PromotionType.ViewPlusCommission) throw new InvalidOperationException("VIEW_ONLY Promotions cannot create VerifiedSale."); PromotionId = promotion.Id; BusinessId = promotion.BusinessId; CreatorId = creatorId; CreatorAllocationId = allocationId; CustomerId = customerId; CashierId = cashierId; PurchaseAmount = purchaseAmount; CreatorCommissionAmount = creatorCommission; CustomerCashbackAmount = cashback; PlatformRevenueAmount = platformRevenue; TotalPromotionCharge = creatorCommission.Add(cashback).Add(platformRevenue); QrTokenReference = qrReference; IdempotencyKey = idempotencyKey; CreatedAtUtc = at; }
}

public sealed class CreatorEarningsAccount
{
    private CreatorEarningsAccount() { }
    public Guid CreatorId { get; } public Money AvailableEarnings { get; private set; } public List<CreatorEarningEntry> Entries { get; } = [];
    public CreatorEarningsAccount(Guid creatorId, string currency = "ETB") { CreatorId = creatorId; AvailableEarnings = Money.Zero(currency); }
    public void Earn(Money amount, EarningSource source, Guid promotionId, DateTime at, Guid correlation) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); AvailableEarnings = AvailableEarnings.Add(amount); Entries.Add(new(Guid.NewGuid(), CreatorId, promotionId, source, amount, at, correlation)); }
    public void EarnUgc(Money amount, Guid assignmentId, DateTime at, Guid correlation) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); AvailableEarnings = AvailableEarnings.Add(amount); Entries.Add(new(Guid.NewGuid(), CreatorId, null, EarningSource.Ugc, amount, at, correlation, assignmentId)); }
    public Money Pay(Money amount, DateTime at, Guid correlation) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); AvailableEarnings = AvailableEarnings.Subtract(amount); return amount; }
}
public sealed record CreatorEarningEntry(Guid Id, Guid CreatorId, Guid? PromotionId, EarningSource Source, Money Amount, DateTime CreatedAtUtc, Guid CorrelationId, Guid? UgcAssignmentId = null);
public sealed class CustomerCashbackAccount
{
    public Money Pay(Money amount) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); AvailableCashback = AvailableCashback.Subtract(amount); return amount; }
    private CustomerCashbackAccount() { }
    public Guid CustomerId { get; } public Money AvailableCashback { get; private set; } public List<CustomerCashbackEntry> Entries { get; } = [];
    public CustomerCashbackAccount(Guid customerId, string currency = "ETB") { CustomerId = customerId; AvailableCashback = Money.Zero(currency); }
    public void Earn(Money amount, Guid saleId, DateTime at, Guid correlation) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); AvailableCashback = AvailableCashback.Add(amount); Entries.Add(new(Guid.NewGuid(), CustomerId, saleId, CashbackSource.VerifiedSale, amount, at, correlation)); }
}
public sealed record CustomerCashbackEntry(Guid Id, Guid CustomerId, Guid? VerifiedSaleId, CashbackSource Source, Money Amount, DateTime CreatedAtUtc, Guid CorrelationId);
public sealed record PlatformRevenueEntry(Guid Id, Guid? PromotionId, PlatformRevenueSource Source, Money Amount, RevenueStatus Status, DateTime CreatedAtUtc, Guid CorrelationId, Guid? UgcAssignmentId = null, Guid? UgcCustomerOfferSaleId = null);
public sealed class PlatformSettlement { private PlatformSettlement() { Reference = null!; } public Guid Id { get; } = Guid.NewGuid(); public Money Amount { get; } public DateTime SettledAtUtc { get; } public string Reference { get; } public PlatformSettlement(Money amount, string reference, DateTime at) { if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); Amount = amount; Reference = reference; SettledAtUtc = at; } }
public sealed record FinancialJournalLine(JournalLineType Type, Money Amount, string Account);
public sealed class FinancialJournal
{
    private FinancialJournal() { Reference = null!; }
    private readonly List<FinancialJournalLine> lines = []; public Guid Id { get; } = Guid.NewGuid(); public string Reference { get; } public Guid CorrelationId { get; } public Guid? ActorId { get; } public DateTime CreatedAtUtc { get; } public JournalSourceType SourceType { get; } public string? IdempotencyReference { get; } public bool IsPosted { get; private set; } public IReadOnlyList<FinancialJournalLine> Lines => lines;
    public FinancialJournal(string reference, Guid correlationId, Guid? actorId, JournalSourceType source, DateTime at, string? idempotencyReference = null) { Reference = reference; CorrelationId = correlationId; ActorId = actorId; SourceType = source; CreatedAtUtc = at; IdempotencyReference = idempotencyReference; }
    public void AddLine(JournalLineType type, Money amount, string account) { if (IsPosted || amount.Amount <= 0) throw new InvalidOperationException(); lines.Add(new(type, amount, account)); }
    public void Post() { if (IsPosted || lines.Count < 2) throw new InvalidOperationException(); var debit = lines.Where(x => x.Type == JournalLineType.Debit).Aggregate(Money.Zero(lines[0].Amount.Currency), (a, x) => a.Add(x.Amount)); var credit = lines.Where(x => x.Type == JournalLineType.Credit).Aggregate(Money.Zero(lines[0].Amount.Currency), (a, x) => a.Add(x.Amount)); if (debit != credit) throw new InvalidOperationException("Journal must balance."); IsPosted = true; }
}
public sealed record LegalDocumentVersion(Guid Id, LegalDocumentType Type, string Version, string ContentHash, DateTime EffectiveFromUtc);
public sealed record LegalAcceptance(Guid UserId, LegalRole Role, Guid DocumentVersionId, DateTime AcceptedAtUtc, string? IpReference, string? UserAgentReference);
