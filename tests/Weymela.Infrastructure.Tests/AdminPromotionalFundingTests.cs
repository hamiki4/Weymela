using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class AdminPromotionalFundingTests(PostgresFixture fixture)
{
    private static readonly Actor Admin = Phase4Scenario.Admin;
    private static readonly AdminPromotionalFundingInput Input = new(5000m, "Launch promotion support");

    private async Task<Scenario> Setup()
    {
        var scenario = await Scenario.Create(fixture, 0);
        await using var db = scenario.Database.Open();
        db.CommercePermissions.AddRange(
            new(scenario.Business.UserId, ActorRole.Business, scenario.Business.BusinessId!.Value, scenario.Business.BusinessId, true, false),
            new(Admin.UserId, ActorRole.PlatformAdmin, Admin.UserId, null, true, false));
        db.AdminGrants.Add(new AdminGrantRecord { UserId = Admin.UserId, Role = ActorRole.PlatformAdmin,
            DisplayName = "Amina Admin", GrantedByUserId = Admin.UserId, GrantedAtUtc = Scenario.Now });
        await db.SaveChangesAsync();
        return scenario;
    }

    [Fact]
    public async Task Funding_is_distinct_balanced_immutable_and_reconciled()
    {
        var scenario = await Setup(); await using var db = scenario.Database.Open();
        var service = new AdminPromotionalFundingService(db, new TestClock());
        var authority = AuthorityContext.ForAuthenticatedActor(Admin);
        var first = await service.AddAsync(authority, scenario.Business.BusinessId!.Value, Input, "fund-one", default);
        var replay = await service.AddAsync(authority, scenario.Business.BusinessId.Value, Input, "fund-one", default);
        Assert.Equal(first, replay);
        Assert.Equal("Amina Admin", first.PlatformAdminDisplayName);
        Assert.Equal(Admin.UserId, first.PlatformAdminUserId);
        Assert.Equal(5000m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        var record = await db.PlatformPromotionalFundings.SingleAsync();
        Assert.Equal(first.JournalId, record.JournalId);
        Assert.Equal("Launch promotion support", record.Reason);
        var journal = await db.FinancialJournals.Include(x => x.Lines).SingleAsync();
        Assert.Equal(JournalSourceType.AdminPromotionalFunding, journal.SourceType);
        Assert.Equal(Admin.UserId, journal.ActorId);
        Assert.Contains(journal.Lines, x => x.Type == JournalLineType.Debit && x.Account == "PlatformPromotionalFunding" && x.Amount.Amount == 5000m);
        Assert.Contains(journal.Lines, x => x.Type == JournalLineType.Credit && x.Account == "BusinessAvailable" && x.Amount.Amount == 5000m);
        Assert.DoesNotContain(journal.Lines, x => x.Account is "CashClearing" or "PlatformRevenue");
        Assert.Equal("AdminPromotionalFunding", (await db.WalletEntries.SingleAsync()).Movement);
        Assert.Empty(await db.DepositRequests.ToListAsync());
        Assert.Empty(await db.PlatformRevenueEntries.ToListAsync());
        var wallet = await new WorkspaceQueries(db, new DevelopmentDirectory(), new TestClock()).WalletAsync(scenario.Business, default);
        Assert.Equal("Promotional funds from Weymela", Assert.Single(wallet.History).Label);
        Assert.Equal(Input.Reason, wallet.History[0].Reason);
        Assert.Equal(first.Id.ToString("D"), wallet.History[0].Reference);
        Assert.Empty(await new ReconciliationService(db).CheckAsync(Admin, default));
        Assert.Single(await service.BusinessHistoryAsync(authority, scenario.Business.BusinessId.Value, default));
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "AdminPromotionalFundingAdded" && x.CorrelationId == first.CorrelationId);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE v3.\"PlatformPromotionalFundings\" SET \"Amount\"=999 WHERE \"Id\"={first.Id}"));
    }

    [Fact]
    public async Task Same_key_with_changed_business_amount_or_reason_conflicts()
    {
        var scenario = await Setup(); await using var db = scenario.Database.Open();
        var service = new AdminPromotionalFundingService(db, new TestClock());
        var authority = AuthorityContext.ForAuthenticatedActor(Admin);
        await service.AddAsync(authority, scenario.Business.BusinessId!.Value, Input, "same-key", default);
        foreach (var (business, input) in new[] {
            (scenario.Business.BusinessId.Value, new AdminPromotionalFundingInput(5001m, Input.Reason)),
            (scenario.Business.BusinessId.Value, new AdminPromotionalFundingInput(5000m, "Different reason")),
            (Guid.NewGuid(), Input) })
        {
            var error = await Assert.ThrowsAsync<ApplicationFailure>(() => service.AddAsync(authority, business, input, "same-key", default));
            Assert.Equal(FailureKind.IdempotencyConflict, error.Kind);
        }
        Assert.Single(await db.PlatformPromotionalFundings.ToListAsync());
        Assert.Equal(5000m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
    }

    [Theory]
    [InlineData(ActorRole.OperationsAdmin)] [InlineData(ActorRole.Business)] [InlineData(ActorRole.Creator)]
    [InlineData(ActorRole.Customer)] [InlineData(ActorRole.Cashier)]
    public async Task Non_platform_roles_cannot_fund_or_view(ActorRole role)
    {
        var scenario = await Setup(); await using var db = scenario.Database.Open();
        var service = new AdminPromotionalFundingService(db, new TestClock());
        var authority = AuthorityContext.ForAuthenticatedActor(new Actor(Guid.NewGuid(), role));
        Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.AddAsync(authority, scenario.Business.BusinessId!.Value, Input, "denied", default))).Kind);
        Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.BusinessHistoryAsync(authority, scenario.Business.BusinessId!.Value, default))).Kind);
        Assert.Empty(await db.PlatformPromotionalFundings.ToListAsync());
    }

    [Theory]
    [InlineData(0, "Reason")] [InlineData(-1, "Reason")] [InlineData(1.001, "Reason")]
    [InlineData(10, "")] [InlineData(10, "   ")]
    public async Task Invalid_amount_or_reason_does_not_post(decimal amount, string reason)
    {
        var scenario = await Setup(); await using var db = scenario.Database.Open();
        var service = new AdminPromotionalFundingService(db, new TestClock());
        Assert.Equal(FailureKind.Validation, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.AddAsync(AuthorityContext.ForAuthenticatedActor(Admin), scenario.Business.BusinessId!.Value,
                new(amount, reason), "invalid", default))).Kind);
        Assert.Empty(await db.PlatformPromotionalFundings.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }

    [Fact]
    public async Task Failure_during_record_insert_rolls_back_wallet_journal_movement_and_audit()
    {
        var scenario = await Setup(); await using var db = scenario.Database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION v3.fail_promotional_funding_test() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected funding failure'; END $$;
            CREATE TRIGGER fail_promotional_funding_test BEFORE INSERT ON v3."PlatformPromotionalFundings"
              FOR EACH ROW EXECUTE FUNCTION v3.fail_promotional_funding_test();
            """);
        var service = new AdminPromotionalFundingService(db, new TestClock());
        await Assert.ThrowsAnyAsync<Exception>(() => service.AddAsync(AuthorityContext.ForAuthenticatedActor(Admin),
            scenario.Business.BusinessId!.Value, Input, "failure", default));
        Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.Empty(await db.WalletEntries.ToListAsync());
        Assert.Empty(await db.PlatformPromotionalFundings.ToListAsync());
        Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.OperationType == "AdminPromotionalFunding"));
        Assert.False(await db.AuditEvents.AnyAsync(x => x.EventType == "AdminPromotionalFundingAdded"));
    }

    [Fact]
    public async Task Concurrent_same_key_requests_credit_at_most_once()
    {
        var scenario = await Setup();
        async Task<bool> Attempt()
        {
            await using var db = scenario.Database.Open();
            try
            {
                await new AdminPromotionalFundingService(db, new TestClock()).AddAsync(
                    AuthorityContext.ForAuthenticatedActor(Admin), scenario.Business.BusinessId!.Value, Input, "double-click", default);
                return true;
            }
            catch (ApplicationFailure error) when (error.Kind is FailureKind.ConcurrencyConflict or FailureKind.IdempotencyConflict) { return false; }
        }
        var results = await Task.WhenAll(Attempt(), Attempt());
        Assert.Contains(true, results);
        await using var read = scenario.Database.Open();
        Assert.Single(await read.PlatformPromotionalFundings.ToListAsync());
        Assert.Single(await read.FinancialJournals.ToListAsync());
        Assert.Equal(5000m, (await read.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Empty(await new ReconciliationService(read).CheckAsync(Admin, default));
    }

    [Fact]
    public async Task Inactive_platform_admin_and_inactive_business_are_denied()
    {
        var scenario = await Setup(); await using var db = scenario.Database.Open();
        var service = new AdminPromotionalFundingService(db, new TestClock());
        var authority = AuthorityContext.ForAuthenticatedActor(Admin);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == Admin.UserId);
        permission.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(FailureKind.Forbidden, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.AddAsync(authority, scenario.Business.BusinessId!.Value, Input, "inactive-admin", default))).Kind);
        permission = await db.CommercePermissions.SingleAsync(x => x.UserId == Admin.UserId);
        permission.IsActive = true;
        var businessPermission = await db.CommercePermissions.SingleAsync(x => x.UserId == scenario.Business.UserId);
        businessPermission.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(FailureKind.NotFound, (await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.AddAsync(authority, scenario.Business.BusinessId!.Value, Input, "inactive-business", default))).Kind);
        Assert.Empty(await db.PlatformPromotionalFundings.ToListAsync());
    }
}
