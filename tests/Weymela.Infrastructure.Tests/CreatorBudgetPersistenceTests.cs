using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class CreatorBudgetPersistenceTests(PostgresFixture fixture)
{
    [Fact] public async Task Approved_creator_budget_assignment_persists_and_updates_unassigned_reserve()
    {
        var s = await Scenario.Create(fixture, funded: true); var creator = await s.ApprovedCreator(); var id = await s.Assign(creator);
        await using var db = s.Database.Open();
        var a = await db.CreatorAllocations.SingleAsync(); Assert.Equal(id, a.Id); Assert.Equal(2000, a.OriginalAllocation.Amount);
        var p = await db.Promotions.Include(x => x.Allocations).SingleAsync(); Assert.Equal(4000, p.UnallocatedBudget.Amount);
        Assert.True(await db.PromotionBudgetEntries.AnyAsync(x => x.Movement == "Assigned"));
    }

    [Fact] public async Task Pending_or_absent_application_cannot_receive_creator_budget()
    {
        var s = await Scenario.Create(fixture, funded: true);
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Assign(Guid.NewGuid()));
        await using var db = s.Database.Open(); Assert.Empty(await db.CreatorAllocations.ToListAsync());
    }

    [Fact] public async Task Creator_budget_cannot_exceed_campaign_unassigned_amount()
    {
        var s = await Scenario.Create(fixture, funded: true); var creator = await s.ApprovedCreator();
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Assign(creator, 6000.01m));
        await using var db = s.Database.Open(); Assert.Empty(await db.CreatorAllocations.ToListAsync());
    }

    [Fact] public async Task Two_creators_have_isolated_budgets()
    {
        var s = await Scenario.Create(fixture, funded: true); var a = await s.Assign(await s.ApprovedCreator(), 2000, "a");
        var b = await s.Assign(await s.ApprovedCreator(), 1000, "b");
        await using var db = s.Database.Open();
        await new FinancialCommands(db).IncreaseCreatorAllocationAsync(new(s.Business, a, new Money(500), 0, Scenario.Now), "top-up");
        Assert.Equal(1000, (await db.CreatorAllocations.SingleAsync(x => x.Id == b)).RemainingAmount.Amount);
        Assert.Equal(2500, (await db.CreatorAllocations.SingleAsync(x => x.Id == a)).RemainingAmount.Amount);
    }

    [Fact] public async Task Active_creator_budget_top_up_persists()
    {
        var s = await Scenario.Create(fixture, funded: true); var a = await s.Assign(await s.ApprovedCreator());
        await using var db = s.Database.Open(); var allocation = await db.CreatorAllocations.SingleAsync();
        allocation.Activate(Scenario.Now); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await new FinancialCommands(db).IncreaseCreatorAllocationAsync(new(s.Business, a, new Money(500), 1, Scenario.Now), "increase");
        Assert.Equal(2500, (await db.CreatorAllocations.SingleAsync()).OriginalAllocation.Amount);
    }

    [Fact] public async Task Top_up_over_remaining_campaign_budget_rolls_back()
    {
        var s = await Scenario.Create(fixture, funded: true); var id = await s.Assign(await s.ApprovedCreator(), 5000);
        await using var db = s.Database.Open();
        await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(db).IncreaseCreatorAllocationAsync(new(s.Business, id, new Money(1001), 0, Scenario.Now), "over"));
        Assert.Equal(5000, (await db.CreatorAllocations.SingleAsync()).OriginalAllocation.Amount);
        Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.Key == "over"));
    }

    [Fact] public async Task Active_reduction_is_rejected_even_through_raw_database_update()
    {
        var s = await Scenario.Create(fixture, funded: true); var id = await s.Assign(await s.ApprovedCreator());
        await using var db = s.Database.Open(); var a = await db.CreatorAllocations.SingleAsync();
        a.Activate(Scenario.Now); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE v3.\"CreatorAllocations\" SET \"OriginalAllocation\" = 1000 WHERE \"Id\" = {id}"));
    }

    [Fact] public async Task Participation_completion_returns_only_unused_budget_to_campaign()
    {
        var s = await Scenario.Create(fixture, funded: true); var id = await s.Assign(await s.ApprovedCreator());
        await ConsumeForFixture(s, id, 500);
        await using var db = s.Database.Open(); var a = await db.CreatorAllocations.SingleAsync();
        await new FinancialCommands(db).CompleteCreatorParticipationAsync(new(s.Business, id, a.Version, Scenario.Now), "complete");
        var p = await db.Promotions.Include(x => x.Allocations).SingleAsync();
        Assert.Equal(5500, p.UnallocatedBudget.Amount); Assert.Equal(500, p.UsedBudget.Amount);
        Assert.Equal(5500, p.ReservedBudget.Amount);
        Assert.Equal(4000, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Equal(5500, (await db.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
    }

    [Fact] public async Task Completed_budget_cannot_be_consumed_again()
    {
        var s = await Scenario.Create(fixture, funded: true); var id = await s.Assign(await s.ApprovedCreator());
        await using var db = s.Database.Open();
        await new FinancialCommands(db).CompleteCreatorParticipationAsync(new(s.Business, id, 0, Scenario.Now), "complete");
        var a = await db.CreatorAllocations.SingleAsync();
        Assert.Throws<InvalidOperationException>(() => a.Consume(new Money(1), Scenario.Now));
    }

    [Fact] public async Task Completion_replay_does_not_release_unused_creator_budget_twice()
    {
        var s = await Scenario.Create(fixture, funded: true); var id = await s.Assign(await s.ApprovedCreator());
        await using var db = s.Database.Open(); var commands = new FinancialCommands(db);
        var c = new CompleteCreatorParticipationCommand(s.Business, id, 0, Scenario.Now);
        await commands.CompleteCreatorParticipationAsync(c, "complete"); await commands.CompleteCreatorParticipationAsync(c, "complete");
        Assert.Single(await db.PromotionBudgetEntries.Where(x => x.Movement == "UnusedReturnedToCampaign").ToListAsync());
        Assert.Equal(4000, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
    }

    [Fact] public async Task Stale_allocation_token_prevents_top_up()
    {
        var s = await Scenario.Create(fixture, funded: true); var id = await s.Assign(await s.ApprovedCreator());
        await using var db = s.Database.Open(); var c = new FinancialCommands(db);
        await c.IncreaseCreatorAllocationAsync(new(s.Business, id, new Money(100), 0, Scenario.Now), "first");
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => c.IncreaseCreatorAllocationAsync(new(s.Business, id, new Money(100), 0, Scenario.Now), "stale"));
        Assert.Equal(FailureKind.ConcurrencyConflict, ex.Kind);
    }

    [Fact] public async Task Assignment_replay_returns_same_budget_id()
    {
        var s = await Scenario.Create(fixture, funded: true); var creator = await s.ApprovedCreator();
        var first = await s.Assign(creator); var again = await s.Assign(creator);
        Assert.Equal(first, again);
        await using var db = s.Database.Open(); Assert.Single(await db.CreatorAllocations.ToListAsync());
    }

    [Fact] public async Task Concurrent_creator_assignments_cannot_overallocate_campaign()
    {
        var s = await Scenario.Create(fixture, funded: true);
        var creatorA = await s.ApprovedCreator(); var creatorB = await s.ApprovedCreator();
        await using var first = s.Database.Open(); await using var second = s.Database.Open();
        var p = await second.Promotions.Include(x => x.Allocations).SingleAsync();
        await new FinancialCommands(first).AssignCreatorAllocationAsync(new(s.Business, p.Id, creatorA, new Money(3000), p.Version, Scenario.Now), "first");
        var ex = await Assert.ThrowsAsync<ApplicationFailure>(() => new FinancialCommands(second)
            .AssignCreatorAllocationAsync(new(s.Business, p.Id, creatorB, new Money(4000), p.Version, Scenario.Now), "second"));
        // Either optimistic concurrency or the deferred aggregate invariant may win
        // the race; both reject the stale second assignment before any commit.
        Assert.Contains(ex.Kind, new[] { FailureKind.ConcurrencyConflict, FailureKind.Validation });
        await using var read = s.Database.Open();
        Assert.Single(await read.CreatorAllocations.ToListAsync());
        Assert.False(await read.IdempotencyRecords.AnyAsync(x => x.Key == "second"));
    }

    [Fact] public async Task Normal_campaign_completion_keeps_unused_funds_committed()
    {
        var s = await Scenario.Create(fixture, funded: true); await s.Assign(await s.ApprovedCreator());
        await using var db = s.Database.Open(); var p = await db.Promotions.Include(x => x.Allocations).SingleAsync();
        p.Activate(Scenario.Now, Guid.NewGuid()); await db.SaveChangesAsync();
        await new FinancialCommands(db).CompletePromotionAsync(new(s.Business, p.Id, p.Version, Scenario.Now), "complete-campaign");
        var complete = await db.Promotions.Include(x => x.Allocations).SingleAsync();
        Assert.Equal(PromotionStatus.Completed, complete.Status);
        Assert.Equal(6000, complete.UnallocatedBudget.Amount);
        Assert.Equal(6000, complete.ReservedBudget.Amount);
        Assert.Equal(4000, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
    }

    private static async Task ConsumeForFixture(Scenario s, Guid allocationId, decimal amount)
    {
        // Arrange a previously earned amount without implementing the Phase 4 verified-view engine.
        await using var db = s.Database.Open();
        await new EfUnitOfWork(db).ExecuteAsync(async ct =>
        {
            var p = await db.Promotions.Include(x => x.Allocations).SingleAsync(ct);
            p.Activate(Scenario.Now, Guid.NewGuid()); p.Consume(allocationId, new Money(amount), Scenario.Now, Guid.NewGuid());
            (await db.BusinessWallets.SingleAsync(ct)).ConsumeReservedFunds(new Money(amount), Scenario.Now, Guid.NewGuid());
            var j = new FinancialJournal(Guid.NewGuid().ToString(), Guid.NewGuid(), s.Business.UserId, JournalSourceType.ViewReward, Scenario.Now);
            j.AddLine(JournalLineType.Debit, new Money(amount), "CreatorAllocatedReserve");
            j.AddLine(JournalLineType.Credit, new Money(amount), "CreatorPayable"); j.Post(); db.FinancialJournals.Add(j);
            db.Entry(j).Property("BusinessId").CurrentValue = s.Business.BusinessId;
            db.Entry(j).Property("PromotionId").CurrentValue = s.PromotionId;
            return true;
        });
    }
}
