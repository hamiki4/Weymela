using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Operations;

public sealed record RequiredLegalDocument(Guid Id, string Type, string Version, string ContentHash, DateTime EffectiveFromUtc, bool Accepted);
public sealed record RequiredLegalContent(Guid Id, string Type, string Version, string ContentHash, string Content);
public sealed class LegalWorkspaceService(WeymelaDbContext db, TimeProvider clock, BusinessLegalDocumentSource? contentSource = null)
{
    private static LegalRole Role(Actor actor) => actor.Role switch { ActorRole.Business => LegalRole.Business, ActorRole.Creator => LegalRole.Creator,
        _ => throw new ApplicationFailure(FailureKind.Forbidden, "This agreement workspace is not available to this role.") };
    private static LegalDocumentType[] Types(LegalRole role) => [role == LegalRole.Business ? LegalDocumentType.BusinessAgreement : LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement];
    public async Task<IReadOnlyList<RequiredLegalDocument>> CurrentAsync(Actor actor, CancellationToken ct)
    {
        var role = Role(actor); var now = clock.GetUtcNow().UtcDateTime; List<RequiredLegalDocument> result = [];
        foreach (var type in Types(role))
        {
            var current = await db.LegalDocumentVersions.AsNoTracking().Where(x => x.Type == type && x.EffectiveFromUtc <= now)
                .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct)
                ?? throw new ApplicationFailure(FailureKind.Validation, "A required current agreement has not been published.");
            result.Add(new(current.Id, type.ToString(), current.Version, current.ContentHash, current.EffectiveFromUtc,
                await db.LegalAcceptances.AnyAsync(x => x.UserId == actor.UserId && x.Role == role && x.DocumentVersionId == current.Id, ct)));
        }
        return result;
    }
    public async Task<RequiredLegalContent?> CurrentContentAsync(Actor actor, Guid id, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business) throw new ApplicationFailure(FailureKind.Forbidden, "Only the Business may review these documents.");
        var current = (await CurrentAsync(actor, ct)).SingleOrDefault(x => x.Id == id)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Current Business document was not found.");
        var version = await db.LegalDocumentVersions.AsNoTracking().SingleAsync(x => x.Id == current.Id, ct);
        var content = contentSource is null ? null : await contentSource.ReadAsync(version, ct);
        return content is null ? null : new(current.Id, current.Type, current.Version, current.ContentHash, content);
    }
    public Task<Guid> AcceptAsync(Actor actor, Guid id, string contentHash, bool confirmed, CancellationToken ct) =>
        new EfUnitOfWork(db, System.Data.IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            InputRules.Id(id); var role = Role(actor);
            var documents = await CurrentAsync(actor, token); var current = documents.SingleOrDefault(x => x.Id == id);
            if (current is null || !confirmed || !string.Equals(current.ContentHash, contentHash, StringComparison.Ordinal))
                throw new ApplicationFailure(FailureKind.Validation, "Review and explicitly accept the exact current document version.");
            if (role == LegalRole.Business && await CurrentContentAsync(actor, id, token) is null)
                throw new ApplicationFailure(FailureKind.Validation, "Approved content for this Business document version is unavailable.");
            if (!current.Accepted)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                db.LegalAcceptances.Add(new(actor.UserId, role, id, now, null, null));
                db.AuditEvents.Add(new(Guid.NewGuid(), "LegalVersionAccepted", actor.UserId, actor.BusinessId, null, actor.CreatorId, Guid.NewGuid(), now, id.ToString()));
            }
            return id;
        }, ct);
}
