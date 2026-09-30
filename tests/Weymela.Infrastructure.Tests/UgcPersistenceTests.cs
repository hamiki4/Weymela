using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Web;
using Weymela.Infrastructure.Deposits;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class UgcPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Platform_requests_require_owned_verified_profiles_and_parallel_approvals_never_overfill()
    {
        var state = await Setup();
        var second = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: Guid.NewGuid());
        Guid firstProfile;
        Guid secondProfile;
        Guid opportunityId;
        Guid firstRequest;
        Guid secondRequest;
        await using (var db = state.Database.Open())
        {
            firstProfile = await db.CreatorSocialProfiles.Where(x => x.CreatorId == state.Creator.CreatorId).Select(x => x.Id).SingleAsync();
            var profile = new CreatorSocialProfileRecord
            {
                CreatorId = second.CreatorId!.Value, Platform = CreatorPlatform.TikTok,
                ProfileUrl = "https://www.tiktok.com/@second", SelfReportedAudience = 20_000,
                VerificationStatus = "Verified", VerifiedAudience = 20_000,
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            };
            secondProfile = profile.Id;
            db.CreatorSocialProfiles.Add(profile);
            db.CommercePermissions.Add(new CommercePermission(second.UserId, ActorRole.Creator, second.CreatorId.Value, null, true, false));
            db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = second.CreatorId.Value, Role = ActorRole.Creator, DisplayName = "Second Creator", PublicId = "CR-SECOND" });
            await db.SaveChangesAsync();
            var service = new UgcService(db, new FixedTime(Now));
            opportunityId = await service.CreateAsync(state.Business, Input(1) with
            {
                PlatformCapacities = [new("TikTok", 1)]
            }, "platform-create", default);
            await service.PublishAsync(state.Business, opportunityId, 0, "platform-publish", default);
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.RequestAsync(second, opportunityId,
                "wrong-profile", default, "TikTok", firstProfile));
            firstRequest = await service.RequestAsync(state.Creator, opportunityId, "first-request", default, "TikTok", firstProfile);
            secondRequest = await service.RequestAsync(second, opportunityId, "second-request", default, "TikTok", secondProfile);
            db.ChangeTracker.Clear();
            Assert.Equal(0, (await db.UgcPlatformCapacities.SingleAsync(x => x.UgcOpportunityId == opportunityId)).ApprovedCount);
        }

        async Task<bool> Approve(Guid requestId, string key)
        {
            await using var db = state.Database.Open();
            try
            {
                await new UgcService(db, new FixedTime(Now)).ReviewRequestAsync(state.Business, requestId, true, null, key, default);
                return true;
            }
            catch (ApplicationFailure) { return false; }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure) { return false; }
        }
        var results = await Task.WhenAll(Approve(firstRequest, "approve-one"), Approve(secondRequest, "approve-two"));
        Assert.Single(results, x => x);
        await using var verify = state.Database.Open();
        Assert.Equal(1, (await verify.UgcPlatformCapacities.SingleAsync(x => x.UgcOpportunityId == opportunityId)).ApprovedCount);
        Assert.Equal(1, (await verify.UgcOpportunities.SingleAsync(x => x.Id == opportunityId)).ApprovedCreatorCount);
        Assert.Equal(1, await verify.UgcAssignments.CountAsync(x => x.UgcOpportunityId == opportunityId));
        Assert.Equal(1, await verify.UgcCreatorRequests.CountAsync(x => x.UgcOpportunityId == opportunityId && x.Status == UgcRequestStatus.Approved));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new UgcService(verify, new FixedTime(Now))
            .RequestAsync(second, opportunityId, "full-request", default, "TikTok", secondProfile));
    }

    [Fact]
    public async Task Delivery_only_draft_uses_general_capacity_without_a_fake_social_platform()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(2) with
        {
            PlatformRequirements = [], PlatformCapacities = []
        }, "delivery-only-create", default);
        await service.PublishAsync(state.Business, id, 0, "delivery-only-publish", default);
        var card = (await service.DetailAsync(state.Business, id, default)).Opportunity;
        Assert.Empty(card.PlatformRequirements);
        Assert.Empty(card.PlatformCapacities!);
        var requestId = await service.RequestAsync(state.Creator, id, "delivery-only-request", default);
        await service.ReviewRequestAsync(state.Business, requestId, true, null, "delivery-only-approve", default);
        db.ChangeTracker.Clear();
        var request = await db.UgcCreatorRequests.SingleAsync(x => x.Id == requestId);
        Assert.Null(request.SelectedPlatform);
        Assert.Null(request.VerifiedSocialProfileId);
        Assert.Equal(1, (await db.UgcOpportunities.SingleAsync(x => x.Id == id)).ApprovedCreatorCount);
    }

    [Fact]
    public async Task Capacity_migration_preserves_legacy_binding_and_database_constraints()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var legacyId = await service.CreateAsync(state.Business, Input(1), "legacy-create", default);
        await service.PublishAsync(state.Business, legacyId, 0, "legacy-publish", default);
        var legacyRequest = await service.RequestAsync(state.Creator, legacyId, "legacy-request", default);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260929022846_BindUgcSaleAssignments");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var preserved = await db.UgcCreatorRequests.SingleAsync(x => x.Id == legacyRequest);
        Assert.Null(preserved.SelectedPlatform);
        Assert.Null(preserved.VerifiedSocialProfileId);
        Assert.Empty(await db.UgcPlatformCapacities.Where(x => x.UgcOpportunityId == legacyId).ToListAsync());

        var capacityId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO v3."UgcPlatformCapacities" ("Id", "UgcOpportunityId", "Platform", "Capacity", "ApprovedCount") VALUES ({capacityId}, {legacyId}, 'TikTok', {1}, {0})""");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO v3."UgcPlatformCapacities" ("Id", "UgcOpportunityId", "Platform", "Capacity", "ApprovedCount") VALUES ({Guid.NewGuid()}, {legacyId}, 'TikTok', {1}, {0})"""));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var overCapacity = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE v3."UgcPlatformCapacities" SET "ApprovedCount" = {2} WHERE "Id" = {capacityId}"""));
        Assert.Equal("CK_UgcPlatformCapacity_Counts", overCapacity.ConstraintName);
        var parentDelete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM v3."UgcOpportunities" WHERE "Id" = {legacyId}"""));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, parentDelete.SqlState);
        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync("20260929022846_BindUgcSaleAssignments"));
    }

    [Fact]
    public async Task Assignment_migration_preserves_unattributed_history_and_rejects_new_unbound_ugc_qr()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var ugc = new UgcService(db, new FixedTime(Now));
        var opportunityId = await ugc.CreateAsync(state.Business, Input() with
        {
            CustomerOfferEnabled = true, CustomerDiscountPercent = 5m,
            CustomerOfferFundedAllocation = 1000m,
            CustomerOfferStartsAtUtc = Now, CustomerOfferEndsAtUtc = Now.AddDays(7)
        }, "historical-offer-create", default);
        await ugc.PublishAsync(state.Business, opportunityId, 0, "historical-offer-publish", default);
        var offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == opportunityId).Select(x => x.Id).SingleAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928230108_AddCreatorNumbers");
        var historicalId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var digest = new string('A', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."OfferQrSessions" ("Id", "CustomerId", "Source", "UgcCustomerOfferId",
                "BusinessId", "TokenHash", "IssuedAtUtc", "ExpiresAtUtc", "Status", "IdempotencyReference", "Version")
            VALUES ({historicalId}, {customerId}, 'UgcCustomerOffer', {offerId},
                {state.Business.BusinessId!.Value}, {digest}, {Now}, {Now.AddMinutes(5)}, 'Issued', 'historical', {0L})
            """);
        await migrator.MigrateAsync();
        var historical = await db.OfferQrSessions.AsNoTracking().SingleAsync(x => x.Id == historicalId);
        Assert.Null(historical.CreatorId);
        Assert.Null(historical.UgcAssignmentId);
        Assert.Equal(offerId, historical.UgcCustomerOfferId);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."OfferQrSessions" ("Id", "CustomerId", "Source", "UgcCustomerOfferId",
                "BusinessId", "TokenHash", "IssuedAtUtc", "ExpiresAtUtc", "Status", "IdempotencyReference", "Version")
            VALUES ({Guid.NewGuid()}, {customerId}, 'UgcCustomerOffer', {offerId},
                {state.Business.BusinessId!.Value}, {new string('B', 64)}, {Now}, {Now.AddMinutes(5)}, 'Issued', 'new-unbound', {0L})
            """));
        var mixed = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."OfferQrSessions" ("Id", "CustomerId", "Source", "UgcCustomerOfferId",
                "BusinessId", "TokenHash", "IssuedAtUtc", "ExpiresAtUtc", "Status", "IdempotencyReference", "Version")
            VALUES ({Guid.NewGuid()}, {customerId}, 'ViewAndSalePromotion', {offerId},
                {state.Business.BusinessId!.Value}, {new string('C', 64)}, {Now}, {Now.AddMinutes(5)}, 'Issued', 'mixed-source', {0L})
            """));
        Assert.Equal("CK_Qr_SourceBinding", mixed.ConstraintName);
        await migrator.MigrateAsync("20260928230108_AddCreatorNumbers");
        await migrator.MigrateAsync();
        Assert.Null((await db.OfferQrSessions.AsNoTracking().SingleAsync(x => x.Id == historicalId)).UgcAssignmentId);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Later_ugc_pricing_change_affects_new_commitments_without_repricing_live_creator_net()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var original = await service.CreateAsync(state.Business, Input(), "original-pricing-create", default);
        await service.PublishAsync(state.Business, original, 0, "original-pricing-publish", default);
        var configurationId = (await db.FinancialConfigurations.SingleAsync()).Id;
        var versionId = Guid.NewGuid();
        db.FinancialConfigurationVersions.Add(new(versionId, configurationId, 2, Phase4Scenario.Admin.UserId, Now,
            Scenario.Price(PromotionType.ViewOnly, versionId), Scenario.Price(PromotionType.ViewPlusCommission, versionId),
            new Money(3000), new Money(4000),
            new UgcPricingSnapshot(new Money(200), 20m, null, Now, versionId, 3m)));
        await db.SaveChangesAsync();
        var future = await service.CreateAsync(state.Business, Input() with { Title = "Future UGC terms" }, "future-pricing-create", default);
        await service.PublishAsync(state.Business, future, 0, "future-pricing-publish", default);
        db.ChangeTracker.Clear();
        var cards = await service.DiscoverAsync(state.Creator, default);
        Assert.Equal(450m, cards.Single(x => x.Id == original).CreatorPayment);
        Assert.Equal(400m, cards.Single(x => x.Id == future).CreatorPayment);
        Assert.Equal(10m, (await db.UgcOpportunities.SingleAsync(x => x.Id == original)).PricingSnapshot.PlatformFeePercent);
        Assert.Equal(20m, (await db.UgcOpportunities.SingleAsync(x => x.Id == future)).PricingSnapshot.PlatformFeePercent);
    }

    [Fact]
    public async Task Product_arrangement_is_required_to_publish_and_is_visible_without_changing_ugc_accounting()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var incomplete = await service.CreateAsync(state.Business, Input() with { ProductProvided = false, CreatorMustPurchase = false }, "arrangement-incomplete", default);
        var before = (await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId)).AvailableBalance.Amount;
        var missing = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(state.Business, incomplete, 0, "arrangement-missing", default));
        Assert.Equal(FailureKind.Validation, missing.Kind);
        Assert.Equal(before, (await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId)).AvailableBalance.Amount);
        Assert.Empty(await db.UgcReservations.Where(x => x.UgcOpportunityId == incomplete).ToListAsync());
        await service.UpdateAsync(state.Business, incomplete,
            new(null, "Create a 30-60 second vertical video.", ["https://example.com/reference"],
                "Addis Ababa", "90-day digital use", false, ProductProvided: true, CreatorMustPurchase: false),
            0, "arrangement-complete-draft", default);
        Assert.True((await db.UgcOpportunities.SingleAsync(x => x.Id == incomplete)).ProductProvided);
        Assert.Equal(before, (await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId)).AvailableBalance.Amount);

        var conflicting = await service.CreateAsync(state.Business, Input() with { ProductProvided = true, CreatorMustPurchase = true }, "arrangement-conflicting", default);
        var invalid = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(state.Business, conflicting, 0, "arrangement-both", default));
        Assert.Equal(FailureKind.Validation, invalid.Kind);

        var provided = await service.CreateAsync(state.Business, Input(), "arrangement-provided", default);
        var providedCard = (await service.BusinessAsync(state.Business, default)).Single(x => x.Id == provided);
        Assert.True(providedCard.ProductProvided);
        Assert.False(providedCard.CreatorMustPurchase);

        var purchase = await service.CreateAsync(state.Business, Input() with { ProductProvided = false, CreatorMustPurchase = true }, "arrangement-purchase", default);
        var businessDraft = (await service.BusinessAsync(state.Business, default)).Single(x => x.Id == purchase);
        Assert.False(businessDraft.ProductProvided);
        Assert.True(businessDraft.CreatorMustPurchase);
        Assert.Equal(500m, businessDraft.CreatorPayment);
        await service.PublishAsync(state.Business, purchase, 0, "arrangement-publish", default);
        var publishedVersion = (await db.UgcOpportunities.SingleAsync(x => x.Id == purchase)).Version;
        var rewrite = await Assert.ThrowsAsync<ApplicationFailure>(() => service.UpdateAsync(state.Business, purchase,
            new(null, "Create a 30-60 second vertical video.", [], "Addis Ababa", null, false,
                ProductProvided: true, CreatorMustPurchase: false), publishedVersion, "arrangement-rewrite", default));
        Assert.Equal(FailureKind.Validation, rewrite.Kind);
        var creatorCard = (await service.DiscoverAsync(state.Creator, default)).Single(x => x.Id == purchase);
        Assert.True(creatorCard.CreatorMustPurchase);
        Assert.False(creatorCard.ProductProvided);
        Assert.Equal(450m, creatorCard.CreatorPayment);
        var request = await service.RequestAsync(state.Creator, purchase, "arrangement-request", default);
        await service.ReviewRequestAsync(state.Business, request, true, null, "arrangement-approve", default);
        var assignment = (await service.CreatorAssignmentsAsync(state.Creator, default)).Single(x => x.OpportunityId == purchase);
        Assert.True(assignment.CreatorMustPurchase);
        Assert.Equal(450m, assignment.CreatorPayment);
        Assert.Equal(before - 1500m, (await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId)).AvailableBalance.Amount);
        Assert.Single(await db.UgcReservations.Where(x => x.UgcOpportunityId == purchase).ToListAsync());
    }

    [Fact]
    public async Task Business_must_accept_current_agreements_before_creating_or_publishing_ugc()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var draft = await service.CreateAsync(state.Business, Input(), "legal-draft", default);
        var current = await db.LegalDocumentVersions.SingleAsync(x => x.Type == LegalDocumentType.BusinessAgreement);
        db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.BusinessAgreement, "2", "new-hash", Now));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        Assert.Equal(draft, await service.CreateAsync(state.Business, Input(), "legal-draft", default));
        var create = await Assert.ThrowsAsync<ApplicationFailure>(() => service.CreateAsync(state.Business, Input(), "legal-create-rejected", default));
        Assert.Equal(FailureKind.Forbidden, create.Kind);
        var publish = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(state.Business, draft, 0, "legal-publish-rejected", default));
        Assert.Equal(FailureKind.Forbidden, publish.Kind);
        Assert.Equal(UgcOpportunityStatus.Draft, (await db.UgcOpportunities.SingleAsync(x => x.Id == draft)).Status);
        Assert.Empty(await db.UgcReservations.Where(x => x.UgcOpportunityId == draft).ToListAsync());

        var updated = await db.LegalDocumentVersions.SingleAsync(x => x.Type == LegalDocumentType.BusinessAgreement && x.Id != current.Id);
        db.LegalAcceptances.Add(new(state.Business.UserId, LegalRole.Business, updated.Id, Now, null, null));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await service.PublishAsync(state.Business, draft, 0, "legal-publish-accepted", default);
        Assert.Equal(UgcOpportunityStatus.Open, (await db.UgcOpportunities.SingleAsync(x => x.Id == draft)).Status);
        db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.BusinessAgreement, "3", "latest-hash", Now));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(draft, await service.PublishAsync(state.Business, draft, 0, "legal-publish-accepted", default));
    }

    [Fact]
    public async Task Complete_ugc_flow_reserves_1650_and_approval_posts_creator_earning_and_platform_fee_once()
    {
        var state = await Setup();
        await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var opportunityId = await service.CreateAsync(state.Business, Input(), "ugc-create", default);
        await service.PublishAsync(state.Business, opportunityId, 0, "ugc-publish", default);
        var requestId = await service.RequestAsync(state.Creator, opportunityId, "ugc-request", default);
        var assignmentId = await service.ReviewRequestAsync(state.Business, requestId, true, null, "ugc-request-approve", default);
        await service.SubmitAsync(state.Creator, assignmentId, new("https://www.tiktok.com/@mimi/video/123"), "ugc-submit", default);
        await service.ReviewSubmissionAsync(state.Business, assignmentId, "approve", null, "ugc-content-approve", default);
        var replay = await service.ReviewSubmissionAsync(state.Business, assignmentId, "approve", null, "ugc-content-approve", default);
        Assert.Equal(assignmentId, replay);

        db.ChangeTracker.Clear();
        var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == opportunityId);
        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId);
        Assert.Equal(1500m, opportunity.RequiredFunding.Amount); Assert.Equal(500m, opportunity.UsedFunding.Amount);
        Assert.Equal(1000m, opportunity.ReservedFunding.Amount); Assert.Equal(8500m, wallet.AvailableBalance.Amount);
        Assert.Equal(1000m, wallet.ReservedBalance.Amount);
        Assert.Equal(450m, (await db.CreatorEarningsAccounts.SingleAsync(x => x.CreatorId == state.Creator.CreatorId)).AvailableEarnings.Amount);
        Assert.Single(await db.CreatorEarningEntries.Where(x => x.UgcAssignmentId == assignmentId && x.Source == EarningSource.Ugc).ToListAsync());
        var fee = await db.PlatformRevenueEntries.SingleAsync(x => x.UgcAssignmentId == assignmentId && x.Source == PlatformRevenueSource.UgcFee);
        Assert.Equal(50m, fee.Amount.Amount);
        Assert.Equal(2, await db.FinancialJournals.CountAsync(x => EF.Property<Guid?>(x, "UgcOpportunityId") == opportunityId));
        Assert.Single(await db.UgcReservations.Where(x => x.UgcOpportunityId == opportunityId).ToListAsync());
    }

    [Fact]
    public async Task Social_post_off_allows_direct_content_delivery()
    {
        var state = await Setup();
        await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input() with { PlatformRequirements = [] }, "ugc-direct-create", default);
        await service.PublishAsync(state.Business, id, 0, "ugc-direct-publish", default);
        var request = await service.RequestAsync(state.Creator, id, "ugc-direct-request", default);
        var assignment = await service.ReviewRequestAsync(state.Business, request, true, null, "ugc-direct-approve", default);
        await service.SubmitAsync(state.Creator, assignment, new("https://example.com/submission/video"), "ugc-direct-submit", default);
        Assert.Equal(UgcAssignmentStatus.Submitted, (await db.UgcAssignments.SingleAsync(x => x.Id == assignment)).Status);
    }

    [Fact]
    public async Task Social_post_on_requires_one_selected_platform_link()
    {
        var state = await Setup();
        await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "ugc-social-create", default);
        await service.PublishAsync(state.Business, id, 0, "ugc-social-publish", default);
        var request = await service.RequestAsync(state.Creator, id, "ugc-social-request", default);
        var assignment = await service.ReviewRequestAsync(state.Business, request, true, null, "ugc-social-approve", default);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(state.Creator, assignment, new("https://example.com/submission/video"), "ugc-social-submit-invalid", default));
        Assert.Equal(FailureKind.Validation, failure.Kind);
        await service.SubmitAsync(state.Creator, assignment, new("https://www.tiktok.com/@mimi/video/456"), "ugc-social-submit-valid", default);
    }

    [Fact]
    public async Task Creator_ugc_view_hides_business_funding_and_platform_fee()
    {
        var state = await Setup();
        await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "ugc-creator-visibility", default);
        await service.PublishAsync(state.Business, id, 0, "ugc-creator-visibility-publish", default);
        var detail = await service.DetailAsync(state.Creator, id, default);
        Assert.Equal(450m, detail.Opportunity.CreatorPayment);
        Assert.Null(detail.Opportunity.RequiredFunding);
        Assert.Null(detail.Opportunity.PlatformFeePercent);
        Assert.Null(detail.Opportunity.PlatformFee);
        Assert.Null(detail.Opportunity.CustomerDiscountPercent);
    }

    [Fact]
    public async Task Customer_offer_discount_uses_basic_percentage_validation_without_admin_maximum()
    {
        var state = await Setup();
        await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input() with
        {
            CustomerOfferEnabled = true, CustomerDiscountPercent = 80m,
            CustomerOfferFundedAllocation = 1000m, CustomerOfferStartsAtUtc = Now,
            CustomerOfferEndsAtUtc = Now.AddDays(7)
        }, "ugc-discount-basic-validation", default);
        Assert.NotEqual(Guid.Empty, id);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.CreateAsync(state.Business, Input() with
        {
            CustomerOfferEnabled = true, CustomerDiscountPercent = 100.0001m,
            CustomerOfferFundedAllocation = 1000m, CustomerOfferStartsAtUtc = Now,
            CustomerOfferEndsAtUtc = Now.AddDays(7)
        }, "ugc-discount-range", default));
        Assert.Equal(FailureKind.Validation, failure.Kind);
    }

    [Fact]
    public async Task Ugc_customer_offer_sale_uses_separate_fund_discount_and_platform_fee_with_zero_creator_share()
    {
        var state = await Setup();
        var customer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var otherCustomer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var cashier = new Actor(Guid.NewGuid(), ActorRole.Cashier, state.Business.BusinessId);
        IssuedOfferQr qr;
        Guid expectedAssignmentId;
        await using (var db = state.Database.Open())
        {
            db.CommercePermissions.AddRange(
                new CommercePermission(customer.UserId, ActorRole.Customer, customer.CustomerId!.Value, null, true, false),
                new CommercePermission(otherCustomer.UserId, ActorRole.Customer, otherCustomer.CustomerId!.Value, null, true, false),
                new CommercePermission(cashier.UserId, ActorRole.Cashier, cashier.UserId, cashier.BusinessId, true, true));
            await db.SaveChangesAsync();
            var ugc = new UgcService(db, new FixedTime(Now));
            var id = await ugc.CreateAsync(state.Business, Input() with
            {
                CustomerOfferEnabled = true, CustomerDiscountPercent = 5m,
                CustomerOfferFundedAllocation = 5600m, CustomerFacingSlogan = "Save on your next visit",
                CustomerOfferStartsAtUtc = Now, CustomerOfferEndsAtUtc = Now.AddDays(7)
            }, "offer-create", default);
            await ugc.PublishAsync(state.Business, id, 0, "offer-publish", default);
            var request = await ugc.RequestAsync(state.Creator, id, "offer-creator-request", default);
            await ugc.ReviewRequestAsync(state.Business, request, true, null, "offer-creator-approve", default);
            var assignmentId = await db.UgcAssignments.Where(x => x.UgcOpportunityId == id).Select(x => x.Id).SingleAsync();
            expectedAssignmentId = assignmentId;
            var offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == id).Select(x => x.Id).SingleAsync();
            qr = await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                .IssueUgcAsync(new(customer, offerId, assignmentId, "offer-qr"), default);
        }
        SaleResult result;
        await using (var db = state.Database.Open())
            result = await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                .RedeemAsync(new(cashier, qr.Token!, new Money(1000), "offer-sale"), default);
        Assert.Equal("UGC_CUSTOMER_OFFER", result.Source); Assert.Equal(950m, result.CustomerPays!.Value.Amount);
        Assert.Equal(50m, result.CustomerDiscount!.Value.Amount); Assert.Equal(80m, result.TotalBusinessCharge.Amount);

        await using var verify = state.Database.Open();
        var sale = await verify.UgcCustomerOfferSales.SingleAsync();
        Assert.Equal(expectedAssignmentId, sale.UgcAssignmentId);
        Assert.Equal(expectedAssignmentId, (await verify.OfferQrSessions.SingleAsync()).UgcAssignmentId);
        Assert.Equal(50m, sale.CustomerDiscountAmount.Amount); Assert.Equal(30m, sale.PlatformRevenueAmount.Amount);
        Assert.Empty(await verify.CreatorEarningEntries.ToListAsync());
        Assert.Empty(await verify.CustomerCashbackEntries.ToListAsync());
        Assert.Single(await verify.PlatformRevenueEntries.Where(x => x.Source == PlatformRevenueSource.UgcCustomerOfferSaleFee).ToListAsync());
        Assert.Equal(5520m, (await verify.UgcCustomerOffers.SingleAsync()).RemainingFunding.Amount);
        Assert.Equal(7020m, (await verify.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        Assert.Equal(OfferQrStatus.Used, (await verify.OfferQrSessions.SingleAsync()).Status);
        var workspace = new WorkspaceQueries(verify, new PersistentWorkspaceDirectory(verify), new FixedTime(Now));
        var businessTransaction = Assert.Single(await workspace.RecentSalesAsync(state.Business, default));
        Assert.Equal("UGC_CUSTOMER_OFFER", businessTransaction.Source);
        Assert.Equal("Morning Hair Transformation", businessTransaction.Offer);
        Assert.Equal("Mimi Creator", businessTransaction.Creator);
        Assert.Equal(80m, businessTransaction.BusinessCharge);
        Assert.Equal("Completed", businessTransaction.Status);
        var day = DateOnly.FromDateTime(Now);
        var adminPurchase = Assert.Single((await workspace.AdminReportAsync(Phase4Scenario.Admin, day, day, default)).Purchases);
        Assert.Equal("UGC_PLUS_SALE", adminPurchase.SourceType);
        Assert.Equal(sale.UgcOpportunityId, adminPurchase.SourceId);
        Assert.Equal(expectedAssignmentId, adminPurchase.UgcAssignmentId);
        Assert.Equal(0m, adminPurchase.CreatorSaleEarning);
        Assert.Equal(50m, adminPurchase.CustomerBenefit);
        Assert.Equal(30m, adminPurchase.PlatformShare);
        Assert.Single(await workspace.RecentSalesAsync(cashier, default));
        var unrelatedCashier = new Actor(Guid.NewGuid(), ActorRole.Cashier, state.Business.BusinessId);
        verify.CommercePermissions.Add(new CommercePermission(unrelatedCashier.UserId, ActorRole.Cashier,
            unrelatedCashier.UserId, unrelatedCashier.BusinessId, true, true));
        await verify.SaveChangesAsync();
        Assert.Empty(await workspace.RecentSalesAsync(unrelatedCashier, default));
        Assert.DoesNotContain("PlatformFee", JsonSerializer.Serialize(businessTransaction));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new CheckoutService(verify,
            new CommerceAccessPolicy(verify), new FixedTime(Now))
            .RedeemAsync(new(cashier, qr.Token!, new Money(1000), "offer-sale-second"), default));
        Assert.Single(await verify.UgcCustomerOfferSales.ToListAsync());
        var queries = new FinancialQueries(verify, new CommerceAccessPolicy(verify),
            new PersistentWorkspaceDirectory(verify), new FixedTime(Now));
        var transaction = Assert.Single(await queries.CustomerTransactionsAsync(customer, default));
        Assert.Equal("UGC_CUSTOMER_OFFER", transaction.Source);
        Assert.Equal("Save on your next visit", transaction.Offer);
        Assert.Equal("Bella Beauty", transaction.Business);
        Assert.Null(transaction.Creator);
        Assert.Equal(1000m, transaction.PurchaseAmount.Amount);
        Assert.Equal(950m, transaction.CustomerPaidAmount!.Value.Amount);
        Assert.Equal(50m, transaction.DiscountReceived!.Value.Amount);
        Assert.Null(transaction.CashbackEarned);
        Assert.Empty(await queries.CustomerTransactionsAsync(otherCustomer, default));
        var json = JsonSerializer.Serialize(transaction);
        foreach (var internalField in new[] { "CreatorId", "BusinessId", "CreatorAllocationId", "Commission", "Platform", "JournalId", "CorrelationId", "IdempotencyKey" })
            Assert.DoesNotContain(internalField, json);
        await DrainNotifications(state.Database);
        Assert.Single(await verify.InAppNotifications.Where(x => x.EventType == "SaleCompleted" && x.UserId == customer.UserId).ToListAsync());
        Assert.Empty(await verify.InAppNotifications.Where(x => x.EventType == "CreatorCommissionEarned").ToListAsync());
    }

    [Fact]
    public async Task Manual_ugc_checkout_keeps_exact_assignment_and_never_pays_a_sale_commission()
    {
        var state = await Setup();
        var customer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var cashier = new Actor(Guid.NewGuid(), ActorRole.Cashier, state.Business.BusinessId);
        Guid assignmentId;
        Guid offerId;
        string creatorNumber;
        await using (var db = state.Database.Open())
        {
            db.CommercePermissions.AddRange(
                new CommercePermission(customer.UserId, ActorRole.Customer, customer.CustomerId!.Value, null, true, false),
                new CommercePermission(cashier.UserId, ActorRole.Cashier, cashier.UserId, cashier.BusinessId, true, true));
            db.CustomerProfiles.Add(new CustomerProfileRecord
            {
                CustomerId = customer.CustomerId.Value, UserId = customer.UserId,
                PreferredName = "Customer", CreatedAtUtc = Now, UpdatedAtUtc = Now
            });
            db.AuthIdentifiers.Add(new AuthIdentifierRecord
            {
                UserId = customer.UserId, Kind = "Phone", IdentifierHash = EmailAuthService.HashIdentifier("+251900000000"),
                DeliveryAddress = "+251900000000",
                IsVerified = true, CreatedAtUtc = Now
            });
            await db.SaveChangesAsync();
            var ugc = new UgcService(db, new FixedTime(Now));
            var opportunityId = await ugc.CreateAsync(state.Business, Input() with
            {
                CustomerOfferEnabled = true, CustomerDiscountPercent = 5m,
                CustomerOfferFundedAllocation = 5600m,
                CustomerOfferStartsAtUtc = Now, CustomerOfferEndsAtUtc = Now.AddDays(7)
            }, "manual-ugc-create", default);
            await ugc.PublishAsync(state.Business, opportunityId, 0, "manual-ugc-publish", default);
            var requestId = await ugc.RequestAsync(state.Creator, opportunityId, "manual-ugc-request", default);
            assignmentId = await ugc.ReviewRequestAsync(state.Business, requestId, true, null, "manual-ugc-approve", default);
            offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == opportunityId).Select(x => x.Id).SingleAsync();
            creatorNumber = (await db.PublicWorkspaceProfiles.SingleAsync(x => x.SubjectId == state.Creator.CreatorId)).CreatorNumber!.Value.ToString();
        }
        await using (var db = state.Database.Open())
        {
            var checkout = new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now));
            var lookup = await checkout.ResolveManualAsync(cashier, new(creatorNumber, "0900000000"), default);
            Assert.Equal(offerId, Assert.Single(lookup.Offers).Id);
            var input = new ManualCheckoutConfirmInput(creatorNumber, "0900000000", offerId, 1000m);
            var result = await checkout.ConfirmManualAsync(cashier, input, "manual-ugc-sale", default);
            Assert.Equal("UGC_CUSTOMER_OFFER", result.Source);
            Assert.Equal(result, await checkout.ConfirmManualAsync(cashier, input, "manual-ugc-sale", default));
        }
        await using var verify = state.Database.Open();
        Assert.Equal(assignmentId, (await verify.UgcCustomerOfferSales.SingleAsync()).UgcAssignmentId);
        var businessHistory = await new WorkspaceQueries(verify, new PersistentWorkspaceDirectory(verify),
            new FixedTime(Now)).RecentSalesAsync(state.Business, default);
        Assert.Equal("••••0000", Assert.Single(businessHistory).CustomerMasked);
        Assert.Single(await verify.FinancialJournals.Where(x => x.SourceType == JournalSourceType.UgcCustomerOfferSale).ToListAsync());
        Assert.Empty(await verify.CreatorEarningEntries.ToListAsync());
    }

    [Fact]
    public async Task Two_ugc_purchases_competing_for_the_final_discount_fund_post_only_once()
    {
        var state = await Setup();
        var firstCustomer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var secondCustomer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var cashier = new Actor(Guid.NewGuid(), ActorRole.Cashier, state.Business.BusinessId);
        IssuedOfferQr firstQr;
        IssuedOfferQr secondQr;
        await using (var db = state.Database.Open())
        {
            db.CommercePermissions.AddRange(
                new CommercePermission(firstCustomer.UserId, ActorRole.Customer, firstCustomer.CustomerId!.Value, null, true, false),
                new CommercePermission(secondCustomer.UserId, ActorRole.Customer, secondCustomer.CustomerId!.Value, null, true, false),
                new CommercePermission(cashier.UserId, ActorRole.Cashier, cashier.UserId, cashier.BusinessId, true, true));
            await db.SaveChangesAsync();
            var ugc = new UgcService(db, new FixedTime(Now));
            var opportunityId = await ugc.CreateAsync(state.Business, Input() with
            {
                CustomerOfferEnabled = true, CustomerDiscountPercent = 5m,
                CustomerOfferFundedAllocation = 80m,
                CustomerOfferStartsAtUtc = Now, CustomerOfferEndsAtUtc = Now.AddDays(7)
            }, "final-fund-create", default);
            await ugc.PublishAsync(state.Business, opportunityId, 0, "final-fund-publish", default);
            var requestId = await ugc.RequestAsync(state.Creator, opportunityId, "final-fund-request", default);
            var assignmentId = await ugc.ReviewRequestAsync(state.Business, requestId, true, null, "final-fund-approve", default);
            var offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == opportunityId).Select(x => x.Id).SingleAsync();
            var checkout = new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now));
            firstQr = await checkout.IssueUgcAsync(new(firstCustomer, offerId, assignmentId, "first-final-qr"), default);
            secondQr = await checkout.IssueUgcAsync(new(secondCustomer, offerId, assignmentId, "second-final-qr"), default);
        }
        async Task<(SaleResult? Sale, Exception? Error)> Redeem(IssuedOfferQr qr, string key)
        {
            await using var db = state.Database.Open();
            try
            {
                return (await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                    .RedeemAsync(new(cashier, qr.Token!, new Money(1000), key), default), null);
            }
            catch (Exception ex) { return (null, ex); }
        }
        var results = await Task.WhenAll(Redeem(firstQr, "first-final-sale"), Redeem(secondQr, "second-final-sale"));
        Assert.Single(results, x => x.Sale is not null);
        Assert.Single(results, x => x.Error is not null);
        await using var verify = state.Database.Open();
        Assert.Single(await verify.UgcCustomerOfferSales.ToListAsync());
        Assert.Single(await verify.FinancialJournals.Where(x => x.SourceType == JournalSourceType.UgcCustomerOfferSale).ToListAsync());
        Assert.Equal(0m, (await verify.UgcCustomerOffers.SingleAsync()).RemainingFunding.Amount);
        Assert.Empty(await verify.CreatorEarningEntries.ToListAsync());
    }

    [Fact]
    public async Task Insufficient_ugc_customer_offer_fund_rejects_before_qr_or_financial_mutation()
    {
        var state = await Setup();
        var customer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        var cashier = new Actor(Guid.NewGuid(), ActorRole.Cashier, state.Business.BusinessId);
        IssuedOfferQr qr;
        await using (var db = state.Database.Open())
        {
            db.CommercePermissions.AddRange(
                new CommercePermission(customer.UserId, ActorRole.Customer, customer.CustomerId!.Value, null, true, false),
                new CommercePermission(cashier.UserId, ActorRole.Cashier, cashier.UserId, cashier.BusinessId, true, true));
            await db.SaveChangesAsync();
            var ugc = new UgcService(db, new FixedTime(Now));
            var id = await ugc.CreateAsync(state.Business, Input() with
            {
                CustomerOfferEnabled = true, CustomerDiscountPercent = 5m,
                CustomerOfferFundedAllocation = 79m, CustomerOfferStartsAtUtc = Now,
                CustomerOfferEndsAtUtc = Now.AddDays(7)
            }, "small-offer-create", default);
            await ugc.PublishAsync(state.Business, id, 0, "small-offer-publish", default);
            var request = await ugc.RequestAsync(state.Creator, id, "small-offer-creator-request", default);
            await ugc.ReviewRequestAsync(state.Business, request, true, null, "small-offer-creator-approve", default);
            var assignmentId = await db.UgcAssignments.Where(x => x.UgcOpportunityId == id).Select(x => x.Id).SingleAsync();
            var offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == id).Select(x => x.Id).SingleAsync();
            qr = await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                .IssueUgcAsync(new(customer, offerId, assignmentId, "small-offer-qr"), default);
        }
        await using (var db = state.Database.Open())
        {
            var failure = await Assert.ThrowsAsync<ApplicationFailure>(() =>
                new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                    .RedeemAsync(new(cashier, qr.Token!, new Money(1000), "small-offer-sale"), default));
            Assert.Equal(FailureKind.InsufficientFunds, failure.Kind); Assert.Equal("Offer no longer available.", failure.Message);
        }
        await using var verify = state.Database.Open();
        Assert.Empty(await verify.UgcCustomerOfferSales.ToListAsync());
        Assert.Equal(OfferQrStatus.Issued, (await verify.OfferQrSessions.SingleAsync()).Status);
        var offer = await verify.UgcCustomerOffers.SingleAsync();
        Assert.Equal(0m, offer.UsedFunding.Amount); Assert.Equal(79m, offer.ReservedFunding.Amount);
        Assert.Empty(await verify.FinancialJournals.Where(x => x.SourceType == JournalSourceType.UgcCustomerOfferSale).ToListAsync());
    }

    [Fact]
    public async Task Customer_discovery_returns_only_the_safe_customer_offer_and_never_the_ugc_job()
    {
        var state = await Setup();
        var customer = new Actor(Guid.NewGuid(), ActorRole.Customer, CustomerId: Guid.NewGuid());
        await using var db = state.Database.Open();
        db.CommercePermissions.Add(new CommercePermission(customer.UserId, ActorRole.Customer,
            customer.CustomerId!.Value, null, true, false));
        await db.SaveChangesAsync();
        var ugc = new UgcService(db, new FixedTime(Now));
        var hidden = await ugc.CreateAsync(state.Business, Input() with { Title = "Internal hidden brief" }, "hidden-create", default);
        await ugc.PublishAsync(state.Business, hidden, 0, "hidden-publish", default);
        var visible = await ugc.CreateAsync(state.Business, Input() with
        {
            Title = "Internal customer must never see this title",
            CustomerOfferEnabled = true,
            CustomerDiscountPercent = 5m,
            CustomerOfferFundedAllocation = 5600m,
            CustomerFacingSlogan = "Save on your next visit",
            CustomerOfferStartsAtUtc = Now,
            CustomerOfferEndsAtUtc = Now.AddDays(7)
        }, "visible-create", default);
        await ugc.PublishAsync(state.Business, visible, 0, "visible-publish", default);
        var request = await ugc.RequestAsync(state.Creator, visible, "visible-creator-request", default);
        var assignmentId = await ugc.ReviewRequestAsync(state.Business, request, true, null, "visible-creator-approve", default);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE v3."PublicWorkspaceProfiles" SET "Latitude"={9.01m}, "Longitude"={38.72m}
            WHERE "SubjectId"={state.Business.BusinessId} AND "Role"='Business'
            """);

        var cards = await new WorkspaceQueries(db, new PersistentWorkspaceDirectory(db), new FixedTime(Now))
            .OffersAsync(customer, default);

        var card = Assert.Single(cards);
        Assert.Equal("UGC_CUSTOMER_OFFER", card.Source);
        Assert.Equal(assignmentId, card.UgcAssignmentId);
        Assert.Equal("Save on your next visit", card.Offer);
        Assert.Equal("Bella Beauty", card.Business.DisplayName);
        Assert.Equal(5m, card.BenefitPercent);
        Assert.NotNull(card.Creator);
        Assert.Null(card.WatchUrl);
        Assert.Equal(9.01m, card.Business.Latitude);
        Assert.Equal(38.72m, card.Business.Longitude);
        Assert.DoesNotContain("Internal", card.Offer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task Insufficient_available_balance_rolls_back_publish_reservation_and_journal(decimal deposit)
    {
        var state = await Setup(deposit); await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "create-insufficient", default);
        Assert.DoesNotContain(await service.DiscoverAsync(state.Creator, default), row => row.Id == id);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(state.Business, id, 0, "publish-insufficient", default));
        Assert.Equal(FailureKind.InsufficientFunds, failure.Kind);
        Assert.DoesNotContain(await service.DiscoverAsync(state.Creator, default), row => row.Id == id);
        Assert.Equal(FailureKind.Validation, (await Assert.ThrowsAsync<ApplicationFailure>(() => service.RequestAsync(state.Creator, id, "request-unfunded", default))).Kind);
        db.ChangeTracker.Clear();
        Assert.Equal(UgcOpportunityStatus.Draft, (await db.UgcOpportunities.SingleAsync(x => x.Id == id)).Status);
        Assert.Empty(await db.UgcReservations.ToListAsync());
        Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.Key == "publish-insufficient"));
    }

    [Fact]
    public async Task Published_ugc_from_an_inactive_Business_is_absent_from_Creator_Discover()
    {
        var state = await Setup(); await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "inactive-business-create", default);
        await service.PublishAsync(state.Business, id, 0, "inactive-business-publish", default);
        Assert.Contains(await service.DiscoverAsync(state.Creator, default), row => row.Id == id);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == state.Business.UserId && x.Role == ActorRole.Business);
        permission.IsActive = false;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.DoesNotContain(await service.DiscoverAsync(state.Creator, default), row => row.Id == id);
        Assert.Equal(FailureKind.Validation, (await Assert.ThrowsAsync<ApplicationFailure>(() => service.RequestAsync(state.Creator, id, "inactive-business-request", default))).Kind);
    }

    [Fact]
    public async Task Another_business_and_another_creator_cannot_access_owned_ugc_state()
    {
        var state = await Setup(); await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "owned-create", default);
        var otherBusiness = new Actor(Guid.NewGuid(), ActorRole.Business, Guid.NewGuid());
        db.BusinessWallets.Add(new(otherBusiness.BusinessId!.Value));
        db.CommercePermissions.Add(new(otherBusiness.UserId, ActorRole.Business, otherBusiness.BusinessId.Value, otherBusiness.BusinessId, true, false));
        db.PublicWorkspaceProfiles.Add(new() { SubjectId = otherBusiness.BusinessId.Value, Role = ActorRole.Business, DisplayName = "Other Business", PublicId = "BU-OTHER" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var businessFailure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(otherBusiness, id, 0, "cross-business", default));
        Assert.Equal(FailureKind.Forbidden, businessFailure.Kind);
        var otherCreator = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: Guid.NewGuid());
        db.CommercePermissions.Add(new(otherCreator.UserId, ActorRole.Creator, otherCreator.CreatorId!.Value, null, true, false));
        db.PublicWorkspaceProfiles.Add(new() { SubjectId = otherCreator.CreatorId.Value, Role = ActorRole.Creator, DisplayName = "Other Creator", PublicId = "CR-OTHER" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var creatorFailure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.DetailAsync(otherCreator, id, default));
        Assert.Equal(FailureKind.Forbidden, creatorFailure.Kind);
    }

    [Fact]
    public async Task Optional_platform_minimum_is_enforced_but_absent_minimum_does_not_block_creator()
    {
        var state = await Setup(); await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var restricted = await service.CreateAsync(state.Business, Input(minimumAudience: 30_000), "ugc-restricted-create", default);
        await service.PublishAsync(state.Business, restricted, 0, "ugc-restricted-publish", default);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.RequestAsync(state.Creator, restricted, "ugc-restricted-request", default));
        Assert.Equal(FailureKind.Validation, failure.Kind);
        var open = await service.CreateAsync(state.Business, Input(minimumAudience: null), "ugc-open-create", default);
        await service.PublishAsync(state.Business, open, 0, "ugc-open-publish", default);
        Assert.NotEqual(Guid.Empty, await service.RequestAsync(state.Creator, open, "ugc-open-request", default));
    }

    [Fact]
    public async Task Concurrent_creator_approvals_never_exceed_ugc_capacity()
    {
        var state = await Setup(); Guid firstRequest; Guid secondRequest; var second = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: Guid.NewGuid());
        await using (var db = state.Database.Open())
        {
            db.CommercePermissions.Add(new(second.UserId, ActorRole.Creator, second.CreatorId!.Value, null, true, false));
            db.PublicWorkspaceProfiles.Add(new() { SubjectId = second.CreatorId.Value, Role = ActorRole.Creator, DisplayName = "Second Creator", PublicId = "CR-SECOND" });
            db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord { CreatorId = second.CreatorId.Value, Platform = CreatorPlatform.TikTok,
                ProfileUrl = "https://www.tiktok.com/@second", SelfReportedAudience = 25_000,
                VerificationStatus = "Verified", VerifiedAudience = 25_000, CreatedAtUtc = Now, UpdatedAtUtc = Now });
            await db.SaveChangesAsync();
            var service = new UgcService(db, new FixedTime(Now));
            var id = await service.CreateAsync(state.Business, Input(creators: 1), "ugc-capacity-create", default);
            await service.PublishAsync(state.Business, id, 0, "ugc-capacity-publish", default);
            firstRequest = await service.RequestAsync(state.Creator, id, "ugc-capacity-request-one", default);
            secondRequest = await service.RequestAsync(second, id, "ugc-capacity-request-two", default);
        }
        async Task<bool> Approve(Guid request, string key)
        {
            try { await using var db = state.Database.Open(); await new UgcService(db, new FixedTime(Now)).ReviewRequestAsync(state.Business, request, true, null, key, default); return true; }
            catch { return false; }
        }
        var results = await Task.WhenAll(Approve(firstRequest, "ugc-capacity-approve-one"), Approve(secondRequest, "ugc-capacity-approve-two"));
        Assert.Single(results, x => x);
        await using var verify = state.Database.Open();
        Assert.Single(await verify.UgcAssignments.ToListAsync());
        Assert.Equal(1, (await verify.UgcOpportunities.SingleAsync()).ApprovedCreatorCount);
    }

    [Fact]
    public async Task Instruction_change_is_material_and_creator_must_accept_before_submission()
    {
        var state = await Setup(); await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "ugc-revision-create", default);
        await service.PublishAsync(state.Business, id, 0, "ugc-revision-publish", default);
        var request = await service.RequestAsync(state.Creator, id, "ugc-revision-request", default);
        var assignment = await service.ReviewRequestAsync(state.Business, request, true, null, "ugc-revision-approve", default);
        db.ChangeTracker.Clear(); var version = (await db.UgcOpportunities.AsNoTracking().SingleAsync(x => x.Id == id)).Version;
        await service.UpdateAsync(state.Business, id, new(null, "Create two vertical clips.", ["https://example.com/new-reference"],
            "Addis Ababa", "90-day digital use", false), version, "ugc-material-update", default);
        db.ChangeTracker.Clear();
        Assert.True((await db.UgcAssignments.AsNoTracking().SingleAsync(x => x.Id == assignment)).RevisionAcceptanceRequired);
        await Assert.ThrowsAnyAsync<Exception>(() => service.SubmitAsync(state.Creator, assignment, new("https://www.tiktok.com/@mimi/video/before-accept"), "ugc-submit-before-accept", default));
        var current = await db.UgcOpportunities.AsNoTracking().SingleAsync(x => x.Id == id);
        await service.AcceptRevisionAsync(state.Creator, assignment, current.CurrentRevision, "ugc-accept-revision", default);
        Assert.NotEqual(Guid.Empty, await service.SubmitAsync(state.Creator, assignment, new("https://www.tiktok.com/@mimi/video/after-accept"), "ugc-submit-after-accept", default));
    }

    [Fact]
    public async Task Cancellation_release_is_exact_and_idempotent()
    {
        var state = await Setup(); await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "ugc-cancel-create", default);
        await service.PublishAsync(state.Business, id, 0, "ugc-cancel-publish", default);
        await service.CancelAsync(state.Business, id, 1, "ugc-cancel", default);
        Assert.Equal(id, await service.CancelAsync(state.Business, id, 1, "ugc-cancel", default));
        db.ChangeTracker.Clear(); var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId);
        Assert.Equal(10_000m, wallet.AvailableBalance.Amount); Assert.Equal(0m, wallet.ReservedBalance.Amount);
        Assert.Single(await db.UgcBudgetEntries.Where(x => x.UgcOpportunityId == id && x.Movement == "Released").ToListAsync());
    }

    [Fact]
    public async Task Draft_terms_can_be_repriced_before_publish_and_revision_history_preserves_both_versions()
    {
        var state = await Setup(); await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "ugc-edit-create", default);
        await service.UpdateAsync(state.Business, id, new("New headline", "Create three vertical clips.", ["https://example.com/new-reference"],
            "Adama", "180-day digital use", false, "Updated transformation", "Photos", Now.AddDays(21), false, true,
            600, 2, [new("Instagram", "Instagram/Reels", 5_000)],
            PlatformCapacities: [new("Instagram", 2)]), 0, "ugc-edit-draft", default);
        db.ChangeTracker.Clear();
        var opportunity = await db.UgcOpportunities.Include(x => x.PlatformRequirements).SingleAsync(x => x.Id == id);
        Assert.Equal("Updated transformation", opportunity.Title); Assert.Equal(UgcContentType.Photos, opportunity.ContentType);
        Assert.Equal(540m, opportunity.CreatorPayment.Amount); Assert.Equal(2, opportunity.CreatorCapacity);
        Assert.Equal(1200m, opportunity.RequiredFunding.Amount);
        Assert.Equal("Instagram", (await service.DetailAsync(state.Business, id, default)).Opportunity.PlatformRequirements.Single().Platform);
        Assert.Equal(2, await db.UgcRevisions.CountAsync(x => x.UgcOpportunityId == id));
        await service.PublishAsync(state.Business, id, opportunity.Version, "ugc-edit-publish", default);
        db.ChangeTracker.Clear();
        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId);
        Assert.Equal(8800m, wallet.AvailableBalance.Amount); Assert.Equal(1200m, wallet.ReservedBalance.Amount);
    }

    [Fact]
    public async Task Published_ugc_rejects_financial_rewrite_and_keeps_original_creator_deal()
    {
        var state = await Setup(); await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "ugc-locked-create", default);
        await service.PublishAsync(state.Business, id, 0, "ugc-locked-publish", default);
        var current = await db.UgcOpportunities.AsNoTracking().SingleAsync(x => x.Id == id);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.UpdateAsync(state.Business, id,
            new(null, current.Instructions, [], current.Location, current.UsageRights, true, CreatorPayment: 200),
            current.Version, "ugc-locked-update", default));
        Assert.Equal(FailureKind.Validation, failure.Kind);
        db.ChangeTracker.Clear(); current = await db.UgcOpportunities.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(450m, current.CreatorPayment.Amount); Assert.Equal(1500m, current.RequiredFunding.Amount);
    }

    [Fact]
    public async Task Ugc_notifications_route_to_correct_recipient_once()
    {
        var state = await Setup(); await using (var db = state.Database.Open())
        {
            var service = new UgcService(db, new FixedTime(Now));
            var id = await service.CreateAsync(state.Business, Input(), "ugc-notify-create", default);
            await service.PublishAsync(state.Business, id, 0, "ugc-notify-publish", default);
            var request = await service.RequestAsync(state.Creator, id, "ugc-notify-request", default);
            var assignment = await service.ReviewRequestAsync(state.Business, request, true, null, "ugc-notify-approve", default);
            await service.SubmitAsync(state.Creator, assignment, new("https://www.tiktok.com/@mimi/video/notify-content"), "ugc-notify-submit", default);
            await service.ReviewSubmissionAsync(state.Business, assignment, "changes", "Use a clearer opening.", "ugc-notify-changes", default);
            await service.SubmitAsync(state.Creator, assignment, new("https://www.tiktok.com/@mimi/video/notify-content-v2"), "ugc-notify-resubmit", default);
            await service.ReviewSubmissionAsync(state.Business, assignment, "approve", null, "ugc-notify-content-approve", default);
        }
        await DrainNotifications(state.Database);
        await using var verify = state.Database.Open();
        foreach (var eventType in new[] { "UgcRequestReceived", "UgcRequestApproved", "UgcChangesRequested", "UgcContentApproved" })
            Assert.Single(await verify.InAppNotifications.Where(x => x.EventType == eventType).ToListAsync());
        Assert.Equal(2, await verify.InAppNotifications.CountAsync(x => x.EventType == "UgcContentSubmitted"));
        Assert.All(await verify.InAppNotifications.Where(x => x.EventType == "UgcRequestReceived" || x.EventType == "UgcContentSubmitted").ToListAsync(), x => Assert.Equal(state.Business.UserId, x.UserId));
        Assert.All(await verify.InAppNotifications.Where(x => x.EventType == "UgcRequestApproved" || x.EventType == "UgcChangesRequested" || x.EventType == "UgcContentApproved").ToListAsync(), x => Assert.Equal(state.Creator.UserId, x.UserId));
    }

    [Fact]
    public async Task Approved_receipt_deposit_supplies_authoritative_ugc_publish_funding()
    {
        var state = await Setup(0);
        var admin = Phase4Scenario.Admin;
        Guid depositId; Guid opportunityId;
        await using (var db = state.Database.Open())
        {
            db.CommercePermissions.Add(new(admin.UserId, admin.Role, admin.UserId, null, true, false));
            await db.SaveChangesAsync();
            var pending = await new DepositService(db, new ManualApprovalDepositProvider(), new FixedTime(Now))
                .SubmitAsync(state.Business, new(10000m, "UGC-BANK-RECEIPT", "r_opaque-test-proof"), "ugc-receipt-submit", default);
            depositId = pending.Id;
            Assert.Equal("Pending", pending.Status);
            Assert.Equal(0m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
            var service = new UgcService(db, new FixedTime(Now));
            opportunityId = await service.CreateAsync(state.Business, Input(), "ugc-after-receipt-create", default);
            var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(state.Business, opportunityId, 0, "ugc-before-review", default));
            Assert.Equal(FailureKind.InsufficientFunds, failure.Kind);
        }
        await using (var db = state.Database.Open())
            await new FinancialCommands(db, new FixedTime(Now)).ReviewDepositAsync(admin, depositId,
                new(true, 0, "UGC-BANK-CONFIRMED"), "ugc-receipt-review");
        await using (var db = state.Database.Open())
        {
            Assert.Equal(10000m, (await db.BusinessWallets.SingleAsync()).AvailableBalance.Amount);
            await new UgcService(db, new FixedTime(Now)).PublishAsync(state.Business, opportunityId, 0, "ugc-after-review-publish", default);
            db.ChangeTracker.Clear();
            var wallet = await db.BusinessWallets.SingleAsync();
            Assert.Equal(8500m, wallet.AvailableBalance.Amount);
            Assert.Equal(1500m, wallet.ReservedBalance.Amount);
            Assert.Single(await db.UgcReservations.Where(x => x.UgcOpportunityId == opportunityId).ToListAsync());
            Assert.Empty(await new ReconciliationService(db).CheckAsync(admin, default));
        }
    }

    [Fact]
    public async Task Creator_entered_link_cannot_satisfy_verified_ugc_platform_requirement()
    {
        var state = await Setup();
        await using var db = state.Database.Open();
        var ugc = new UgcService(db, new FixedTime(Now));
        var opportunity = await ugc.CreateAsync(state.Business, Input(minimumAudience: 0), "ugc-verified-profile-gate", default);
        await ugc.PublishAsync(state.Business, opportunity, 0, "ugc-verified-profile-publish", default);
        Assert.Contains(await ugc.DiscoverAsync(state.Creator, default), x => x.Id == opportunity);
        await new Weymela.Infrastructure.Web.CreatorSocialProfileLinks(db, new FixedTime(Now))
            .SaveAsync(state.Creator, CreatorPlatform.TikTok, "https://www.tiktok.com/@mimi_new", default);
        db.ChangeTracker.Clear();
        Assert.DoesNotContain(await ugc.DiscoverAsync(state.Creator, default), x => x.Id == opportunity);
        await Assert.ThrowsAsync<ApplicationFailure>(() => ugc.RequestAsync(state.Creator, opportunity, "ugc-manual-link-request", default));
    }

    private async Task<State> Setup(decimal deposit = 10_000)
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var business = new Actor(Guid.NewGuid(), ActorRole.Business, Guid.NewGuid());
        var creator = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: Guid.NewGuid());
        var configurationId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        db.BusinessWallets.Add(new BusinessWallet(business.BusinessId!.Value));
        db.CommercePermissions.AddRange(
            new CommercePermission(business.UserId, ActorRole.Business, business.BusinessId.Value, business.BusinessId, true, false),
            new CommercePermission(creator.UserId, ActorRole.Creator, creator.CreatorId!.Value, null, true, false));
        db.PublicWorkspaceProfiles.AddRange(
            new PublicWorkspaceProfile { SubjectId = business.BusinessId.Value, Role = ActorRole.Business, DisplayName = "Bella Beauty", PublicId = "BU-BELLA" },
            new PublicWorkspaceProfile { SubjectId = creator.CreatorId.Value, Role = ActorRole.Creator, DisplayName = "Mimi Creator", PublicId = "CR-MIMI" });
        db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord
        {
            CreatorId = creator.CreatorId.Value, Platform = CreatorPlatform.TikTok,
            ProfileUrl = "https://www.tiktok.com/@mimi", SelfReportedAudience = 20_000,
            VerificationStatus = "Verified", VerifiedAudience = 20_000,
            CreatedAtUtc = Now, UpdatedAtUtc = Now
        });
        db.FinancialConfigurations.Add(new(configurationId, "PlatformPricing"));
        db.FinancialConfigurationVersions.Add(new(versionId, configurationId, 1, Guid.NewGuid(), Now,
            Scenario.Price(PromotionType.ViewOnly, versionId), Scenario.Price(PromotionType.ViewPlusCommission, versionId),
            new Money(3000), new Money(4000), new UgcPricingSnapshot(new Money(200), 10m, null, Now, versionId, 3m)));
        foreach (var type in new[] { LegalDocumentType.BusinessAgreement, LegalDocumentType.AntiCircumventionAgreement })
        {
            var id = Guid.NewGuid();
            db.LegalDocumentVersions.Add(new(id, type, "1", "fixture-hash", Now));
            db.LegalAcceptances.Add(new(business.UserId, LegalRole.Business, id, Now, null, null));
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (deposit > 0) await new FinancialCommands(db, new FixedTime(Now)).CreditDepositAsync(new(business, new Money(deposit), "ugc-seed-deposit", Now), 0);
        return new(database, business, creator);
    }

    private static CreateUgcInput Input(int creators = 3, long? minimumAudience = 10_000) => new CreateUgcInput("Morning Hair Transformation", null, "Video", "Create a 30-60 second vertical video.",
        ["https://example.com/reference"], "Addis Ababa", Now.AddDays(14), true, false, "90-day digital use", 500, creators,
        [new("TikTok", "TikTok-style", minimumAudience)]);

    private static async Task DrainNotifications(TestDatabase database)
    {
        await using var db = database.Open(); var clock = new FixedTime(Now);
        var processor = new OutboxProcessor(db, new RuntimeOptions { WorkerBatchSize = 100, RecipientBatchSize = 100 }, new DisabledPushProvider(), clock);
        for (var i = 0; i < 10 && await processor.ProcessAsync(default) > 0; i++) { }
    }

    private sealed record State(TestDatabase Database, Actor Business, Actor Creator);
    private sealed class FixedTime(DateTime value) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(value); }
}
