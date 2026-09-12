using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class NotificationDeliveryTests(PostgresFixture fixture)
{
    private static OutboxProcessor Processor(WeymelaDbContext db, TestClock clock, INotificationPushProvider? push = null, int recipients = 100) =>
        new(db, new RuntimeOptions { WorkerBatchSize = 50, RecipientBatchSize = recipients }, push ?? new DisabledPushProvider(), clock);
    private static async Task Drain(Phase4Scenario s, int recipients = 100)
    {
        await using var db = s.Database.Open(); for (var i = 0; i < 10; i++) if (await Processor(db, s.Clock, recipients: recipients).ProcessAsync(default) == 0) break;
    }
    [Fact] public async Task View_reward_targets_only_earning_creator_and_duplicate_event_does_not_duplicate_notification()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000); await Drain(s);
        await using var db = s.Database.Open(); var row = await db.InAppNotifications.SingleAsync(x => x.EventType == "ViewRewardEarned");
        Assert.Equal(s.Creator.UserId, row.UserId); Assert.Equal(ActorRole.Creator, row.Role); Assert.StartsWith("/creator/", row.Route);
        var source = await db.OutboxMessages.FirstAsync(x => x.EventType == "ViewRewardEarned");
        db.OutboxMessages.Add(new() { EventType = source.EventType, Payload = source.Payload, OccurredAtUtc = source.OccurredAtUtc }); await db.SaveChangesAsync();
        await Drain(s); Assert.Single(await db.InAppNotifications.Where(x => x.EventType == "ViewRewardEarned").ToListAsync());
    }
    [Fact] public async Task Funding_notification_is_business_and_admin_only()
    {
        var s = await Phase4Scenario.Create(fixture); await using var db = s.Database.Open();
        var admin = Phase4Scenario.Admin; db.CommercePermissions.Add(new(admin.UserId, admin.Role, admin.UserId, null, true, false)); await db.SaveChangesAsync();
        await Drain(s); var rows = await db.InAppNotifications.Where(x => x.EventType == "PromotionFunded").ToListAsync();
        Assert.Equal(2, rows.Count); Assert.Contains(rows, x => x.UserId == s.Seed.Business.UserId); Assert.Contains(rows, x => x.UserId == admin.UserId);
    }
    [Fact] public async Task Mark_one_read_is_idempotent_and_cannot_access_other_users_notification()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000); await Drain(s); await using var db = s.Database.Open();
        var n = await db.InAppNotifications.SingleAsync(x => x.EventType == "ViewRewardEarned"); var service = new NotificationService(db, s.Clock);
        Assert.Equal(FailureKind.NotFound, (await Assert.ThrowsAsync<ApplicationFailure>(() => service.ReadAsync(s.Customer, n.Id, default))).Kind);
        await service.ReadAsync(s.Creator, n.Id, default); s.Clock.Now = s.Clock.Now.AddMinutes(1); await service.ReadAsync(s.Creator, n.Id, default);
        db.ChangeTracker.Clear(); Assert.Equal(Scenario.Now, (await db.InAppNotifications.SingleAsync(x => x.Id == n.Id)).ReadAtUtc);
    }
    [Fact] public async Task Read_all_is_scoped_to_user_and_role_and_leaves_future_notifications_unread()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000); await Drain(s); await using var db = s.Database.Open();
        db.InAppNotifications.Add(new() { UserId = s.Creator.UserId, Role = ActorRole.Creator, SourceKey = "future", EventType = "test", Title = "Later", Message = "Later", Route = "/creator", CreatedAtUtc = Scenario.Now.AddMinutes(1) }); await db.SaveChangesAsync();
        await new NotificationService(db, s.Clock).ReadAllAsync(s.Creator, default);
        Assert.Equal(1, (await new NotificationService(db, s.Clock).GetAsync(s.Creator, default)).UnreadCount);
        Assert.True(await db.InAppNotifications.AnyAsync(x => x.UserId == s.Seed.Business.UserId && x.ReadAtUtc == null));
    }
    [Fact] public async Task Concurrent_workers_deliver_one_notification_per_event_recipient()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000);
        await using var one = s.Database.Open(); await using var two = s.Database.Open();
        await Task.WhenAll(Processor(one, s.Clock).ProcessAsync(default), Processor(two, s.Clock).ProcessAsync(default)); await Drain(s);
        Assert.Single(await one.InAppNotifications.Where(x => x.EventType == "ViewRewardEarned").ToListAsync());
    }
    [Fact] public async Task Fanout_is_bounded_and_cursor_continues_without_losing_recipients()
    {
        var s = await Phase4Scenario.Create(fixture); await using var db = s.Database.Open();
        for (var i = 0; i < 5; i++) { var id = Guid.NewGuid(); db.CommercePermissions.Add(new(id, ActorRole.PlatformAdmin, id, null, true, false)); }
        await db.SaveChangesAsync(); await Drain(s, 2);
        Assert.Equal(6, await db.InAppNotifications.CountAsync(x => x.EventType == "PromotionFunded"));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null));
    }
    [Fact] public async Task Malformed_event_retries_with_redacted_error_then_enters_failure_state()
    {
        var s = await Phase4Scenario.Create(fixture); await Drain(s); await using var db = s.Database.Open();
        var bad = new OutboxMessage { EventType = "ViewRewardEarned", Payload = "{\"secret\":\"DO-NOT-LOG\"}", OccurredAtUtc = Scenario.Now };
        db.OutboxMessages.Add(bad); await db.SaveChangesAsync();
        for (var i = 0; i < 5; i++) { await Processor(db, s.Clock).ProcessAsync(default); s.Clock.Now = s.Clock.Now.AddMinutes(10); }
        db.ChangeTracker.Clear(); var saved = await db.OutboxMessages.SingleAsync(x => x.Id == bad.Id);
        Assert.NotNull(saved.FailedAtUtc); Assert.Equal(5, saved.FailureCount); Assert.DoesNotContain("DO-NOT-LOG", saved.LastError!);
    }
    [Fact] public async Task Push_failure_does_not_remove_in_app_notification_or_store_provider_secret()
    {
        var s = await Phase4Scenario.Create(fixture); await Drain(s); await s.Refresh(3000); await using var db = s.Database.Open();
        var processor = Processor(db, s.Clock, new FailedPush()); await processor.ProcessAsync(default);
        for (var i = 0; i < 5; i++) { await processor.PushAsync(default); s.Clock.Now = s.Clock.Now.AddMinutes(10); }
        var row = await db.InAppNotifications.SingleAsync(x => x.EventType == "ViewRewardEarned");
        Assert.Equal(PushDeliveryState.Failed, row.PushState); Assert.Equal(5, row.PushAttempts); Assert.DoesNotContain("provider-secret", row.LastPushErrorCode!);
        Assert.Contains((await new NotificationService(db, s.Clock).GetAsync(s.Creator, default)).Items, x => x.Id == row.Id);
    }
    [Fact] public async Task Worker_records_expiry_without_financial_posting_or_deleting_qr_history()
    {
        var s = await Phase4Scenario.Create(fixture); var qr = await s.Issue(); s.Clock.Now = s.Clock.Now.AddMinutes(6); await using var db = s.Database.Open();
        var journals = await db.FinancialJournals.CountAsync(); var pump = new WorkerPump(db, new RuntimeOptions(), new DisabledPushProvider(), s.Clock);
        await pump.RunOnceAsync(default); await pump.RunOnceAsync(default);
        Assert.Equal(Weymela.Domain.OfferQrStatus.Expired, (await db.OfferQrSessions.SingleAsync()).Status);
        Assert.Equal(journals, await db.FinancialJournals.CountAsync()); Assert.NotNull((await db.WorkerCheckpoints.SingleAsync()).LastSuccessAtUtc);
        await Assert.ThrowsAsync<ApplicationFailure>(() => s.Redeem(qr));
    }
    [Fact] public async Task Notification_content_and_outbox_envelope_cannot_be_rewritten()
    {
        var s = await Phase4Scenario.Create(fixture); await Drain(s); await using var db = s.Database.Open();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE v3.\"InAppNotifications\" SET \"Route\"='https://evil.invalid'"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE v3.\"OutboxMessages\" SET \"Payload\"='{}'::jsonb"));
    }
    [Fact] public async Task Reconciliation_matches_wallet_creator_customer_and_platform_after_views_and_sale()
    {
        var s = await Phase4Scenario.Create(fixture); await s.Refresh(3000); await s.Redeem(await s.Issue()); await using var db = s.Database.Open();
        Assert.Empty(await new ReconciliationService(db).CheckAsync(Phase4Scenario.Admin, default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new ReconciliationService(db).CheckAsync(s.Creator, default));
    }
    private sealed class FailedPush : INotificationPushProvider
    {
        public bool Enabled => true;
        public Task<PushDeliveryResult> SendAsync(PushNotification notification, CancellationToken ct) => Task.FromResult(new PushDeliveryResult(false, true, "provider-secret"));
    }
    [Fact] public async Task Notification_commit_failure_rolls_back_delivery_and_records_retry_without_losing_event()
    {
        var s=await Phase4Scenario.Create(fixture); await Drain(s); await s.Refresh(3000); await using var db=s.Database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION v3.fail_notification() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Injected notification store failure'; END $$;
            CREATE TRIGGER fail_notification BEFORE INSERT ON v3."InAppNotifications" FOR EACH ROW EXECUTE FUNCTION v3.fail_notification();
            """);
        await Processor(db,s.Clock).ProcessAsync(default); db.ChangeTracker.Clear();
        var row=await db.OutboxMessages.SingleAsync(x=>x.EventType=="ViewRewardEarned");
        Assert.Null(row.ProcessedAtUtc); Assert.Equal(1,row.FailureCount); Assert.Equal("NotificationCommitUnavailable",row.LastError);
        Assert.False(await db.InAppNotifications.AnyAsync(x=>x.EventType=="ViewRewardEarned"));
    }
}
