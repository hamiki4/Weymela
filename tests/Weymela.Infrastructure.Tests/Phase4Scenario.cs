using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Tests;

internal sealed record Phase4Scenario(Scenario Seed, Actor Creator, Actor Customer, Actor Cashier, Guid AllocationId, Guid ParticipationId,
    TestClock Clock, TestViews Provider)
{
    public TestDatabase Database => Seed.Database;
    public static Actor Admin => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), ActorRole.PlatformAdmin);
    public VerifiedViewService Views(WeymelaDbContext db) => new(db, Provider, new CommerceAccessPolicy(db), Clock);
    public CheckoutService Checkout(WeymelaDbContext db) => new(db, new CommerceAccessPolicy(db), Clock);
    public PayoutService Payouts(WeymelaDbContext db) => new(db, Clock);
    public FinancialQueries Queries(WeymelaDbContext db) => new(db, new CommerceAccessPolicy(db), new TestDirectory(), Clock);

    public static async Task<Phase4Scenario> Create(PostgresFixture fixture, PromotionType type = PromotionType.ViewPlusCommission,
        decimal allocation = 2000, decimal budget = 6000, decimal threshold = 5000)
    {
        var seed = await Scenario.Create(fixture, Math.Max(10000, budget), true, budget, type, 3000, threshold, threshold);
        var creatorId = await seed.ApprovedCreator();
        var allocationId = await seed.Assign(creatorId, allocation);
        var creator = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: creatorId);
        var customer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var cashier = new Actor(Guid.NewGuid(), ActorRole.Cashier, seed.Business.BusinessId);
        var clock = new TestClock(); var provider = new TestViews(clock);
        await using var db = seed.Database.Open();
        var p = await db.Promotions.Include(x => x.Allocations).SingleAsync();
        p.Activate(Scenario.Now, Guid.NewGuid());
        foreach (var typeOfDocument in new[] { LegalDocumentType.CreatorAgreement, LegalDocumentType.AntiCircumventionAgreement })
        {
            var document = new LegalDocumentVersion(Guid.NewGuid(), typeOfDocument, "1", "test-hash", Scenario.Now);
            db.LegalDocumentVersions.Add(document);
            db.LegalAcceptances.Add(new(creator.UserId, LegalRole.Creator, document.Id, Scenario.Now, null, null));
        }
        db.CommercePermissions.AddRange(
            new(seed.Business.UserId, ActorRole.Business, seed.Business.BusinessId!.Value, seed.Business.BusinessId, true, true),
            new(creator.UserId, ActorRole.Creator, creatorId, null, true, false),
            new(customer.UserId, ActorRole.Customer, customer.CustomerId!.Value, null, true, false),
            new(cashier.UserId, ActorRole.Cashier, cashier.UserId, cashier.BusinessId, true, true));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var participation = await new VerifiedViewService(db, provider, new CommerceAccessPolicy(db), clock)
            .GoLiveAsync(new(creator, allocationId, "TestProvider", "content-1", "go-live"));
        return new(seed, creator, customer, cashier, allocationId, participation, clock, provider);
    }
    public async Task<ViewRewardResult> Refresh(long campaignViews, string key = "refresh")
    {
        Provider.Count = 1000 + campaignViews;
        await using var db = Database.Open();
        return await Views(db).RefreshAsync(new(Creator, ParticipationId, key));
    }
    public async Task<IssuedOfferQr> Issue(string key = "issue")
    {
        await using var db = Database.Open(); return await Checkout(db).IssueAsync(new(Customer, AllocationId, key));
    }
    public async Task<SaleResult> Redeem(IssuedOfferQr qr, decimal purchase = 1000, string key = "redeem", Actor? scanner = null)
    {
        await using var db = Database.Open(); return await Checkout(db).RedeemAsync(new(scanner ?? Cashier, qr.Token!, new Money(purchase), key));
    }
    public async Task FailOutbox()
    {
        await using var db = Database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION v3.test_outbox_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected outbox failure'; END $$;
            CREATE TRIGGER test_failure BEFORE INSERT ON v3."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION v3.test_outbox_failure();
            """);
    }
}
internal sealed class TestClock : TimeProvider
{
    public DateTime Now { get; set; } = Scenario.Now;
    public override DateTimeOffset GetUtcNow() => new(Now);
}
internal sealed class TestViews(TestClock clock) : IVerifiedViewProvider
{
    public long Count { get; set; } = 1000;
    public Task<VerifiedViewResult> VerifyAsync(VerifiedViewRequest request, CancellationToken ct) =>
        Task.FromResult(new VerifiedViewResult(Count, request.Provider, request.ExternalContentId, clock.Now, "test-evidence"));
}
internal sealed class TestDirectory : IPublicIdentityDirectory
{
    public Task<PublicBusiness> BusinessAsync(Guid id, CancellationToken ct) => Task.FromResult(new PublicBusiness(id, "Abc"));
    public Task<PublicCreator> CreatorAsync(Guid id, CancellationToken ct) => Task.FromResult(new PublicCreator(id, "CR-100", "Bella"));
}
