using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Transactions;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class SchemaIntegrityTests(PostgresFixture fixture)
{
    [Fact] public async Task Money_columns_use_explicit_18_2_and_rates_use_9_4()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var money = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema='v3' AND data_type='numeric' AND numeric_precision=18 AND numeric_scale=2
            """).SingleAsync();
        var implicitNumeric = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema='v3' AND data_type='numeric'
              AND NOT ((numeric_precision=18 AND numeric_scale=2)
                OR (numeric_precision=9 AND numeric_scale=4)
                OR (table_name='PublicWorkspaceProfiles' AND column_name IN ('Latitude','Longitude')
                    AND numeric_precision=9 AND numeric_scale=6))
            """).SingleAsync();
        Assert.True(money >= 30); Assert.Equal(0, implicitNumeric);
    }

    [Fact] public async Task Excess_money_precision_is_rejected_instead_of_silently_rounded()
    {
        var s = await Scenario.Create(fixture, 0); await using var db = s.Database.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FinancialCommands(db)
            .CreditDepositAsync(new(s.Business, new Money(1.001m), "fraction", Scenario.Now), 0));
        Assert.Equal(0, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
    }

    [Fact] public async Task Wallet_total_is_database_generated_available_plus_reserved()
    {
        var s = await Scenario.Create(fixture, funded: true); await using var db = s.Database.Open();
        var total = await db.Database.SqlQueryRaw<decimal>("SELECT \"TotalBalance\" AS \"Value\" FROM v3.\"BusinessWallets\"").SingleAsync();
        Assert.Equal(10000, total);
    }

    [Fact] public async Task Database_rejects_negative_wallet_balance()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE v3.\"BusinessWallets\" SET \"AvailableBalance\"=-1"));
    }

    [Fact] public async Task Public_campaign_id_is_unique()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        var original = await db.Promotions.SingleAsync();
        var p = new Promotion(s.Business.BusinessId!.Value, "Other", "", PromotionType.ViewOnly, new Money(100), new(null,null,null,null), Scenario.Now, Scenario.Now.AddDays(1), original.PricingSnapshot with {}, Scenario.Now);
        db.Promotions.Add(p); db.Entry(p).Property(x => x.PublicPromotionId).CurrentValue = original.PublicPromotionId;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task Duplicate_active_creator_application_is_rejected_by_database()
    {
        var s = await Scenario.Create(fixture, funded: true); var creator = await s.ApprovedCreator();
        await using var db = s.Database.Open(); var p = await db.Promotions.SingleAsync();
        db.CreatorApplications.Add(new(p.Id, creator, "Again", null, p, Scenario.Now, Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task Duplicate_creator_budget_is_rejected_by_database()
    {
        var s = await Scenario.Create(fixture, funded: true); var creator = await s.ApprovedCreator(); await s.Assign(creator);
        await using var db = s.Database.Open(); var p = await db.Promotions.Include(x => x.Allocations).SingleAsync();
        db.CreatorAllocations.Add(p.Allocate(creator, new Money(100), Scenario.Now, Guid.NewGuid()));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact] public async Task Idempotency_database_key_is_scoped_to_actor_and_operation()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var actor = Guid.NewGuid();
        db.IdempotencyRecords.Add(new(actor, "Deposit", "same", "a", "result", Scenario.Now));
        db.IdempotencyRecords.Add(new(actor, "Fund", "same", "b", "result", Scenario.Now));
        db.IdempotencyRecords.Add(new(Guid.NewGuid(), "Deposit", "same", "c", "result", Scenario.Now));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.IdempotencyRecords.Add(new(actor, "Deposit", "same", "d", "result", Scenario.Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task Posted_journal_cannot_be_deleted_through_EF()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        db.FinancialJournals.Remove(await db.FinancialJournals.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task Posted_journal_cannot_be_modified_through_SQL()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE v3.\"FinancialJournals\" SET \"Reference\"='tampered'"));
    }

    [Fact] public async Task Posted_journal_cannot_receive_extra_lines_in_later_transaction()
    {
        var s = await Scenario.Create(fixture); await using var db = s.Database.Open();
        var j = await db.FinancialJournals.SingleAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO v3.\"FinancialJournalLines\" (\"Id\",\"JournalId\",\"Type\",\"Amount\",\"Account\") VALUES ({Guid.NewGuid()},{j.Id},'Debit',1,'tamper')"));
    }

    [Fact] public async Task Database_rejects_unbalanced_journal_even_when_EF_is_bypassed()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open(); var id = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."FinancialJournals" ("Id","Reference","CorrelationId","CreatedAtUtc","SourceType","IsPosted")
            VALUES ({id},{id.ToString()},{Guid.NewGuid()},{Scenario.Now},'Deposit',true)
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."FinancialJournalLines" ("Id","JournalId","Type","Amount","Account") VALUES
            ({Guid.NewGuid()},{id},'Debit',10,'a'),({Guid.NewGuid()},{id},'Credit',9,'b')
            """);
        await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
    }

    [Fact] public async Task Funded_pricing_snapshot_is_immutable_in_database()
    {
        var s = await Scenario.Create(fixture, funded: true); await using var db = s.Database.Open();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE v3.\"PricingSnapshots\" SET \"BusinessCharge\"=999"));
    }

    [Fact] public async Task Outbox_failure_rolls_back_deposit_journal_idempotency_and_audit()
    {
        var s = await Scenario.Create(fixture, 0); await using var db = s.Database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION v3.test_outbox_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected outbox failure'; END $$;
            CREATE TRIGGER test_failure BEFORE INSERT ON v3."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION v3.test_outbox_failure();
            """);
        await Assert.ThrowsAnyAsync<Exception>(() => new FinancialCommands(db).CreditDepositAsync(new(s.Business, new Money(50), "fail", Scenario.Now), 0));
        await using var read = s.Database.Open();
        Assert.Equal(0, (await read.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
        Assert.Empty(await read.FinancialJournals.ToListAsync()); Assert.Empty(await read.IdempotencyRecords.ToListAsync());
        Assert.Empty(await read.AuditEvents.ToListAsync()); Assert.Empty(await read.OutboxMessages.ToListAsync());
    }

    [Fact] public async Task Model_has_no_pending_changes_after_initial_migration()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
