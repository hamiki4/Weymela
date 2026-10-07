using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Deposits;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Notifications;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class OperationalDepositTests(PostgresFixture fixture)
{
    private static readonly Guid Destination = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Actor Admin = Phase4Scenario.Admin;
    private async Task<Scenario> Setup()
    {
        var s = await Scenario.Create(fixture, 0); await using var db = s.Database.Open();
        db.CommercePermissions.AddRange(new(s.Business.UserId, ActorRole.Business, s.Business.BusinessId!.Value, s.Business.BusinessId, true, false),
            new(Admin.UserId, ActorRole.PlatformAdmin, Admin.UserId, null, true, false));
        await db.SaveChangesAsync(); return s;
    }
    private static DepositService Service(WeymelaDbContext db) => new(db, new ManualApprovalDepositProvider(), new TestClock());
    private static Task<DepositReceipt> Submit(WeymelaDbContext db, Scenario s, decimal amount = 17.23m, string key = "submit", string reference = "payment-1") =>
        Service(db).SubmitAsync(s.Business, new(amount, reference, "proof-reference", Destination), key, default);
    private static Task<Guid> Review(WeymelaDbContext db, Guid id, bool approved = true, string key = "review") =>
        new FinancialCommands(db, new TestClock()).ReviewDepositAsync(Admin, id, new(approved, 0, "BANK-CONFIRMED-1"), key);

    [Fact] public async Task Pending_arbitrary_deposit_does_not_credit_the_wallet()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var r = await Submit(db, s);
        Assert.Equal("Pending", r.Status); Assert.Equal(17.23m, r.Amount);
        Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount); Assert.Empty(await db.FinancialJournals.ToListAsync());
    }
    [Fact] public async Task Submission_replay_returns_one_request_and_conflicting_amount_is_rejected()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var first = await Submit(db, s);
        Assert.Equal(first.Id, (await Submit(db, s)).Id);
        Assert.Equal(FailureKind.IdempotencyConflict, (await Assert.ThrowsAsync<ApplicationFailure>(() => Submit(db, s, 99))).Kind);
        Assert.Single(await db.DepositRequests.ToListAsync());
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1.001)]
    public async Task Invalid_deposit_amount_cannot_create_pending_request(decimal value)
    {
        var s = await Setup(); await using var db = s.Database.Open();
        await Assert.ThrowsAnyAsync<Exception>(() => Submit(db, s, value)); Assert.Empty(await db.DepositRequests.ToListAsync());
    }
    [Fact] public async Task Admin_approval_credits_exact_amount_and_reuses_existing_balanced_journal_engine()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var pending = await Submit(db, s);
        await Review(db, pending.Id); await Review(db, pending.Id);
        Assert.Equal(17.23m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        var request = await db.DepositRequests.SingleAsync(); Assert.Equal(Admin.UserId, request.ReviewedBy);
        var journal = await db.FinancialJournals.SingleAsync(); Assert.Equal(journal.Id, request.JournalId);
        Assert.Empty(await new ReconciliationService(db).CheckAsync(Admin, default));
    }
    [Fact] public async Task Rejection_never_credits_funds_and_review_is_final()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var pending = await Submit(db, s);
        await Review(db, pending.Id, false); await Assert.ThrowsAsync<ApplicationFailure>(() => Review(db, pending.Id, true, "other-key"));
        Assert.Empty(await db.FinancialJournals.ToListAsync()); Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount);
    }
    [Fact] public async Task Business_cannot_approve_its_own_deposit()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var pending = await Submit(db, s);
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).ReviewDepositAsync(s.Business, pending.Id, new(true, 0, "ref"), "key"));
        Assert.Equal(FailureKind.Forbidden, error.Kind);
    }
    [Fact] public async Task One_external_confirmed_payment_cannot_credit_twice()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var first = await Submit(db, s); await Review(db, first.Id);
        var second = await Submit(db, s, key: "second", reference: "other-request");
        await Assert.ThrowsAsync<ApplicationFailure>(() => Review(db, second.Id, key: "second-review"));
        Assert.Single(await db.FinancialJournals.ToListAsync());
    }
    [Fact] public async Task Failure_in_review_outbox_rolls_back_credit_review_journal_and_idempotency()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var pending = await Submit(db, s);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION v3.fail_review() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            IF NEW."EventType"='DepositReviewed' THEN RAISE EXCEPTION 'Injected'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_review BEFORE INSERT ON v3."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION v3.fail_review();
            """);
        await Assert.ThrowsAnyAsync<Exception>(() => Review(db, pending.Id));
        Assert.Equal(DepositReviewStatus.Pending, (await db.DepositRequests.SingleAsync()).Status);
        Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount); Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.OperationType == "ReviewDeposit"));
    }
    [Fact] public async Task Disabled_provider_cannot_fake_live_funding()
    {
        var s = await Setup(); await using var db = s.Database.Open();
        await Assert.ThrowsAsync<ApplicationFailure>(() => new DepositService(db, new DisabledDepositProvider(), new TestClock()).SubmitAsync(s.Business, new(17.23m, "ref", null, Destination), "key", default));
        Assert.Empty(await db.DepositRequests.ToListAsync());
    }
    [Fact] public async Task Reviewed_deposit_history_cannot_be_deleted_or_rewritten()
    {
        var s = await Setup(); await using var db = s.Database.Open(); var pending = await Submit(db, s); await Review(db, pending.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM v3.\"DepositRequests\" WHERE \"Id\"={pending.Id}"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE v3.\"DepositRequests\" SET \"Amount\"=999 WHERE \"Id\"={pending.Id}"));
    }
    [Fact] public async Task Concurrent_admin_approvals_commit_at_most_one_credit()
    {
        var s=await Setup(); Guid id; await using(var db=s.Database.Open()) id=(await Submit(db,s)).Id;
        async Task<bool> Attempt(string key) { await using var db=s.Database.Open(); try { await Review(db,id,key:key);return true; } catch(ApplicationFailure) {return false;} }
        var results=await Task.WhenAll(Attempt("review-one"),Attempt("review-two")); Assert.Single(results,x=>x);
        await using var read=s.Database.Open(); Assert.Single(await read.FinancialJournals.ToListAsync());
        Assert.Equal(17.23m,(await read.BusinessWallets.SingleAsync()).TotalBalance.Amount);
    }
    [Fact] public async Task Approved_deposit_funds_promotion_through_authoritative_available_balance()
    {
        var s = await Setup(); await using var db = s.Database.Open();
        var pending = await Submit(db, s, 10000m);
        Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).FundPromotionAsync(
            new(s.Business, s.PromotionId, 0, 0, "early-promotion", Scenario.Now)));
        await Review(db, pending.Id);
        var wallet = await db.BusinessWallets.SingleAsync();
        Assert.Equal(10000m, wallet.AvailableBalance.Amount);
        await new FinancialCommands(db).FundPromotionAsync(new(s.Business, s.PromotionId, 0, wallet.Version, "fund-after-approval", Scenario.Now));
        db.ChangeTracker.Clear();
        wallet = await db.BusinessWallets.SingleAsync();
        Assert.Equal(4000m, wallet.AvailableBalance.Amount);
        Assert.Equal(6000m, wallet.ReservedBalance.Amount);
        Assert.Empty(await new ReconciliationService(db).CheckAsync(Admin, default));
    }
    [Fact] public async Task Deposit_submission_notifies_both_admin_roles_with_wallet_review_route()
    {
        var s = await Setup(); var operations = new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin);
        await using var db = s.Database.Open();
        db.CommercePermissions.Add(new(operations.UserId, operations.Role, operations.UserId, null, true, false));
        await db.SaveChangesAsync();
        var pending = await Submit(db, s);
        var processor = new OutboxProcessor(db, new RuntimeOptions { WorkerBatchSize = 50, RecipientBatchSize = 100 }, new DisabledPushProvider(), new TestClock());
        for (var i = 0; i < 5 && await processor.ProcessAsync(default) > 0; i++) { }
        var notices = await db.InAppNotifications.Where(x => x.EventType == "DepositSubmitted").ToListAsync();
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, x => x.UserId == Admin.UserId && x.Role == ActorRole.PlatformAdmin && x.Route == "/admin/wallets");
        Assert.Contains(notices, x => x.UserId == operations.UserId && x.Role == ActorRole.OperationsAdmin && x.Route == "/admin/wallets");
        await Review(db, pending.Id);
        for (var i = 0; i < 5 && await processor.ProcessAsync(default) > 0; i++) { }
        var reviewed = await db.InAppNotifications.Where(x => x.EventType == "DepositReviewed").ToListAsync();
        Assert.Contains(reviewed, x => x.UserId == s.Business.UserId && x.Role == ActorRole.Business && x.Route == "/business/wallet");
    }
}
