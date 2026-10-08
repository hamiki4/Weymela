using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Identity;
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

    [Fact] public async Task Account_closure_notifications_reach_remaining_owner_roles_and_platform_admin_once()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid(); var creator = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.CommercePermissions.AddRange(
            new(user, ActorRole.Customer, customer, null, true, false),
            new(user, ActorRole.Creator, creator, null, true, false),
            new(admin, ActorRole.PlatformAdmin, admin, null, true, false));
        await db.SaveChangesAsync();
        var service = new AccountDeletionService(db, new TestClock(), new TestIdentityDeletionProvider());
        await service.CloseOwnRoleAsync(new(user, ActorRole.Creator, CreatorId: creator),
            new("Creator", creator, "DELETE"), "notify-role-close", default);
        for (var i = 0; i < 5 && await Processor(db, new TestClock()).ProcessAsync(default) > 0; i++) { }

        var rows = await db.InAppNotifications.Where(x => x.EventType == "AccountRoleClosed").ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.UserId == user && x.Role == ActorRole.Customer
            && x.Route == "/settings/delete-account");
        Assert.Contains(rows, x => x.UserId == admin && x.Role == ActorRole.PlatformAdmin
            && x.Route == "/admin/accounts");
        Assert.DoesNotContain(rows, x => x.Role == ActorRole.Creator);
    }

    [Fact] public async Task Profile_application_notifications_reach_both_admins_and_applicants_once_with_review_routes()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var platform = Guid.NewGuid(); var operations = Guid.NewGuid();
        db.CommercePermissions.AddRange(
            new CommercePermission(platform, ActorRole.PlatformAdmin, platform, null, true, false),
            new CommercePermission(operations, ActorRole.OperationsAdmin, operations, null, true, false));
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        foreach (var role in new[] { ActorRole.Creator, ActorRole.Business })
        foreach (var approve in new[] { true, false })
        {
            var applicant = Guid.NewGuid(); var customer = Guid.NewGuid();
            db.CommercePermissions.Add(new CommercePermission(applicant, ActorRole.Customer, customer, null, true, false));
            await db.SaveChangesAsync();
            var request = new RoleEnrollmentRequest(role, $"{role} applicant", null, "Addis", "Local", "Submitted details",
                SocialProfiles: role == ActorRole.Creator ? [new("Instagram", "https://www.instagram.com/bella/")] : null);
            var key = $"{role}-{approve}";
            var pending = await service.SubmitAsync(new Actor(applicant, ActorRole.Customer, CustomerId: customer), request, key, default);
            await service.SubmitAsync(new Actor(applicant, ActorRole.Customer, CustomerId: customer), request, key, default);
            await service.ReviewAsync(new Actor(operations, ActorRole.OperationsAdmin), pending.Id, approve,
                approve ? null : "More details needed", pending.Version, key + "-review", default);
        }
        for (var i = 0; i < 10; i++) if (await Processor(db, new TestClock()).ProcessAsync(default) == 0) break;
        var submissions = await db.InAppNotifications.Where(x => x.EventType == "RoleEnrollmentSubmitted").ToListAsync();
        Assert.Equal(8, submissions.Count);
        Assert.All(submissions.Where(x => x.Role == ActorRole.PlatformAdmin && x.Title.Contains("Business", StringComparison.Ordinal)), x => Assert.Equal("/admin/businesses", x.Route));
        Assert.All(submissions.Where(x => x.Role == ActorRole.PlatformAdmin && x.Title.Contains("Creator", StringComparison.Ordinal)), x => Assert.Equal("/admin/creators", x.Route));
        Assert.All(submissions.Where(x => x.Role == ActorRole.OperationsAdmin), x => Assert.Equal("/admin/role-enrollments", x.Route));
        Assert.Equal(4, submissions.Count(x => x.UserId == platform));
        Assert.Equal(4, submissions.Count(x => x.UserId == operations));
        var approved = await db.InAppNotifications.Where(x => x.EventType == "RoleEnrollmentApproved").ToListAsync();
        Assert.Equal(4, approved.Count);
        Assert.Equal(2, approved.Count(x => x.Role == ActorRole.Customer));
        Assert.All(approved, x => Assert.Equal("/onboarding", x.Route));
        var rejected = await db.InAppNotifications.Where(x => x.EventType == "RoleEnrollmentRejected").ToListAsync();
        Assert.Equal(2, rejected.Count);
        Assert.All(rejected, x => { Assert.Equal(ActorRole.Customer, x.Role); Assert.Equal("/onboarding", x.Route);
            Assert.Contains("More details needed", x.Message, StringComparison.Ordinal); });
    }
    [Fact] public async Task Rejected_creator_application_notifies_applicant_in_existing_business_workspace()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var business = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Business, business, business, true, false));
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var pending = await service.SubmitAsync(new Actor(user, ActorRole.Business, business),
            new RoleEnrollmentRequest(ActorRole.Creator, "Owner", null, null, null, null,
                SocialProfiles: [new("YouTube", "https://www.youtube.com/@owner")]), "creator-from-business", default);
        await service.ReviewAsync(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), pending.Id, false,
            "Please add more details", pending.Version, "reject-business-creator", default);
        for (var i = 0; i < 5 && await Processor(db, new TestClock()).ProcessAsync(default) > 0; i++) { }
        var notice = await db.InAppNotifications.SingleAsync(x => x.EventType == "RoleEnrollmentRejected");
        Assert.Equal(user, notice.UserId);
        Assert.Equal(ActorRole.Business, notice.Role);
        Assert.Equal("/onboarding", notice.Route);
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
        var pending = await db.OutboxMessages.Where(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null)
            .Select(x => x.EventType).ToListAsync();
        Assert.False(pending.Count > 0, "Pending notification events: " + string.Join(", ", pending));
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
    private sealed class TestIdentityDeletionProvider : IAccountIdentityDeletionProvider
    {
        public bool Enabled => true;
        public Task DeleteAsync(string projectId, string externalSubject, CancellationToken ct) => Task.CompletedTask;
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
