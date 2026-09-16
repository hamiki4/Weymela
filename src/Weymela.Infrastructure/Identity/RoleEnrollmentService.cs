using System.Text.Json;
using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed record RoleEnrollmentRequest(ActorRole Role, string DisplayName, string? PublicId, string? Region,
    string? Category, string? Submission, Guid? ProposedBusinessId = null,
    AccountLegalConfirmation? AccountLegal = null, string? IpReference = null, string? UserAgentReference = null);
public sealed record RoleEnrollmentSummary(Guid Id, ActorRole Role, RoleEnrollmentStatus Status, string DisplayName,
    string PublicId, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc, string? DecisionReason, long Version);

public sealed class RoleEnrollmentService(WeymelaDbContext db, TimeProvider clock)
{
    private static readonly ActorRole[] PublicRoles = [ActorRole.Customer, ActorRole.Creator, ActorRole.Business];

    public async Task<RoleEnrollmentSummary> SubmitAsync(Actor actor, RoleEnrollmentRequest request, string idempotencyKey, CancellationToken ct)
    {
        if (actor.UserId == Guid.Empty || !PublicRoles.Contains(request.Role)) throw Denied();
        Validate(request, idempotencyKey);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.RoleEnrollments.SingleOrDefaultAsync(x => x.UserId == actor.UserId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            var prior = JsonSerializer.Deserialize<EnrollmentDetails>(existing.SubmissionJson);
            if (existing.RequestedRole != request.Role || prior is null || prior.DisplayName != request.DisplayName.Trim()
                || (request.Role != ActorRole.Customer && prior.PublicId != request.PublicId?.Trim()))
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
            : request.PublicId!.Trim();
        if (request.Role == ActorRole.Customer)
            await new AccountLegalOnboardingService(db, clock).AcceptCurrentAsync(actor.UserId,
                request.AccountLegal, request.IpReference, request.UserAgentReference, ct);
        var row = new RoleEnrollmentRecord
        {
            UserId = actor.UserId,
            RequestedRole = request.Role,
            SubmissionJson = JsonSerializer.Serialize(new
            {
                DisplayName = request.DisplayName.Trim(),
                PublicId = publicId,
                Region = request.Role == ActorRole.Customer ? null : request.Region?.Trim(),
                Category = request.Role == ActorRole.Customer ? null : request.Category?.Trim(),
                Submission = request.Role == ActorRole.Customer ? null : request.Submission?.Trim()
            }),
            SubmittedAtUtc = now,
            IdempotencyKey = idempotencyKey
        };
        db.RoleEnrollments.Add(row);
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

    public async Task<IReadOnlyList<RoleEnrollmentSummary>> PendingAsync(CancellationToken ct)
        => (await db.RoleEnrollments.AsNoTracking().Where(x => x.Status == RoleEnrollmentStatus.Pending).OrderBy(x => x.SubmittedAtUtc).ToListAsync(ct)).Select(Summary).ToList();

    public async Task<RoleEnrollmentSummary> ReviewAsync(Actor admin, Guid id, bool approve, string? reason, long expectedVersion, string idempotencyKey, CancellationToken ct)
    {
        if (admin.Role != ActorRole.PlatformAdmin) throw Denied();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "A request reference is required.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var row = await db.RoleEnrollments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApplicationFailure(FailureKind.NotFound, "Profile request not found.");
        if (row.Version != expectedVersion) throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "This profile request changed. Refresh before reviewing.");
        if (row.Status != RoleEnrollmentStatus.Pending) return Summary(row);
        var now = clock.GetUtcNow().UtcDateTime;
        row.Status = approve ? RoleEnrollmentStatus.Approved : RoleEnrollmentStatus.Rejected; row.ReviewedAtUtc = now; row.ReviewedBy = admin.UserId; row.DecisionReason = reason?.Trim();
        if (approve)
        {
            if (await db.CommercePermissions.AnyAsync(x => x.UserId == row.UserId && x.Role == row.RequestedRole, ct))
                throw new ApplicationFailure(FailureKind.Validation, "This profile already has an active or suspended membership.");
            var details = JsonSerializer.Deserialize<EnrollmentDetails>(row.SubmissionJson) ?? throw new ApplicationFailure(FailureKind.Validation, "The profile request is incomplete.");
            var subject = Guid.NewGuid(); Guid? business = null;
            if (row.RequestedRole == ActorRole.Business) { business = Guid.NewGuid(); subject = business.Value; db.BusinessWallets.Add(new BusinessWallet(business.Value)); }
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = row.RequestedRole, DisplayName = details.DisplayName, PublicId = details.PublicId, Region = details.Region ?? "", Category = details.Category ?? "" });
            db.CommercePermissions.Add(new CommercePermission(row.UserId, row.RequestedRole, subject, business, true, false));
        }
        db.OutboxMessages.Add(new OutboxMessage { EventType = approve ? "RoleEnrollmentApproved" : "RoleEnrollmentRejected", Payload = JsonSerializer.Serialize(new { row.Id, row.UserId, row.RequestedRole }), OccurredAtUtc = now });
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), approve ? "RoleEnrollmentApproved" : "RoleEnrollmentRejected", admin.UserId,
            null, null, null, Guid.NewGuid(), now, $"enrollment={row.Id:D};targetUserId={row.UserId:D}"));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Summary(row);
    }

    private static RoleEnrollmentSummary Summary(RoleEnrollmentRecord x)
    {
        var details = JsonSerializer.Deserialize<EnrollmentDetails>(x.SubmissionJson);
        return new(x.Id, x.RequestedRole, x.Status, details?.DisplayName ?? "", details?.PublicId ?? "", x.SubmittedAtUtc, x.ReviewedAtUtc, x.DecisionReason, x.Version);
    }
    private static void Validate(RoleEnrollmentRequest x, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200 || string.IsNullOrWhiteSpace(x.DisplayName)
            || x.DisplayName.Trim().Length > 120
            || (x.Role != ActorRole.Customer && (string.IsNullOrWhiteSpace(x.PublicId) || x.PublicId.Trim().Length > 80)))
            throw new ApplicationFailure(FailureKind.Validation, "Complete the required profile details.");
        if ((x.Region?.Length ?? 0) > 80 || (x.Category?.Length ?? 0) > 80 || (x.Submission?.Length ?? 0) > 3000) throw new ApplicationFailure(FailureKind.Validation, "Keep profile details concise.");
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
    private sealed record EnrollmentDetails(string DisplayName, string PublicId, string? Region, string? Category, string? Submission);
}
