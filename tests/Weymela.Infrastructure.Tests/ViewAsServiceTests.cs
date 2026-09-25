using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class ViewAsServiceTests(PostgresFixture fixture)
{
    private static readonly DateTime Start = new(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Resolution_preserves_real_actor_rejects_cross_admin_reuse_and_ends_immediately()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new ViewAsService(db, new FixedClock(Start));
        var started = await service.StartAsync(AuthorityContext.ForAuthenticatedActor(state.Platform),
            state.Customer.UserId, "support review", "view-start", Guid.NewGuid());

        var authority = await service.ResolveActiveAsync(new RealActor(state.Platform), started.CookieValue);
        Assert.True(authority.IsViewAsActive);
        Assert.Equal(state.Platform, authority.CommandActor);
        Assert.Equal(state.Customer, authority.EffectiveSubject.Identity);

        var crossAdmin = await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.ResolveActiveAsync(new RealActor(state.OtherPlatform), started.CookieValue));
        Assert.Equal(FailureKind.Forbidden, crossAdmin.Kind);

        await service.EndAsync(new RealActor(state.Platform), started.CookieValue, Guid.NewGuid());
        var ended = await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.ResolveActiveAsync(new RealActor(state.Platform), started.CookieValue));
        Assert.Equal("InvalidViewAsSession", ended.Code);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "ViewAsStarted"
            && x.ActorId == state.Platform.UserId && x.TargetUserId == state.Customer.UserId
            && x.SupportSessionId == started.Session.SupportSessionId);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "ViewAsEnded"
            && x.ActorId == state.Platform.UserId && x.SupportSessionId == started.Session.SupportSessionId);
    }

    [Fact]
    public async Task Only_one_active_session_is_allowed_and_same_key_replay_is_idempotent()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new ViewAsService(db, new FixedClock(Start));
        var normal = AuthorityContext.ForAuthenticatedActor(state.Platform);
        var started = await service.StartAsync(normal, state.Customer.UserId, null, "same-key", Guid.NewGuid());
        var active = await service.ResolveActiveAsync(new RealActor(state.Platform), started.CookieValue);
        var replay = await service.StartAsync(active, state.Customer.UserId, null, "same-key", Guid.NewGuid());
        Assert.Equal(started.Session.SupportSessionId, replay.Session.SupportSessionId);

        var conflict = await Assert.ThrowsAsync<ApplicationFailure>(() =>
            service.StartAsync(active, state.OtherTarget.UserId, null, "other-key", Guid.NewGuid()));
        Assert.Equal(FailureKind.ConcurrencyConflict, conflict.Kind);
        Assert.Equal("ViewAsAlreadyActive", conflict.Code);
    }

    [Theory]
    [InlineData(ActorRole.PlatformAdmin)]
    [InlineData(ActorRole.Cashier)]
    public async Task Forbidden_target_roles_are_rejected(ActorRole role)
    {
        var state = await Setup(role);
        await using var db = state.Database.Open();
        var service = new ViewAsService(db, new FixedClock(Start));
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.StartAsync(
            AuthorityContext.ForAuthenticatedActor(state.Platform), state.OtherTarget.UserId,
            null, "forbidden-role-" + role, Guid.NewGuid()));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        Assert.Equal("ViewAsTargetRoleForbidden", failure.Code);
    }

    private async Task<State> Setup(ActorRole forbiddenRole = ActorRole.PlatformAdmin)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var platform = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        var otherPlatform = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        var customer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var otherTarget = new Actor(Guid.NewGuid(), forbiddenRole,
            BusinessId: forbiddenRole == ActorRole.Business ? Guid.NewGuid() : null,
            CustomerId: forbiddenRole == ActorRole.Customer ? Guid.NewGuid() : null,
            CreatorId: forbiddenRole == ActorRole.Creator ? Guid.NewGuid() : null);
        db.CommercePermissions.AddRange(
            new(platform.UserId, ActorRole.PlatformAdmin, platform.UserId, null, true, false),
            new(otherPlatform.UserId, ActorRole.PlatformAdmin, otherPlatform.UserId, null, true, false),
            new(customer.UserId, customer.Role, customer.CustomerId!.Value, null, true, false),
            new(otherTarget.UserId, otherTarget.Role, otherTarget.BusinessId ?? otherTarget.CreatorId ?? otherTarget.CustomerId ?? otherTarget.UserId,
                otherTarget.BusinessId, true, false));
        await db.SaveChangesAsync();
        return new(database, platform, otherPlatform, customer, otherTarget);
    }

    private sealed record State(TestDatabase Database, Actor Platform, Actor OtherPlatform, Actor Customer, Actor OtherTarget);
    private sealed class FixedClock(DateTime value) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(value); }
}
