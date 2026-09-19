using Microsoft.EntityFrameworkCore;
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
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class UgcPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

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
        Assert.Equal(1650m, opportunity.RequiredFunding.Amount); Assert.Equal(550m, opportunity.UsedFunding.Amount);
        Assert.Equal(1100m, opportunity.ReservedFunding.Amount); Assert.Equal(8350m, wallet.AvailableBalance.Amount);
        Assert.Equal(1100m, wallet.ReservedBalance.Amount);
        Assert.Equal(500m, (await db.CreatorEarningsAccounts.SingleAsync(x => x.CreatorId == state.Creator.CreatorId)).AvailableEarnings.Amount);
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
        Assert.Equal(500m, detail.Opportunity.CreatorPayment);
        Assert.Null(detail.Opportunity.RequiredFunding);
        Assert.Null(detail.Opportunity.PlatformFeePercent);
        Assert.Null(detail.Opportunity.PlatformFee);
        Assert.Null(detail.Opportunity.CustomerDiscountPercent);
    }

    [Fact]
    public async Task Customer_offer_discount_cannot_exceed_effective_admin_maximum()
    {
        var state = await Setup();
        await using var db = state.Database.Open(); var service = new UgcService(db, new FixedTime(Now));
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.CreateAsync(state.Business, Input() with
        {
            CustomerOfferEnabled = true, CustomerDiscountPercent = 21m,
            CustomerOfferFundedAllocation = 1000m, CustomerOfferStartsAtUtc = Now,
            CustomerOfferEndsAtUtc = Now.AddDays(7)
        }, "ugc-discount-limit", default));
        Assert.Equal(FailureKind.Validation, failure.Kind);
    }

    [Fact]
    public async Task Ugc_customer_offer_sale_uses_separate_fund_discount_and_platform_fee_with_zero_creator_share()
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
                CustomerOfferFundedAllocation = 5600m, CustomerFacingSlogan = "Save on your next visit",
                CustomerOfferStartsAtUtc = Now, CustomerOfferEndsAtUtc = Now.AddDays(7)
            }, "offer-create", default);
            await ugc.PublishAsync(state.Business, id, 0, "offer-publish", default);
            var offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == id).Select(x => x.Id).SingleAsync();
            qr = await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                .IssueUgcAsync(new(customer, offerId, "offer-qr"), default);
        }
        SaleResult result;
        await using (var db = state.Database.Open())
            result = await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                .RedeemAsync(new(cashier, qr.Token!, new Money(1000), "offer-sale"), default);
        Assert.Equal("UGC_CUSTOMER_OFFER", result.Source); Assert.Equal(950m, result.CustomerPays!.Value.Amount);
        Assert.Equal(50m, result.CustomerDiscount!.Value.Amount); Assert.Equal(80m, result.TotalBusinessCharge.Amount);

        await using var verify = state.Database.Open();
        var sale = await verify.UgcCustomerOfferSales.SingleAsync();
        Assert.Equal(50m, sale.CustomerDiscountAmount.Amount); Assert.Equal(30m, sale.PlatformRevenueAmount.Amount);
        Assert.Empty(await verify.CreatorEarningEntries.ToListAsync());
        Assert.Empty(await verify.CustomerCashbackEntries.ToListAsync());
        Assert.Single(await verify.PlatformRevenueEntries.Where(x => x.Source == PlatformRevenueSource.UgcCustomerOfferSaleFee).ToListAsync());
        Assert.Equal(5520m, (await verify.UgcCustomerOffers.SingleAsync()).RemainingFunding.Amount);
        Assert.Equal(7170m, (await verify.BusinessWallets.SingleAsync()).ReservedBalance.Amount);
        Assert.Equal(OfferQrStatus.Used, (await verify.OfferQrSessions.SingleAsync()).Status);
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
            var offerId = await db.UgcCustomerOffers.Where(x => x.UgcOpportunityId == id).Select(x => x.Id).SingleAsync();
            qr = await new CheckoutService(db, new CommerceAccessPolicy(db), new FixedTime(Now))
                .IssueUgcAsync(new(customer, offerId, "small-offer-qr"), default);
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

        var cards = await new WorkspaceQueries(db, new PersistentWorkspaceDirectory(db), new FixedTime(Now))
            .OffersAsync(customer, default);

        var card = Assert.Single(cards);
        Assert.Equal("UGC_CUSTOMER_OFFER", card.Source);
        Assert.Equal("Save on your next visit", card.Offer);
        Assert.Equal("Bella Beauty", card.Business.DisplayName);
        Assert.Equal(5m, card.BenefitPercent);
        Assert.Null(card.Creator);
        Assert.Null(card.WatchUrl);
        Assert.DoesNotContain("Internal", card.Offer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Insufficient_available_balance_rolls_back_publish_reservation_and_journal()
    {
        var state = await Setup(deposit: 1000); await using var db = state.Database.Open();
        var service = new UgcService(db, new FixedTime(Now));
        var id = await service.CreateAsync(state.Business, Input(), "create-insufficient", default);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PublishAsync(state.Business, id, 0, "publish-insufficient", default));
        Assert.Equal(FailureKind.InsufficientFunds, failure.Kind);
        db.ChangeTracker.Clear();
        Assert.Equal(UgcOpportunityStatus.Draft, (await db.UgcOpportunities.SingleAsync(x => x.Id == id)).Status);
        Assert.Empty(await db.UgcReservations.ToListAsync());
        Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.Key == "publish-insufficient"));
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
                ProfileUrl = "https://www.tiktok.com/@second", SelfReportedAudience = 25_000, CreatedAtUtc = Now, UpdatedAtUtc = Now });
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
            600, 2, [new("Instagram", "Instagram/Reels", 5_000)]), 0, "ugc-edit-draft", default);
        db.ChangeTracker.Clear();
        var opportunity = await db.UgcOpportunities.Include(x => x.PlatformRequirements).SingleAsync(x => x.Id == id);
        Assert.Equal("Updated transformation", opportunity.Title); Assert.Equal(UgcContentType.Photos, opportunity.ContentType);
        Assert.Equal(600m, opportunity.CreatorPayment.Amount); Assert.Equal(2, opportunity.CreatorCapacity);
        Assert.Equal(1320m, opportunity.RequiredFunding.Amount); Assert.Equal("Instagram", opportunity.PlatformRequirements.Single().Platform.ToString());
        Assert.Equal(2, await db.UgcRevisions.CountAsync(x => x.UgcOpportunityId == id));
        await service.PublishAsync(state.Business, id, opportunity.Version, "ugc-edit-publish", default);
        db.ChangeTracker.Clear();
        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == state.Business.BusinessId);
        Assert.Equal(8680m, wallet.AvailableBalance.Amount); Assert.Equal(1320m, wallet.ReservedBalance.Amount);
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
        Assert.Equal(500m, current.CreatorPayment.Amount); Assert.Equal(1650m, current.RequiredFunding.Amount);
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
            CreatedAtUtc = Now, UpdatedAtUtc = Now
        });
        db.FinancialConfigurations.Add(new(configurationId, "PlatformPricing"));
        db.FinancialConfigurationVersions.Add(new(versionId, configurationId, 1, Guid.NewGuid(), Now,
            Scenario.Price(PromotionType.ViewOnly, versionId), Scenario.Price(PromotionType.ViewPlusCommission, versionId),
            new Money(3000), new Money(4000), new UgcPricingSnapshot(new Money(200), 10m, null, Now, versionId, 3m, 20m)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await new FinancialCommands(db, new FixedTime(Now)).CreditDepositAsync(new(business, new Money(deposit), "ugc-seed-deposit", Now), 0);
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
