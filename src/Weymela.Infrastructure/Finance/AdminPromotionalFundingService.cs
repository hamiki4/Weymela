using System.Data;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;

namespace Weymela.Infrastructure.Finance;

public sealed class AdminPromotionalFundingService(WeymelaDbContext db, TimeProvider clock)
{
    private const string Operation = "AdminPromotionalFunding";

    public async Task<AdminPromotionalFundingReceipt> AddAsync(AuthorityContext authority, Guid businessId,
        AdminPromotionalFundingInput input, string key, CancellationToken ct)
    {
        var actor = authority.CommandActor;
        DemandPlatform(authority);
        if (businessId == Guid.Empty) throw new ApplicationFailure(FailureKind.Validation, "Choose a Business.");
        var amount = WorkspaceCommands.Amount(input.Amount);
        var reason = input.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new ApplicationFailure(FailureKind.Validation, "Enter a reason of 1 to 500 characters.");
        return await new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            await EnsureActivePlatformAsync(actor, token);

            var op = new FinancialOperation(db);
            var fingerprint = RequestFingerprint.Create(businessId.ToString("D"), RequestFingerprint.Amount(amount), reason);
            var replay = await op.Replay(actor, Operation, key, fingerprint, token);
            if (replay is not null)
                return Receipt(await db.PlatformPromotionalFundings.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(replay), token));

            if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Business
                && x.SubjectId == businessId && x.IsActive, token))
                throw new ApplicationFailure(FailureKind.NotFound, "An active Business account was not found.");
            var wallet = await db.BusinessWallets.SingleOrDefaultAsync(x => x.BusinessId == businessId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Business wallet not found.");
            var name = await DisplayNameAsync(actor.UserId, token);
            var now = clock.GetUtcNow().UtcDateTime;
            var correlation = Guid.NewGuid();
            var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId,
                JournalSourceType.AdminPromotionalFunding, now, key);
            journal.AddLine(JournalLineType.Debit, amount, "PlatformPromotionalFunding");
            journal.AddLine(JournalLineType.Credit, amount, "BusinessAvailable");
            journal.Post();
            wallet.CreditPromotionalFunding(amount, now, correlation);
            db.FinancialJournals.Add(journal);
            db.Entry(journal).Property("BusinessId").CurrentValue = businessId;
            var record = new PlatformPromotionalFundingRecord(Guid.NewGuid(), businessId, amount, reason,
                actor.UserId, name, now, correlation, key, fingerprint, journal.Id);
            db.PlatformPromotionalFundings.Add(record);
            db.WalletEntries.Add(new(Guid.NewGuid(), businessId, null, amount, Operation, journal.Id, now));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AdminPromotionalFundingAdded", actor.UserId,
                businessId, null, null, correlation, now, $"fundingId={record.Id:D}",
                TargetSubjectId: businessId, Operation: Operation));
            op.Remember(actor, Operation, key, fingerprint, record.Id.ToString("D"), now);
            return Receipt(record);
        }, ct);
    }

    public async Task<IReadOnlyList<AdminPromotionalFundingReceipt>> BusinessHistoryAsync(AuthorityContext authority,
        Guid businessId, CancellationToken ct)
    {
        DemandPlatform(authority);
        await EnsureActivePlatformAsync(authority.CommandActor, ct);
        if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Business
            && x.SubjectId == businessId, ct))
            throw new ApplicationFailure(FailureKind.NotFound, "Business account not found.");
        return (await db.PlatformPromotionalFundings.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct)).Select(Receipt).ToArray();
    }

    private static void DemandPlatform(AuthorityContext authority)
    {
        if (authority.RealActor.Role != ActorRole.PlatformAdmin || !authority.Authority.Allows(AdministrativeCapability.PlatformPromotionalFunding)
            || authority.CommandActor.UserId != authority.RealActor.UserId)
            throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin promotional funding authority is required.");
    }

    private async Task EnsureActivePlatformAsync(Actor actor, CancellationToken ct)
    {
        if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == actor.UserId
            && x.Role == ActorRole.PlatformAdmin && x.SubjectId == actor.UserId && x.IsActive, ct)
            || await db.AccountLifecycles.AsNoTracking().AnyAsync(x => x.UserId == actor.UserId
                && x.Status != AccountLifecycleStatus.Active, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "An active Platform Admin is required.");
    }

    private async Task<string> DisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var grantName = await db.AdminGrants.AsNoTracking().Where(x => x.UserId == userId
            && x.Role == ActorRole.PlatformAdmin && x.IsActive)
            .OrderByDescending(x => x.GrantedAtUtc).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(grantName)) return grantName.Trim();
        var profileName = await (from permission in db.CommercePermissions.AsNoTracking()
            join profile in db.PublicWorkspaceProfiles.AsNoTracking()
                on new { permission.SubjectId, permission.Role } equals new { profile.SubjectId, profile.Role }
            where permission.UserId == userId && permission.IsActive
                && (permission.Role == ActorRole.Customer || permission.Role == ActorRole.Creator)
            select profile.DisplayName).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(profileName)) return profileName.Trim();
        var email = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == userId
            && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null)
            .Select(x => x.DeliveryAddress).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(email)) return email.Split('@')[0];
        throw new ApplicationFailure(FailureKind.Validation, "The Platform Admin needs a verified display identity before adding funds.");
    }

    private static AdminPromotionalFundingReceipt Receipt(PlatformPromotionalFundingRecord row) =>
        new(row.Id, row.BusinessId, row.Amount.Amount, row.Reason, row.PlatformAdminUserId,
            row.PlatformAdminDisplayNameSnapshot, row.CreatedAtUtc, row.CorrelationId, row.JournalId);
}
