using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;

namespace Weymela.Infrastructure.Identity;

public sealed record RoleEnrollmentRequest(ActorRole Role, string DisplayName, string? PublicId, string? Region,
    string? Category, string? Submission, Guid? ProposedBusinessId = null,
    AccountLegalConfirmation? AccountLegal = null, string? IpReference = null, string? UserAgentReference = null,
    IReadOnlyList<CreatorApplicationSocialProfile>? SocialProfiles = null);
public sealed record CreatorApplicationSocialProfile(string Platform, string ProfileUrl);
public sealed record RoleEnrollmentSummary(Guid Id, ActorRole Role, RoleEnrollmentStatus Status, string DisplayName,
    string PublicId, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc, string? DecisionReason, long Version,
    string? Region = null, string? Category = null, string? Submission = null,
    IReadOnlyList<CreatorApplicationSocialProfile>? SocialProfiles = null);

public sealed class RoleEnrollmentService(WeymelaDbContext db, TimeProvider clock)
{
    private static readonly ActorRole[] PublicRoles = [ActorRole.Customer, ActorRole.Creator, ActorRole.Business];

    public async Task<RoleEnrollmentSummary> SubmitAsync(Actor actor, RoleEnrollmentRequest request, string idempotencyKey, CancellationToken ct)
    {
        if (actor.UserId == Guid.Empty || !PublicRoles.Contains(request.Role)) throw Denied();
        Validate(request, idempotencyKey);
        var socialProfiles = NormalizeSocialProfiles(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.RoleEnrollments.SingleOrDefaultAsync(x => x.UserId == actor.UserId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            var prior = JsonSerializer.Deserialize<EnrollmentDetails>(existing.SubmissionJson);
            if (existing.RequestedRole != request.Role || prior is null || prior.DisplayName != request.DisplayName.Trim()
                || (request.Role != ActorRole.Customer && !string.IsNullOrWhiteSpace(request.PublicId) && prior.PublicId != request.PublicId.Trim())
                || prior.Region != request.Region?.Trim() || prior.Category != request.Category?.Trim()
                || prior.Submission != request.Submission?.Trim()
                || !(prior.SocialProfiles ?? []).SequenceEqual(socialProfiles))
                throw new ApplicationFailure(FailureKind.IdempotencyConflict, "This request reference was already used for another profile request.");
            return Summary(existing);
        }
        if (request.ProposedBusinessId is not null) throw new ApplicationFailure(FailureKind.Validation, "A new Business is created by Weymela after approval.");
        if (await db.RoleEnrollments.AnyAsync(x => x.UserId == actor.UserId && x.RequestedRole == request.Role && x.Status == RoleEnrollmentStatus.Pending, ct))
            throw new ApplicationFailure(FailureKind.Validation, "This profile request is already under review.");
        if (await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == request.Role, ct))
            throw new ApplicationFailure(FailureKind.Validation, "This profile is already active or has a prior decision requiring review.");
        var now = clock.GetUtcNow().UtcDateTime;
        var publicId = request.Role == ActorRole.Customer
            ? await NewCustomerPublicIdAsync(ct)
            : string.IsNullOrWhiteSpace(request.PublicId)
                ? $"{(request.Role == ActorRole.Creator ? "CR" : "BU")}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}"
                : request.PublicId.Trim();
        if (request.Role == ActorRole.Customer)
            await new AccountLegalOnboardingService(db, clock).AcceptCurrentAsync(actor.UserId,
                request.AccountLegal, request.IpReference, request.UserAgentReference, ct);
        var submission = JsonSerializer.Serialize(new
            {
                DisplayName = request.DisplayName.Trim(),
                PublicId = publicId,
                Region = request.Role == ActorRole.Customer ? null : request.Region?.Trim(),
                Category = request.Role == ActorRole.Customer ? null : request.Category?.Trim(),
                Submission = request.Role == ActorRole.Customer ? null : request.Submission?.Trim(),
                SocialProfiles = socialProfiles
            });
        if (submission.Length > 6000) throw new ApplicationFailure(FailureKind.Validation, "Keep application details concise.");
        var row = new RoleEnrollmentRecord
        {
            UserId = actor.UserId,
            RequestedRole = request.Role,
            SubmissionJson = submission,
            SubmittedAtUtc = now,
            IdempotencyKey = idempotencyKey
        };
        db.RoleEnrollments.Add(row);
        if (request.Role is ActorRole.Creator or ActorRole.Business)
            db.OutboxMessages.Add(new OutboxMessage { EventType = "RoleEnrollmentSubmitted",
                Payload = JsonSerializer.Serialize(new { row.Id, row.UserId, row.RequestedRole }), OccurredAtUtc = now });
        if (request.Role == ActorRole.Customer)
        {
            var subject = Guid.NewGuid();
            row.Status = RoleEnrollmentStatus.Approved;
            row.ReviewedAtUtc = now;
            row.DecisionReason = "Customer profile activated by the account holder.";
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
            {
                SubjectId = subject, Role = ActorRole.Customer, DisplayName = request.DisplayName.Trim(), PublicId = publicId
            });
            db.CustomerProfiles.Add(new CustomerProfileRecord
            {
                CustomerId = subject,
                UserId = actor.UserId,
                PreferredName = request.DisplayName.Trim(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.CommercePermissions.Add(new CommercePermission(actor.UserId, ActorRole.Customer, subject, null, true, false));
            db.CustomerCashbackAccounts.Add(new CustomerCashbackAccount(subject));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "CustomerProfileActivated", actor.UserId, null, null, null,
                Guid.NewGuid(), now, $"enrollment={row.Id:D};customerId={subject:D}"));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Summary(row);
    }

    public async Task<IReadOnlyList<RoleEnrollmentSummary>> MineAsync(Guid userId, CancellationToken ct)
        => (await db.RoleEnrollments.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct)).Select(Summary).ToList();

    public async Task<IReadOnlyList<RoleEnrollmentSummary>> PendingAsync(Actor admin, CancellationToken ct)
    {
        if (!AdministrativeAuthority.For(new RealActor(admin)).Allows(AdministrativeCapability.AccountReview)) throw Denied();
        return (await db.RoleEnrollments.AsNoTracking().Where(x => x.Status == RoleEnrollmentStatus.Pending)
            .OrderBy(x => x.SubmittedAtUtc).ToListAsync(ct)).Select(Summary).ToList();
    }

    public Task<IReadOnlyList<RoleEnrollmentSummary>> PendingAsync(CancellationToken ct)
        => PendingAsync(new Actor(Guid.Empty, ActorRole.PlatformAdmin), ct);

    public async Task<RoleEnrollmentSummary> ReviewAsync(Actor admin, Guid id, bool approve, string? reason, long expectedVersion, string idempotencyKey, CancellationToken ct)
    {
        if (!AdministrativeAuthority.For(new RealActor(admin)).Allows(AdministrativeCapability.AccountReview)) throw Denied();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "A request reference is required.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var row = await db.RoleEnrollments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApplicationFailure(FailureKind.NotFound, "Profile request not found.");
        if (row.Version != expectedVersion) throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "This profile request changed. Refresh before reviewing.");
        if (row.Status != RoleEnrollmentStatus.Pending) return Summary(row);
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500))
            throw new ApplicationFailure(FailureKind.Validation, "Give a concise reason for rejection.");
        var now = clock.GetUtcNow().UtcDateTime;
        row.Status = approve ? RoleEnrollmentStatus.Approved : RoleEnrollmentStatus.Rejected; row.ReviewedAtUtc = now; row.ReviewedBy = admin.UserId; row.DecisionReason = reason?.Trim();
        if (approve)
        {
            if (await db.AccountLifecycles.AsNoTracking().AnyAsync(x => x.UserId == row.UserId
                && (x.Status == AccountLifecycleStatus.Closed || x.Status == AccountLifecycleStatus.Revoked
                    || x.Status == AccountLifecycleStatus.Cancelled), ct))
                throw new ApplicationFailure(FailureKind.Forbidden, "This account cannot receive a profile.");
            if (await db.CommercePermissions.AnyAsync(x => x.UserId == row.UserId && x.Role == row.RequestedRole, ct))
                throw new ApplicationFailure(FailureKind.Validation, "This profile already has an active or suspended membership.");
            var details = JsonSerializer.Deserialize<EnrollmentDetails>(row.SubmissionJson) ?? throw new ApplicationFailure(FailureKind.Validation, "The profile request is incomplete.");
            var subject = Guid.NewGuid(); Guid? business = null;
            if (row.RequestedRole == ActorRole.Business) { business = Guid.NewGuid(); subject = business.Value; db.BusinessWallets.Add(new BusinessWallet(business.Value)); }
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = row.RequestedRole, DisplayName = details.DisplayName, PublicId = details.PublicId, Region = details.Region ?? "", Category = details.Category ?? "" });
            db.CommercePermissions.Add(new CommercePermission(row.UserId, row.RequestedRole, subject, business, true,
                row.RequestedRole == ActorRole.Business));
            if (row.RequestedRole == ActorRole.Creator && details.SocialProfiles is { Count: > 0 })
                foreach (var social in details.SocialProfiles)
                {
                    if (!Enum.TryParse<CreatorPlatform>(social.Platform, true, out var platform) || !Enum.IsDefined(platform))
                        throw new ApplicationFailure(FailureKind.Validation, "The Creator application contains an invalid social profile.");
                    db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord { CreatorId = subject, Platform = platform,
                        ProfileUrl = CreatorSocialProfileLinks.Normalize(platform, social.ProfileUrl),
                        VerificationStatus = "SelfReported", CreatedAtUtc = now, UpdatedAtUtc = now });
                }
        }
        db.OutboxMessages.Add(new OutboxMessage { EventType = approve ? "RoleEnrollmentApproved" : "RoleEnrollmentRejected", Payload = JsonSerializer.Serialize(new { row.Id, row.UserId, row.RequestedRole }), OccurredAtUtc = now });
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), approve ? "RoleEnrollmentApproved" : "RoleEnrollmentRejected", admin.UserId,
            null, null, null, Guid.NewGuid(), now, $"enrollment={row.Id:D};targetUserId={row.UserId:D}"));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Summary(row);
    }

    private static RoleEnrollmentSummary Summary(RoleEnrollmentRecord x)
    {
        var details = JsonSerializer.Deserialize<EnrollmentDetails>(x.SubmissionJson);
        return new(x.Id, x.RequestedRole, x.Status, details?.DisplayName ?? "", details?.PublicId ?? "", x.SubmittedAtUtc, x.ReviewedAtUtc, x.DecisionReason, x.Version,
            details?.Region, details?.Category, details?.Submission, details?.SocialProfiles);
    }
    private static void Validate(RoleEnrollmentRequest x, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200 || string.IsNullOrWhiteSpace(x.DisplayName)
            || x.DisplayName.Trim().Length > 120
            || (x.PublicId?.Trim().Length ?? 0) > 80)
            throw new ApplicationFailure(FailureKind.Validation, "Complete the required profile details.");
        if ((x.Region?.Length ?? 0) > 80 || (x.Category?.Length ?? 0) > 80 || (x.Submission?.Length ?? 0) > 3000) throw new ApplicationFailure(FailureKind.Validation, "Keep profile details concise.");
        if (x.Role == ActorRole.Creator && string.IsNullOrWhiteSpace(x.PublicId) && (x.SocialProfiles?.Count ?? 0) == 0)
            throw new ApplicationFailure(FailureKind.Validation, "Add at least one social profile.");
    }

    private static IReadOnlyList<CreatorApplicationSocialProfile> NormalizeSocialProfiles(RoleEnrollmentRequest request)
    {
        if (request.Role != ActorRole.Creator)
        {
            if (request.SocialProfiles is { Count: > 0 }) throw new ApplicationFailure(FailureKind.Validation, "Social profiles belong to a Creator application.");
            return [];
        }
        if (request.SocialProfiles is null) return [];
        if (request.SocialProfiles.Count > 4) throw new ApplicationFailure(FailureKind.Validation, "Add up to four social profiles.");
        var result = new List<CreatorApplicationSocialProfile>();
        foreach (var social in request.SocialProfiles)
        {
            if (!Enum.TryParse<CreatorPlatform>(social.Platform, true, out var platform) || !Enum.IsDefined(platform)
                || result.Any(x => x.Platform == platform.ToString()))
                throw new ApplicationFailure(FailureKind.Validation, "Choose each supported social platform once.");
            result.Add(new(platform.ToString(), CreatorSocialProfileLinks.Normalize(platform, social.ProfileUrl)));
        }
        return result;
    }

    private async Task<string> NewCustomerPublicIdAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"CU-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
            if (!await db.PublicWorkspaceProfiles.AsNoTracking()
                    .AnyAsync(x => x.Role == ActorRole.Customer && x.PublicId == candidate, ct))
                return candidate;
        }
        throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "We couldn't create the Customer profile. Try again.");
    }
    private static ApplicationFailure Denied() => new(FailureKind.Forbidden, "This profile action is not available.");
    private sealed record EnrollmentDetails(string DisplayName, string PublicId, string? Region, string? Category, string? Submission,
        IReadOnlyList<CreatorApplicationSocialProfile>? SocialProfiles = null);
}
