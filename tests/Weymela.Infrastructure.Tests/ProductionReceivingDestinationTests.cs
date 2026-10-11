using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Deposits;
using Weymela.Infrastructure.Operations;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ProductionReceivingDestinationTests(PostgresFixture fixture)
{
    private static readonly RuntimeOptions Production = new() { EnvironmentName = "Production" };

    [Fact]
    public async Task Production_excludes_migration_examples_and_rejects_deposits_without_financial_records()
    {
        var scenario = await Scenario.Create(fixture, 0);
        await using var db = scenario.Database.Open();
        db.CommercePermissions.Add(new(scenario.Business.UserId, ActorRole.Business,
            scenario.Business.BusinessId!.Value, scenario.Business.BusinessId, true, false));
        await db.SaveChangesAsync();
        var destinations = new ReceivingDestinationService(db, TimeProvider.System, Production);
        Assert.Empty(await destinations.ActiveAsync(scenario.Business));
        Assert.All(await destinations.AdminAsync(Phase4Scenario.Admin), x => Assert.False(x.IsActive));
        var deposits = new DepositService(db, new ManualApprovalDepositProvider(), TimeProvider.System, Production);
        foreach (var id in await db.PlatformReceivingDestinations.Select(x => x.Id).ToListAsync())
            await Assert.ThrowsAsync<ApplicationFailure>(() => deposits.SubmitAsync(scenario.Business,
                new DepositSubmission(10, "isolated-reference", null, id), "isolated-key", default));
        Assert.Empty(await db.DepositRequests.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
        Assert.Equal(0, (await db.BusinessWallets.SingleAsync()).TotalBalance.Amount);
    }

    [Fact]
    public async Task Only_platform_admin_can_replace_examples_and_activate_a_production_destination()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var service = new ReceivingDestinationService(db, TimeProvider.System, Production);
        var row = await db.PlatformReceivingDestinations.OrderBy(x => x.SortOrder).FirstAsync();
        var input = new ReceivingDestinationInput(row.Method, row.Name, row.AccountReference, true, row.SortOrder, row.Version);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.UpdateAsync(
            new Actor(Phase4Scenario.Admin.UserId, ActorRole.OperationsAdmin), row.Id, input));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.UpdateAsync(Phase4Scenario.Admin, row.Id, input));
        Assert.Empty(await db.AuditEvents.ToListAsync());
        var approved = await service.UpdateAsync(Phase4Scenario.Admin, row.Id,
            input with { AccountReference = "isolated-owner-confirmed-account" });
        Assert.True(approved.IsActive);
        Assert.True(approved.Version > 0);
        Assert.Equal(Phase4Scenario.Admin.UserId, row.UpdatedBy);
        Assert.Single(await service.ActiveAsync(new Actor(Guid.NewGuid(), ActorRole.Business, Guid.NewGuid())));
        Assert.Single(await db.AuditEvents.ToListAsync());
        Assert.Empty(await db.FinancialJournals.ToListAsync());
    }
}
