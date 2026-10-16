using System.Text.Json;
using Weymela.Domain;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

public sealed class AdminSocialProfileReviewService(WeymelaDbContext db, TimeProvider clock)
{
    private const long MaximumAudience = 9_000_000_000_000_000;

    public async Task<IReadOnlyList<AdminSocialProfileReviewView>> ListAsync(Actor actor, CancellationToken ct, AdminListFilter? filter = null)
    {
        Demand(actor);
        var query = filter ?? new(); query.Validate();
        var term = query.Term;
        var source = db.CreatorSocialProfiles.AsNoTracking().Where(x => x.IsActive);
        if (term != "") source = source.Where(x => x.ProfileUrl.ToLower().Contains(term)
            || db.PublicWorkspaceProfiles.Any(p => p.SubjectId == x.CreatorId && p.Role == ActorRole.Creator && p.DisplayName.ToLower().Contains(term)));
        if (!string.IsNullOrEmpty(query.Type))
        {
            if (!Enum.TryParse<CreatorPlatform>(query.Type, true, out var platform))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a valid social platform.");
            source = source.Where(x => x.Platform == platform);
        }
        if (!string.IsNullOrEmpty(query.Status)) source = source.Where(x => x.VerificationStatus == query.Status);
        var profiles = await source.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
            .Skip(query.Offset).Take(query.PageSize).ToListAsync(ct);
        var ids = profiles.Select(x => x.CreatorId).Distinct().ToArray();
        var creators = await db.PublicWorkspaceProfiles.AsNoTracking()
            .Where(x => x.Role == ActorRole.Creator && ids.Contains(x.SubjectId)).ToDictionaryAsync(x => x.SubjectId, ct);
        return profiles.Where(x => creators.ContainsKey(x.CreatorId)).Select(x =>
        {
            var creator = creators[x.CreatorId];
            return new AdminSocialProfileReviewView(x.Id, x.CreatorId, creator.DisplayName, creator.CreatorNumber,
                creator.CreatorPhotoKey, x.Platform.ToString(), x.ProfileUrl, x.SelfReportedAudience,
                x.VerifiedAudience, x.VerificationStatus, x.AudienceVerificationSource);
        }).ToArray();
    }

    public async Task<Guid> ReviewAsync(Actor actor, Guid id, AdminSocialProfileReviewInput input, string key, CancellationToken ct)
    {
        Demand(actor);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "A review reference is required.");
        var action = input.Action.Trim().ToLowerInvariant();
        if (action is not ("approve" or "reject")) throw new ApplicationFailure(FailureKind.Validation, "Choose Approve or Reject.");
        if (action == "approve" && (input.VerifiedAudience is null or < 0 or > MaximumAudience))
            throw new ApplicationFailure(FailureKind.Validation, "Enter a valid Admin Verified audience count.");
        var row = await db.CreatorSocialProfiles.SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Social profile not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (action == "approve")
        {
            row.VerifiedAudience = input.VerifiedAudience;
            row.AudienceVerificationSource = "AdminVerified";
            row.VerificationStatus = "Verified";
        }
        else
        {
            row.VerifiedAudience = null;
            row.AudienceVerificationSource = "SelfReported";
            row.VerificationStatus = "Rejected";
        }
        row.UpdatedAtUtc = now;
        row.Version++;
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), action == "approve" ? "CreatorSocialProfileAdminVerified" : "CreatorSocialProfileVerificationRejected",
            actor.UserId, null, null, row.CreatorId, Guid.NewGuid(), now,
            JsonSerializer.Serialize(new { row.Id, row.Platform, row.SelfReportedAudience, row.VerifiedAudience, Source = row.AudienceVerificationSource })));
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private static void Demand(Actor actor)
    {
        if (!AdministrativeAuthority.For(new RealActor(actor)).Allows(AdministrativeCapability.CreatorSocialProfileReview))
            throw new ApplicationFailure(FailureKind.Forbidden, "Social profile review is not available to this role.");
    }
}
