using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed record ViewAsStartResult(ViewAsSessionView Session, string CookieValue);

/// <summary>
/// Server-side support context for Platform Admin inspection. The authenticated
/// actor is never replaced and the returned subject is never accepted as a
/// command actor.
/// </summary>
public sealed class ViewAsService(WeymelaDbContext db, TimeProvider clock)
{
    public const int LifetimeMinutes = 15;
    public const string StartOperation = "ViewAsStart";
    private static readonly ActorRole[] ForbiddenTargetRoles = [ActorRole.PlatformAdmin, ActorRole.Cashier];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<ViewAsStartResult> StartAsync(AuthorityContext authority, Guid viewedUserId,
        string? reason, string idempotencyKey, Guid correlationId, CancellationToken ct = default)
        => new Persistence.Transactions.EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var real = await DemandRealPlatformAsync(authority, token);
            var cleanReason = CleanReason(reason);
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
                throw new ApplicationFailure(FailureKind.Validation, "A request reference is required.");
            var fingerprint = RequestFingerprint.Create(viewedUserId.ToString("D"), cleanReason ?? "");
            var prior = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == real.UserId
                && x.OperationType == StartOperation && x.Key == idempotencyKey, token);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint)
                    throw new ApplicationFailure(FailureKind.IdempotencyConflict, "This request reference was already used for another View As target.");
                var priorId = Guid.Parse(prior.ResultReference);
                var priorRow = await db.SupportSessions.SingleAsync(x => x.Id == priorId, token);
                return Result(priorRow);
            }

            if (authority.IsViewAsActive)
                throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                    "End the current View As session before starting another one.", code: "ViewAsAlreadyActive");

            var existing = await db.SupportSessions.SingleOrDefaultAsync(x => x.RealActorUserId == real.UserId && x.EndedAtUtc == null, token);
            if (existing is not null)
            {
                if (existing.ExpiresAtUtc <= Now)
                {
                    await ExpireAsync(existing, correlationId, token);
                }
                else
                {
                    throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                        "End the current View As session before starting another one.", code: "ViewAsAlreadyActive");
                }
            }

            var target = await ResolveTargetAsync(viewedUserId, token);
            var now = Now;
            var row = new SupportSessionRecord
            {
                RealActorUserId = real.UserId,
                ViewedUserId = target.UserId,
                ViewedRole = target.Role,
                ViewedBusinessId = target.BusinessId,
                ViewedCreatorId = target.CreatorId,
                ViewedCustomerId = target.CustomerId,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(LifetimeMinutes),
                Version = 1
            };
            row.SessionIdentifierHash = Hash(row.Id);
            db.SupportSessions.Add(row);
            db.AuditEvents.Add(Audit(row, real.UserId, "ViewAsStarted", "view-as-start", cleanReason, correlationId, now));
            db.IdempotencyRecords.Add(new(real.UserId, StartOperation, idempotencyKey, fingerprint, row.Id.ToString("D"), now));
            return Result(row);
        }, ct);

    public async Task<ViewAsSessionView?> CurrentAsync(RealActor real, string? cookieValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cookieValue)) return null;
        var row = await LoadByCookieAsync(real.UserId, cookieValue, ct);
        if (row.EndedAtUtc is not null || row.ExpiresAtUtc <= Now)
        {
            if (row.EndedAtUtc is null) await ExpireAsync(row, Guid.NewGuid(), ct);
            throw InvalidSession();
        }
        await EnsureRealActorStillActiveAsync(real, ct);
        await EnsureTargetStillActiveAsync(row, ct);
        return View(row);
    }

    public async Task<AuthorityContext> ResolveActiveAsync(RealActor real, string? cookieValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
            return AuthorityContext.ForAuthenticatedActor(real.Identity);
        var row = await LoadByCookieAsync(real.UserId, cookieValue, ct);
        if (row.EndedAtUtc is not null) throw InvalidSession();
        if (row.ExpiresAtUtc <= Now)
        {
            await ExpireAsync(row, Guid.NewGuid(), ct);
            throw InvalidSession();
        }
        try
        {
            await EnsureRealActorStillActiveAsync(real, ct);
            var viewed = await EnsureTargetStillActiveAsync(row, ct);
            var support = new SupportSessionContext(row.Id, row.RealActorUserId,
                EffectiveSubject.Viewed(viewed), row.ExpiresAtUtc);
            return AuthorityContext.ForValidatedViewAs(real, EffectiveSubject.Viewed(viewed), support, Now);
        }
        catch (ApplicationFailure)
        {
            await EndInvalidAsync(row, Guid.NewGuid(), ct);
            throw InvalidSession();
        }
    }

    public async Task<ViewAsSessionView?> EndAsync(RealActor real, string? cookieValue,
        Guid correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cookieValue)) return null;
        var row = await LoadByCookieAsync(real.UserId, cookieValue, ct);
        if (row.EndedAtUtc is null)
        {
            if (row.ExpiresAtUtc <= Now)
                await ExpireAsync(row, correlationId, ct);
            else
            {
                row.EndedAtUtc = Now;
                db.AuditEvents.Add(Audit(row, real.UserId, "ViewAsEnded", "view-as-end", null, correlationId, row.EndedAtUtc.Value));
                await db.SaveChangesAsync(ct);
            }
        }
        return View(row);
    }

    public async Task RecordBlockedAsync(AuthorityContext authority, string operation,
        Guid correlationId, CancellationToken ct = default)
    {
        if (authority.SupportSession is not { } support) return;
        var row = await db.SupportSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == support.SessionId
            && x.RealActorUserId == authority.RealActor.UserId, ct);
        if (row is null) return;
        var now = Now;
        db.AuditEvents.Add(Audit(row, authority.RealActor.UserId, "ViewAsActionBlocked", operation,
            "View As is read-only.", correlationId, now));
        await db.SaveChangesAsync(ct);
    }

    private async Task<Actor> DemandRealPlatformAsync(AuthorityContext authority, CancellationToken ct)
    {
        if (authority.RealActor.Role != ActorRole.PlatformAdmin
            || !authority.Authority.IsPlatformAdmin || authority.CommandActor.UserId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Forbidden, "An active real Platform Admin authority is required.", code: "PlatformAdminAuthorityRequired");
        await EnsureRealActorStillActiveAsync(authority.RealActor, ct);
        return authority.RealActor.Identity;
    }

    private async Task EnsureRealActorStillActiveAsync(RealActor real, CancellationToken ct)
    {
        if (real.Role != ActorRole.PlatformAdmin)
            throw new ApplicationFailure(FailureKind.Forbidden, "Only a Platform Admin may use View As.", code: "ViewAsPlatformAdminRequired");
        var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == real.UserId, ct);
        if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled
            or AccountLifecycleStatus.Cancelled or AccountLifecycleStatus.Revoked)
            throw new ApplicationFailure(FailureKind.Forbidden, "The Platform Admin account is not active.", code: "PlatformAdminInactive");
        if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == real.UserId
            && x.Role == ActorRole.PlatformAdmin && x.SubjectId == real.UserId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "The Platform Admin account is not active.", code: "PlatformAdminInactive");
    }

    private async Task<Actor> ResolveTargetAsync(Guid viewedUserId, CancellationToken ct)
    {
        if (viewedUserId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Validation, "Choose an account to view.");
        var permissions = await db.CommercePermissions.AsNoTracking()
            .Where(x => x.UserId == viewedUserId && x.IsActive)
            .ToListAsync(ct);
        var roles = permissions.Select(x => x.Role).Distinct().ToArray();
        if (roles.Any(ForbiddenTargetRoles.Contains))
            throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin and Cashier accounts cannot be viewed.", code: "ViewAsTargetRoleForbidden");
        var candidates = permissions.Where(x => AdministrativeAuthority.CanBeViewedAs(x.Role)).ToArray();
        if (candidates.Length == 0)
            throw new ApplicationFailure(FailureKind.NotFound, "The target account is not an active View As account.");
        if (candidates.Length != 1)
            throw new ApplicationFailure(FailureKind.Validation, "The target account has more than one active View As role; choose an unambiguous account.", code: "ViewAsTargetAmbiguous");
        var permission = candidates[0];
        var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == viewedUserId, ct);
        if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled
            or AccountLifecycleStatus.Cancelled or AccountLifecycleStatus.Revoked)
            throw new ApplicationFailure(FailureKind.Forbidden, "The target account is not active.", code: "ViewAsTargetInactive");
        return permission.Role switch
        {
            ActorRole.Business => new Actor(viewedUserId, permission.Role,
                BusinessId: permission.BusinessId ?? permission.SubjectId),
            ActorRole.Creator => new Actor(viewedUserId, permission.Role, CreatorId: permission.SubjectId),
            ActorRole.Customer => new Actor(viewedUserId, permission.Role, CustomerId: permission.SubjectId),
            ActorRole.OperationsAdmin => new Actor(viewedUserId, permission.Role),
            _ => throw new ApplicationFailure(FailureKind.Forbidden, "The target account cannot be viewed.", code: "ViewAsTargetRoleForbidden")
        };
    }

    private async Task<Actor> EnsureTargetStillActiveAsync(SupportSessionRecord row, CancellationToken ct)
    {
        var target = await ResolveTargetAsync(row.ViewedUserId, ct);
        if (target.Role != row.ViewedRole || target.BusinessId != row.ViewedBusinessId
            || target.CreatorId != row.ViewedCreatorId || target.CustomerId != row.ViewedCustomerId)
            throw InvalidSession();
        return target;
    }

    private async Task<SupportSessionRecord> LoadByCookieAsync(Guid realActorUserId, string cookieValue, CancellationToken ct)
    {
        if (!Guid.TryParse(cookieValue, out var sessionId) || sessionId == Guid.Empty)
            throw InvalidSession();
        var row = await db.SupportSessions.SingleOrDefaultAsync(x => x.Id == sessionId
            && x.RealActorUserId == realActorUserId && x.SessionIdentifierHash == Hash(sessionId), ct);
        return row ?? throw InvalidSession();
    }

    private async Task ExpireAsync(SupportSessionRecord row, Guid correlationId, CancellationToken ct)
    {
        if (row.EndedAtUtc is not null) return;
        row.EndedAtUtc = Now;
        db.AuditEvents.Add(Audit(row, row.RealActorUserId, "ViewAsExpired", "view-as-expire",
            "The bounded View As lifetime elapsed.", correlationId, row.EndedAtUtc.Value));
        await db.SaveChangesAsync(ct);
    }

    private async Task EndInvalidAsync(SupportSessionRecord row, Guid correlationId, CancellationToken ct)
    {
        if (row.EndedAtUtc is not null) return;
        row.EndedAtUtc = Now;
        db.AuditEvents.Add(Audit(row, row.RealActorUserId, "ViewAsEnded", "view-as-end",
            "The real actor or viewed authority is no longer active.", correlationId, row.EndedAtUtc.Value));
        await db.SaveChangesAsync(ct);
    }

    private static ViewAsStartResult Result(SupportSessionRecord row)
        => new(View(row), row.Id.ToString("D"));

    private static ViewAsSessionView View(SupportSessionRecord row)
        => new(row.Id, row.ViewedUserId, row.ViewedRole.ToString(), row.ViewedBusinessId,
            row.ViewedCreatorId, row.ViewedCustomerId, row.CreatedAtUtc, row.ExpiresAtUtc);

    private static AuditEvent Audit(SupportSessionRecord row, Guid actorUserId, string eventType,
        string operation, string? reason, Guid correlationId, DateTime occurredAtUtc)
        => new(Guid.NewGuid(), eventType, actorUserId, row.ViewedBusinessId, null, row.ViewedCreatorId,
            correlationId, occurredAtUtc, $"viewedUserId={row.ViewedUserId:D};viewedRole={row.ViewedRole};supportSessionId={row.Id:D}",
            SupportSessionId: row.Id, TargetUserId: row.ViewedUserId, TargetRole: row.ViewedRole,
            TargetSubjectId: row.ViewedBusinessId ?? row.ViewedCreatorId ?? row.ViewedCustomerId,
            Operation: operation, Reason: reason);

    private static string? CleanReason(string? reason)
    {
        var clean = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (clean?.Length > 500) throw new ApplicationFailure(FailureKind.Validation, "The View As reason is too long.");
        return clean;
    }

    private static string Hash(Guid value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString("N"))));
    private static ApplicationFailure InvalidSession() => new(FailureKind.Forbidden,
        "The View As session is invalid, expired, ended, or unavailable.", code: "InvalidViewAsSession");
}
