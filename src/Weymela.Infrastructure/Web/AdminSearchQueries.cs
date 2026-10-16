using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

// Callers demand their capability before evaluating these read-only predicates.
// No private identifiers are placed in public profile records.
public sealed class AdminSearchQueries(WeymelaDbContext db, AdminListFilter filter)
{
    public IQueryable<CommercePermission> Permissions(ActorRole role)
    {
        filter.Validate();
        var term = filter.Term;
        var rows = db.CommercePermissions.AsNoTracking().Where(x => x.Role == role);
        if (filter.Status == "Active") rows = rows.Where(x => x.IsActive);
        else if (filter.Status == "Inactive") rows = rows.Where(x => !x.IsActive);
        if (term != "") rows = rows.Where(x =>
            db.PublicWorkspaceProfiles.Any(p => p.SubjectId == x.SubjectId && p.Role == role && p.DisplayName.ToLower().Contains(term))
            || db.AuthIdentifiers.Any(i => i.UserId == x.UserId && i.DeliveryAddress != null && i.DeliveryAddress.ToLower().Contains(term))
            || db.RoleEnrollments.Any(r => r.UserId == x.UserId && r.RequestedRole == role && r.SubmissionJson.ToLower().Contains(term))
            || role == ActorRole.Creator && db.CreatorSocialProfiles.Any(p => p.CreatorId == x.SubjectId && p.ProfileUrl.ToLower().Contains(term)));
        return rows;
    }

    public IQueryable<Promotion> Campaigns()
    {
        filter.Validate();
        var term = filter.Term;
        var rows = db.Promotions.AsNoTracking();
        if (term != "") rows = rows.Where(x => x.Title.ToLower().Contains(term) || x.PublicPromotionId.ToLower().Contains(term)
            || db.PublicWorkspaceProfiles.Any(p => p.SubjectId == x.BusinessId && p.Role == ActorRole.Business && p.DisplayName.ToLower().Contains(term))
            || x.Allocations.Any(a => db.PublicWorkspaceProfiles.Any(p => p.SubjectId == a.CreatorId && p.Role == ActorRole.Creator && p.DisplayName.ToLower().Contains(term))));
        if (!string.IsNullOrEmpty(filter.Status))
        {
            if (!Enum.TryParse<PromotionStatus>(filter.Status, true, out var status))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a valid campaign status.");
            rows = rows.Where(x => x.Status == status);
        }
        if (!string.IsNullOrEmpty(filter.Type))
        {
            var type = filter.Type switch { "View Only" or "VIEW_ONLY" => PromotionType.ViewOnly,
                "View + Sale" or "VIEW_PLUS_SALE" => PromotionType.ViewPlusCommission,
                _ => Enum.TryParse<PromotionType>(filter.Type, true, out var parsed) ? parsed : (PromotionType?)null };
            if (type is null) throw new ApplicationFailure(FailureKind.Validation, "Choose a valid campaign type.");
            rows = rows.Where(x => x.PromotionType == type);
        }
        if (filter.From is { } from) rows = rows.Where(x => x.CreatedAtUtc >= from);
        if (filter.To is { } to) rows = rows.Where(x => x.CreatedAtUtc < to.AddDays(1));
        return rows;
    }

    public IQueryable<UgcOpportunity> Ugc()
    {
        filter.Validate();
        var term = filter.Term;
        var rows = db.UgcOpportunities.AsNoTracking();
        if (term != "") rows = rows.Where(x => x.Title.ToLower().Contains(term)
            || db.PublicWorkspaceProfiles.Any(p => p.SubjectId == x.BusinessId && p.Role == ActorRole.Business && p.DisplayName.ToLower().Contains(term))
            || db.UgcAssignments.Any(a => a.UgcOpportunityId == x.Id && db.PublicWorkspaceProfiles.Any(p => p.SubjectId == a.CreatorId && p.Role == ActorRole.Creator && p.DisplayName.ToLower().Contains(term))));
        if (!string.IsNullOrEmpty(filter.Status))
        {
            if (!Enum.TryParse<UgcOpportunityStatus>(filter.Status, true, out var status))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a valid UGC status.");
            rows = rows.Where(x => x.Status == status);
        }
        if (filter.From is { } from) rows = rows.Where(x => x.CreatedAtUtc >= from);
        if (filter.To is { } to) rows = rows.Where(x => x.CreatedAtUtc < to.AddDays(1));
        return rows;
    }
}
