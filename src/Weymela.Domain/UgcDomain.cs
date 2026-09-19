namespace Weymela.Domain;

public enum UgcContentType { Video, Photos }
public enum UgcOpportunityStatus { Draft, Open, InProgress, Completed, Cancelled }
public enum UgcRequestStatus { Pending, Approved, Rejected, Withdrawn }
public enum UgcAssignmentStatus { InProgress, Submitted, ChangesRequested, Approved, Rejected }

public sealed record UgcPricingSnapshot(Money MinimumCreatorPayment, decimal PlatformFeePercent,
    Money? MinimumUgcBudget, DateTime EffectiveFromUtc, Guid ConfigurationVersionId,
    decimal? CustomerOfferPlatformSalePercent = null,
    decimal? MaximumCustomerDiscountPercent = null)
{
    public bool IsValid => MinimumCreatorPayment.Amount > 0 && PlatformFeePercent is >= 0 and <= 100
        && (MinimumUgcBudget is null || MinimumUgcBudget.Value.Amount > 0)
        && (CustomerOfferPlatformSalePercent is null or >= 0 and <= 100)
        && (MaximumCustomerDiscountPercent is null or > 0 and <= 100);
}

public sealed class UgcOpportunity
{
    private UgcOpportunity() { Title = null!; Instructions = null!; PricingSnapshot = null!; }
    private readonly List<UgcPlatformRequirement> platformRequirements = [];
    public Guid Id { get; } = Guid.NewGuid();
    public Guid BusinessId { get; }
    public string Title { get; private set; }
    public string? Slogan { get; private set; }
    public UgcContentType ContentType { get; private set; }
    public string Instructions { get; private set; }
    public string ResourcesJson { get; private set; } = "[]";
    public string? Location { get; private set; }
    public DateTime DueDateUtc { get; private set; }
    public bool ProductProvided { get; private set; }
    public bool CreatorMustPurchase { get; private set; }
    public string? UsageRights { get; private set; }
    public Money CreatorPayment { get; private set; }
    public int CreatorCapacity { get; private set; }
    public int ApprovedCreatorCount { get; private set; }
    public Money RequiredFunding { get; private set; }
    public Money ReservedFunding { get; private set; }
    public Money UsedFunding { get; private set; }
    public UgcPricingSnapshot PricingSnapshot { get; }
    public UgcOpportunityStatus Status { get; private set; } = UgcOpportunityStatus.Draft;
    public int CurrentRevision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public long Version { get; private set; }
    public Money PerAssignmentFee => new(decimal.Round(CreatorPayment.Amount
        * PricingSnapshot.PlatformFeePercent / 100m, 2, MidpointRounding.AwayFromZero), CreatorPayment.Currency);
    // Reserve and consume the same rounded per-assignment amount so the final
    // approval cannot strand a fractional-cent residual in the reservation.
    public Money PlatformFee => new(PerAssignmentFee.Amount * CreatorCapacity, CreatorPayment.Currency);
    public Money RemainingFunding => RequiredFunding.Subtract(UsedFunding);
    public IReadOnlyList<UgcPlatformRequirement> PlatformRequirements => platformRequirements;

    public UgcOpportunity(Guid businessId, string title, string? slogan, UgcContentType contentType,
        string instructions, string resourcesJson, string? location, DateTime dueDateUtc,
        bool productProvided, bool creatorMustPurchase, string? usageRights, Money creatorPayment,
        int creatorCapacity, UgcPricingSnapshot pricing, DateTime now,
        IReadOnlyCollection<(CreatorPlatform Platform, string Format, long? MinimumAudience)> requirements)
    {
        if (businessId == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
            throw new ArgumentException("UGC title is required.");
        if (string.IsNullOrWhiteSpace(instructions) || instructions.Trim().Length > 4000)
            throw new ArgumentException("UGC instructions are required.");
        if (dueDateUtc.Kind != DateTimeKind.Utc || dueDateUtc <= now)
            throw new ArgumentException("UGC due date must be in the future.");
        if (creatorCapacity is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(creatorCapacity));
        if (!pricing.IsValid || creatorPayment.Amount < pricing.MinimumCreatorPayment.Amount)
            throw new InvalidOperationException("Creator payment is below the current minimum.");
        BusinessId = businessId; Title = title.Trim(); Slogan = Clean(slogan, 160);
        ContentType = contentType; Instructions = instructions.Trim(); ResourcesJson = resourcesJson;
        Location = Clean(location, 160); DueDateUtc = dueDateUtc; ProductProvided = productProvided;
        CreatorMustPurchase = creatorMustPurchase; UsageRights = Clean(usageRights, 2000);
        CreatorPayment = creatorPayment; CreatorCapacity = creatorCapacity; PricingSnapshot = pricing;
        RequiredFunding = new Money(decimal.Round(creatorPayment.Amount * creatorCapacity, 2)
            + PlatformFee.Amount, creatorPayment.Currency);
        if (pricing.MinimumUgcBudget is { } minimum && RequiredFunding.Amount < minimum.Amount)
            throw new InvalidOperationException("UGC funding is below the current minimum.");
        ReservedFunding = Money.Zero(creatorPayment.Currency); UsedFunding = Money.Zero(creatorPayment.Currency);
        CreatedAtUtc = now;
        if (requirements.GroupBy(x => x.Platform).Any(x => x.Count() > 1))
            throw new ArgumentException("Each UGC platform requirement may be added once.");
        foreach (var item in requirements)
        {
            if (item.MinimumAudience < 0 || string.IsNullOrWhiteSpace(item.Format) || item.Format.Trim().Length > 80)
                throw new ArgumentException("UGC platform requirements are invalid.");
            platformRequirements.Add(new(Id, item.Platform, item.Format.Trim(), item.MinimumAudience));
        }
    }

    public void Publish(BusinessWallet wallet, DateTime now, Guid correlation)
    {
        Ensure(UgcOpportunityStatus.Draft); wallet.Reserve(RequiredFunding, now, correlation);
        ReservedFunding = RequiredFunding; Status = UgcOpportunityStatus.Open; PublishedAtUtc = now; Version++;
    }
    public void ApproveCreator()
    {
        Ensure(UgcOpportunityStatus.Open, UgcOpportunityStatus.InProgress);
        if (ApprovedCreatorCount >= CreatorCapacity) throw new InvalidOperationException("UGC Creator capacity is full.");
        ApprovedCreatorCount++; Status = UgcOpportunityStatus.InProgress; Version++;
    }
    public void RecognizeApprovedDeliverable(BusinessWallet wallet, DateTime now, Guid correlation)
    {
        Ensure(UgcOpportunityStatus.InProgress);
        var amount = CreatorPayment.Add(PerAssignmentFee);
        if (amount.Amount > ReservedFunding.Amount) throw new InvalidOperationException("UGC reserved funding is insufficient.");
        wallet.ConsumeReservedFunds(amount, now, correlation); ReservedFunding = ReservedFunding.Subtract(amount);
        UsedFunding = UsedFunding.Add(amount); Version++;
        if (UsedFunding.Amount == RequiredFunding.Amount) { Status = UgcOpportunityStatus.Completed; CompletedAtUtc = now; }
    }
    public void UpdateNonMaterial(string? slogan, string instructions, string resourcesJson, string? location,
        string? usageRights)
    {
        Ensure(UgcOpportunityStatus.Draft, UgcOpportunityStatus.Open, UgcOpportunityStatus.InProgress);
        if (string.IsNullOrWhiteSpace(instructions) || instructions.Trim().Length > 4000)
            throw new ArgumentException("UGC instructions are required.");
        Slogan = Clean(slogan, 160); Instructions = instructions.Trim(); ResourcesJson = resourcesJson;
        Location = Clean(location, 160); UsageRights = Clean(usageRights, 2000); Version++;
    }
    public void UpdateDraft(string title, string? slogan, UgcContentType contentType, string instructions,
        string resourcesJson, string? location, DateTime dueDateUtc, bool productProvided,
        bool creatorMustPurchase, string? usageRights, Money creatorPayment, int creatorCapacity,
        IReadOnlyCollection<(CreatorPlatform Platform, string Format, long? MinimumAudience)> requirements,
        DateTime now)
    {
        Ensure(UgcOpportunityStatus.Draft);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120) throw new ArgumentException("UGC title is required.");
        if (string.IsNullOrWhiteSpace(instructions) || instructions.Trim().Length > 4000) throw new ArgumentException("UGC instructions are required.");
        if (dueDateUtc.Kind != DateTimeKind.Utc || dueDateUtc <= now) throw new ArgumentException("UGC due date must be in the future.");
        if (creatorCapacity is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(creatorCapacity));
        if (creatorPayment.Amount < PricingSnapshot.MinimumCreatorPayment.Amount) throw new InvalidOperationException("Creator payment is below the current minimum.");
        if (requirements.GroupBy(x => x.Platform).Any(x => x.Count() > 1)) throw new ArgumentException("Each UGC platform requirement may be added once.");
        foreach (var item in requirements)
            if (item.MinimumAudience < 0 || string.IsNullOrWhiteSpace(item.Format) || item.Format.Trim().Length > 80)
                throw new ArgumentException("UGC platform requirements are invalid.");
        Title = title.Trim(); Slogan = Clean(slogan, 160); ContentType = contentType; Instructions = instructions.Trim();
        ResourcesJson = resourcesJson; Location = Clean(location, 160); DueDateUtc = dueDateUtc;
        ProductProvided = productProvided; CreatorMustPurchase = creatorMustPurchase; UsageRights = Clean(usageRights, 2000);
        CreatorPayment = creatorPayment; CreatorCapacity = creatorCapacity;
        RequiredFunding = new Money(decimal.Round(creatorPayment.Amount * creatorCapacity, 2) + PlatformFee.Amount, creatorPayment.Currency);
        if (PricingSnapshot.MinimumUgcBudget is { } minimum && RequiredFunding.Amount < minimum.Amount)
            throw new InvalidOperationException("UGC funding is below the current minimum.");
        platformRequirements.Clear();
        foreach (var item in requirements) platformRequirements.Add(new(Id, item.Platform, item.Format.Trim(), item.MinimumAudience));
    }
    public void StartRevision() { Ensure(UgcOpportunityStatus.Draft, UgcOpportunityStatus.Open, UgcOpportunityStatus.InProgress); CurrentRevision++; Version++; }
    public void StartMaterialRevision() => StartRevision();
    public void Cancel(BusinessWallet wallet, DateTime now, Guid correlation)
    {
        Ensure(UgcOpportunityStatus.Draft, UgcOpportunityStatus.Open);
        if (ApprovedCreatorCount > 0) throw new InvalidOperationException("UGC with approved Creators cannot be cancelled.");
        if (ReservedFunding.Amount > 0) wallet.ReleaseReserve(ReservedFunding, now, correlation);
        ReservedFunding = Money.Zero(CreatorPayment.Currency); Status = UgcOpportunityStatus.Cancelled; Version++;
    }
    private void Ensure(params UgcOpportunityStatus[] allowed)
    { if (!allowed.Contains(Status)) throw new InvalidOperationException($"UGC cannot transition from {Status}."); }
    private static string? Clean(string? value, int maximum)
    { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); if (result.Length > maximum) throw new ArgumentException("UGC information is too long."); return result; }
}

public sealed class UgcPlatformRequirement
{
    private UgcPlatformRequirement() { Format = null!; }
    public Guid Id { get; } = Guid.NewGuid(); public Guid UgcOpportunityId { get; }
    public CreatorPlatform Platform { get; } public string Format { get; } public long? MinimumAudience { get; }
    public UgcPlatformRequirement(Guid ugcOpportunityId, CreatorPlatform platform, string format, long? minimumAudience)
    { UgcOpportunityId = ugcOpportunityId; Platform = platform; Format = format; MinimumAudience = minimumAudience; }
}

public sealed class UgcRevision
{
    private UgcRevision() { SnapshotJson = null!; }
    public Guid Id { get; } = Guid.NewGuid(); public Guid UgcOpportunityId { get; }
    public int RevisionNumber { get; } public bool IsMaterial { get; } public string SnapshotJson { get; }
    public Guid CreatedByUserId { get; } public DateTime CreatedAtUtc { get; }
    public UgcRevision(Guid opportunityId, int number, bool material, string snapshotJson, Guid actor, DateTime now)
    { if (number < 1 || string.IsNullOrWhiteSpace(snapshotJson)) throw new ArgumentException("UGC revision is invalid."); UgcOpportunityId = opportunityId; RevisionNumber = number; IsMaterial = material; SnapshotJson = snapshotJson; CreatedByUserId = actor; CreatedAtUtc = now; }
}

public sealed class UgcCreatorRequest
{
    private UgcCreatorRequest() { }
    public Guid Id { get; } = Guid.NewGuid(); public Guid UgcOpportunityId { get; }
    public Guid CreatorId { get; } public UgcRequestStatus Status { get; private set; } = UgcRequestStatus.Pending;
    public DateTime RequestedAtUtc { get; } public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; } public string? RejectionReason { get; private set; }
    public UgcCreatorRequest(Guid opportunityId, Guid creatorId, DateTime now)
    { UgcOpportunityId = opportunityId; CreatorId = creatorId; RequestedAtUtc = now; }
    public void Approve(Guid actor, DateTime now) { if (Status != UgcRequestStatus.Pending) throw new InvalidOperationException("UGC request is already decided."); Status = UgcRequestStatus.Approved; ReviewedByUserId = actor; ReviewedAtUtc = now; }
    public void Reject(Guid actor, string? reason, DateTime now) { if (Status != UgcRequestStatus.Pending) throw new InvalidOperationException("UGC request is already decided."); Status = UgcRequestStatus.Rejected; ReviewedByUserId = actor; ReviewedAtUtc = now; RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(); }
    public void Withdraw(DateTime now) { if (Status != UgcRequestStatus.Pending) throw new InvalidOperationException("Only pending UGC requests can be withdrawn."); Status = UgcRequestStatus.Withdrawn; ReviewedAtUtc = now; }
}

public sealed class UgcAssignment
{
    private UgcAssignment() { }
    public Guid Id { get; } = Guid.NewGuid(); public Guid UgcOpportunityId { get; }
    public Guid UgcCreatorRequestId { get; } public Guid CreatorId { get; }
    public Money CreatorPayment { get; } public Money PlatformFee { get; }
    public int AcceptedRevisionNumber { get; private set; } public bool RevisionAcceptanceRequired { get; private set; }
    public UgcAssignmentStatus Status { get; private set; } = UgcAssignmentStatus.InProgress;
    public DateTime ApprovedAtUtc { get; } public DateTime? CompletedAtUtc { get; private set; }
    public long Version { get; private set; }
    public UgcAssignment(Guid opportunityId, Guid requestId, Guid creatorId, Money creatorPayment,
        Money platformFee, int revision, DateTime now)
    { UgcOpportunityId = opportunityId; UgcCreatorRequestId = requestId; CreatorId = creatorId; CreatorPayment = creatorPayment; PlatformFee = platformFee; AcceptedRevisionNumber = revision; ApprovedAtUtc = now; }
    public void RequireRevisionAcceptance() { if (Status is UgcAssignmentStatus.Approved or UgcAssignmentStatus.Rejected) return; RevisionAcceptanceRequired = true; Version++; }
    public void AcceptRevision(int revision) { if (!RevisionAcceptanceRequired || revision <= AcceptedRevisionNumber) throw new InvalidOperationException("No newer UGC revision requires acceptance."); AcceptedRevisionNumber = revision; RevisionAcceptanceRequired = false; Version++; }
    public void Submitted() { if (RevisionAcceptanceRequired || Status is not (UgcAssignmentStatus.InProgress or UgcAssignmentStatus.ChangesRequested)) throw new InvalidOperationException("UGC assignment cannot be submitted."); Status = UgcAssignmentStatus.Submitted; Version++; }
    public void RequestChanges() { if (Status != UgcAssignmentStatus.Submitted) throw new InvalidOperationException("Only submitted UGC can require changes."); Status = UgcAssignmentStatus.ChangesRequested; Version++; }
    public void Approve(DateTime now) { if (Status != UgcAssignmentStatus.Submitted) throw new InvalidOperationException("Only submitted UGC can be approved."); Status = UgcAssignmentStatus.Approved; CompletedAtUtc = now; Version++; }
    public void Reject(DateTime now) { if (Status != UgcAssignmentStatus.Submitted) throw new InvalidOperationException("Only submitted UGC can be rejected."); Status = UgcAssignmentStatus.Rejected; CompletedAtUtc = now; Version++; }
}

public sealed class UgcSubmission
{
    private UgcSubmission() { SubmissionUrl = null!; }
    public Guid Id { get; } = Guid.NewGuid(); public Guid UgcAssignmentId { get; }
    public int RevisionNumber { get; } public string SubmissionUrl { get; }
    public DateTime SubmittedAtUtc { get; } public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; } public string? Feedback { get; private set; }
    public UgcAssignmentStatus Status { get; private set; } = UgcAssignmentStatus.Submitted;
    public UgcSubmission(Guid assignmentId, int revision, string url, DateTime now)
    { UgcAssignmentId = assignmentId; RevisionNumber = revision; SubmissionUrl = url; SubmittedAtUtc = now; }
    public void RequestChanges(Guid actor, string feedback, DateTime now) { if (Status != UgcAssignmentStatus.Submitted || string.IsNullOrWhiteSpace(feedback)) throw new InvalidOperationException("Feedback is required."); Status = UgcAssignmentStatus.ChangesRequested; ReviewedByUserId = actor; ReviewedAtUtc = now; Feedback = feedback.Trim(); }
    public void Approve(Guid actor, DateTime now) { if (Status != UgcAssignmentStatus.Submitted) throw new InvalidOperationException("Only submitted UGC can be approved."); Status = UgcAssignmentStatus.Approved; ReviewedByUserId = actor; ReviewedAtUtc = now; }
    public void Reject(Guid actor, string? feedback, DateTime now) { if (Status != UgcAssignmentStatus.Submitted) throw new InvalidOperationException("Only submitted UGC can be rejected."); Status = UgcAssignmentStatus.Rejected; ReviewedByUserId = actor; ReviewedAtUtc = now; Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim(); }
}
