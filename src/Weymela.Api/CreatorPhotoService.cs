using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Api;

public sealed class CreatorPhotoService(WeymelaDbContext db, CreatorPhotoStore store, ILogger<CreatorPhotoService> logger)
{
    public async Task<bool> ReplaceAsync(Actor actor, IFormFile photo, CancellationToken ct)
    {
        var creatorId = RequireCreator(actor);
        var previous = await CurrentAsync(actor, creatorId, ct);
        var key = await store.SaveAsync(photo, ct);
        try
        {
            var changed = await db.PublicWorkspaceProfiles
                .Where(x => x.SubjectId == creatorId && x.Role == ActorRole.Creator && x.CreatorPhotoKey == previous)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatorPhotoKey, key), ct);
            if (changed != 1) throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Your photo changed. Refresh and try again.");
        }
        catch
        {
            store.Delete(key);
            throw;
        }
        Cleanup(previous);
        await PruneAsync(ct);
        return true;
    }

    public async Task RemoveAsync(Actor actor, CancellationToken ct)
    {
        var creatorId = RequireCreator(actor);
        var previous = await CurrentAsync(actor, creatorId, ct);
        if (previous is null) return;
        var changed = await db.PublicWorkspaceProfiles
            .Where(x => x.SubjectId == creatorId && x.Role == ActorRole.Creator && x.CreatorPhotoKey == previous)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatorPhotoKey, (string?)null), ct);
        if (changed != 1) throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Your photo changed. Refresh and try again.");
        Cleanup(previous);
        await PruneAsync(ct);
    }

    public async Task<(byte[] Bytes, string ContentType)> ReadOwnAsync(Actor actor, CancellationToken ct)
    {
        var creatorId = RequireCreator(actor);
        return await store.ReadAsync(await CurrentAsync(actor, creatorId, ct), ct);
    }

    public async Task<(byte[] Bytes, string ContentType)> ReadForBusinessAsync(Actor actor, Guid creatorId, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is not Guid businessId)
            throw new ApplicationFailure(FailureKind.NotFound, "Creator photo not found.");
        var authorized = await db.CreatorApplications.AsNoTracking().AnyAsync(x => x.CreatorId == creatorId
            && db.Promotions.Any(p => p.Id == x.PromotionId && p.BusinessId == businessId), ct)
            || await db.CreatorAllocations.AsNoTracking().AnyAsync(x => x.CreatorId == creatorId
                && db.Promotions.Any(p => p.Id == x.PromotionId && p.BusinessId == businessId), ct)
            || await db.UgcCreatorRequests.AsNoTracking().AnyAsync(x => x.CreatorId == creatorId
                && db.UgcOpportunities.Any(o => o.Id == x.UgcOpportunityId && o.BusinessId == businessId), ct)
            || await db.UgcAssignments.AsNoTracking().AnyAsync(x => x.CreatorId == creatorId
                && db.UgcOpportunities.Any(o => o.Id == x.UgcOpportunityId && o.BusinessId == businessId), ct);
        if (!authorized) throw new ApplicationFailure(FailureKind.NotFound, "Creator photo not found.");
        var key = await db.PublicWorkspaceProfiles.AsNoTracking()
            .Where(x => x.Role == ActorRole.Creator && x.SubjectId == creatorId)
            .Select(x => x.CreatorPhotoKey).SingleOrDefaultAsync(ct);
        return await store.ReadAsync(key, ct);
    }

    private async Task<string?> CurrentAsync(Actor actor, Guid creatorId, CancellationToken ct)
    {
        var active = await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == actor.UserId
            && x.Role == ActorRole.Creator && x.SubjectId == creatorId && x.IsActive, ct);
        if (!active) throw new ApplicationFailure(FailureKind.Forbidden, "An active Creator profile is required.");
        var profile = await db.PublicWorkspaceProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == creatorId && x.Role == ActorRole.Creator, ct);
        if (profile is null) throw new ApplicationFailure(FailureKind.NotFound, "Creator profile not found.");
        return profile.CreatorPhotoKey;
    }

    private static Guid RequireCreator(Actor actor) => actor.Role == ActorRole.Creator && actor.CreatorId is Guid id
        ? id : throw new ApplicationFailure(FailureKind.Forbidden, "An active Creator profile is required.");

    private void Cleanup(string? key)
    {
        try { store.Delete(key); }
        catch (IOException error) { logger.LogWarning(error, "Creator photo cleanup deferred"); }
        catch (UnauthorizedAccessException error) { logger.LogWarning(error, "Creator photo cleanup deferred"); }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        try { await store.PruneOrphansAsync(db, ct); }
        catch (IOException error) { logger.LogWarning(error, "Creator photo orphan cleanup deferred"); }
        catch (UnauthorizedAccessException error) { logger.LogWarning(error, "Creator photo orphan cleanup deferred"); }
    }
}
