using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class RoleEnrollmentTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Customer_activation_is_immediate_idempotent_and_zero_balance()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var legal = await SeedAccountLegalAsync(db);

        var first = await service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "USER-CONTROLLED", null, null, null, AccountLegal: legal), "customer-1", default);
        var replay = await service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "A-DIFFERENT-IGNORED-VALUE", null, null, null, AccountLegal: legal), "customer-1", default);

        Assert.Equal(RoleEnrollmentStatus.Approved, first.Status);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.PublicId, replay.PublicId);
        Assert.StartsWith("CU-", first.PublicId, StringComparison.Ordinal);
        Assert.DoesNotContain("USER-CONTROLLED", first.PublicId, StringComparison.Ordinal);
        Assert.Single(await db.RoleEnrollments.Where(x => x.UserId == user && x.RequestedRole == ActorRole.Customer).ToListAsync());
        Assert.Empty(await service.PendingAsync(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), default));
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Customer);
        Assert.True(permission.IsActive);
        var projected = await db.PublicWorkspaceProfiles.SingleAsync(x => x.SubjectId == permission.SubjectId && x.Role == ActorRole.Customer);
        Assert.Equal(first.PublicId, projected.PublicId);
        var profile = await db.CustomerProfiles.SingleAsync(x => x.CustomerId == permission.SubjectId);
        Assert.Equal(user, profile.UserId);
        Assert.Equal("Hana", profile.PreferredName);
        var cashback = await db.CustomerCashbackAccounts.SingleAsync(x => x.CustomerId == permission.SubjectId);
        Assert.Equal(0m, cashback.AvailableCashback.Amount);
        Assert.Contains(await db.AuditEvents.Where(x => x.ActorId == user).ToListAsync(), x => x.EventType == "CustomerProfileActivated");
        Assert.Equal(2, await db.LegalAcceptances.CountAsync(x => x.UserId == user && x.Role == LegalRole.Account));
    }

    [Fact]
    public async Task Customer_activation_is_resolved_from_persisted_profile_on_session_refresh()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding
        {
            Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = "uid", UserId = user,
            IsActive = true, ValidAfterUtc = DateTime.UtcNow.AddMinutes(-1), Version = 1
        });
        await db.SaveChangesAsync();
        var legal = await SeedAccountLegalAsync(db);

        var enrollment = new RoleEnrollmentService(db, TimeProvider.System);
        var created = await enrollment.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-REFRESH", null, null, null, AccountLegal: legal), "customer-refresh", default);

        var result = await new TrustedIdentityService(db, new Verifier(), new PersistentWorkspaceDirectory(db)).SignInAsync("token", default);
        Assert.Equal(ActorRole.Customer, result.Actor.Role);
        Assert.NotEqual(Guid.Empty, result.Actor.CustomerId);
        Assert.Single(result.Profiles);
        Assert.Equal(created.PublicId, result.PublicId);
    }

    [Fact]
    public async Task Customer_activation_fails_atomically_for_missing_stale_or_tampered_legal_documents()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-LEGAL", null, null, null), "missing", default));
        Assert.False(await db.CommercePermissions.AnyAsync(x => x.UserId == user));

        var legal = await SeedAccountLegalAsync(db);
        var tampered = legal with { TermsOfService = legal.TermsOfService! with { ContentHash = "tampered" } };
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-LEGAL", null, null, null, AccountLegal: tampered), "tampered", default));
        Assert.False(await db.RoleEnrollments.AnyAsync(x => x.UserId == user));
        Assert.False(await db.LegalAcceptances.AnyAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task Account_legal_status_detects_a_new_current_version_without_mutating_history()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var legal = await SeedAccountLegalAsync(db);
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        await service.SubmitAsync(new Actor(user, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Hana", "CU-VERSION", null, null, null, AccountLegal: legal), "version-1", default);
        var oldCount = await db.LegalAcceptances.CountAsync(x => x.UserId == user);
        db.LegalDocumentVersions.Add(new(Guid.NewGuid(), LegalDocumentType.TermsOfService, "fixture-2", "fixture-terms-hash-2", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var status = await new AccountLegalOnboardingService(db, TimeProvider.System).StatusAsync(user, default);
        Assert.True(status.Available);
        Assert.False(status.Current);
        Assert.Equal(oldCount, await db.LegalAcceptances.CountAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task Account_legal_acceptance_is_idempotent_and_current_for_the_same_effective_versions()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var confirmation = await SeedAccountLegalAsync(db);
        var legal = new AccountLegalOnboardingService(db, TimeProvider.System);

        await legal.AcceptCurrentAsync(user, confirmation, null, null, default);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await legal.AcceptCurrentAsync(user, confirmation, null, null, default);
        await db.SaveChangesAsync();

        var status = await legal.StatusAsync(user, default);
        Assert.True(status.Available);
        Assert.True(status.Current);
        Assert.All(status.Documents, document => Assert.True(document.Accepted));
        Assert.Equal(2, await db.LegalAcceptances.CountAsync(x =>
            x.UserId == user && x.Role == LegalRole.Account));
    }

    [Fact]
    public async Task Concurrent_customer_activation_never_creates_duplicate_profile_or_acceptance_evidence()
    {
        var database = await fixture.CreateAsync();
        AccountLegalConfirmation legal;
        await using (var seed = database.Open()) legal = await SeedAccountLegalAsync(seed);
        var user = Guid.NewGuid();
        await using var firstDb = database.Open();
        await using var secondDb = database.Open();
        async Task<bool> Attempt(Weymela.Infrastructure.Persistence.WeymelaDbContext db, string key)
        {
            try
            {
                await new RoleEnrollmentService(db, TimeProvider.System).SubmitAsync(new Actor(user, ActorRole.Customer),
                    new RoleEnrollmentRequest(ActorRole.Customer, "Concurrent", "CU-CONCURRENT", null, null, null,
                        AccountLegal: legal), key, default);
                return true;
            }
            catch (Exception) { return false; }
        }
        var outcomes = await Task.WhenAll(Attempt(firstDb, "concurrent-customer-a"), Attempt(secondDb, "concurrent-customer-b"));
        Assert.Contains(true, outcomes);
        await using var verify = database.Open();
        Assert.Single(await verify.RoleEnrollments.Where(x => x.UserId == user && x.RequestedRole == ActorRole.Customer).ToListAsync());
        Assert.Single(await verify.CommercePermissions.Where(x => x.UserId == user && x.Role == ActorRole.Customer).ToListAsync());
        Assert.Single(await verify.CustomerProfiles.Where(x => x.UserId == user).ToListAsync());
        Assert.Equal(2, await verify.LegalAcceptances.CountAsync(x => x.UserId == user && x.Role == LegalRole.Account));
    }

    [Fact]
    public async Task Missing_account_legal_documents_return_a_clean_unavailable_status()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();

        var status = await new AccountLegalOnboardingService(db, TimeProvider.System)
            .StatusAsync(Guid.NewGuid(), default);

        Assert.False(status.Available);
        Assert.False(status.Current);
        Assert.Empty(status.Documents);
    }

    [Fact]
    public async Task Customer_identifiers_are_unique_and_identity_data_is_not_stored_in_profiles()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var legal = await SeedAccountLegalAsync(db);
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();

        var first = await service.SubmitAsync(new Actor(firstUser, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "First", null, null, null, null, AccountLegal: legal),
            "first-customer", default);
        var second = await service.SubmitAsync(new Actor(secondUser, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Second", null, null, null, null, AccountLegal: legal),
            "second-customer", default);

        Assert.NotEqual(first.PublicId, second.PublicId);
        Assert.Equal(2, await db.CustomerProfiles.CountAsync());
        var names = typeof(CustomerProfileRecord).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, x => x.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Phone", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<AccountLegalConfirmation> SeedAccountLegalAsync(Weymela.Infrastructure.Persistence.WeymelaDbContext db)
    {
        var terms = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.TermsOfService, "fixture-1", "fixture-terms-hash", DateTime.UtcNow.AddMinutes(-1));
        var privacy = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.PrivacyPolicy, "fixture-1", "fixture-privacy-hash", DateTime.UtcNow.AddMinutes(-1));
        db.LegalDocumentVersions.AddRange(terms, privacy);
        await db.SaveChangesAsync();
        return new(new(terms.Id, terms.ContentHash, true), new(privacy.Id, privacy.ContentHash, true));
    }

    [Fact]
    public async Task Existing_customer_can_add_creator_with_server_assigned_number_without_losing_customer_access()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-1" });
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var request = new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-1", "Addis", "Food", "Short bio");
        var result = await service.SubmitAsync(new Actor(user, ActorRole.Customer, CustomerId: customer), request, "enroll-1", default);
        Assert.Equal(RoleEnrollmentStatus.Pending, result.Status);
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.Role == ActorRole.Customer && x.IsActive));
        await service.ReviewAsync(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), result.Id, true, null, result.Version, "approve-creator", default);
        var creator = await db.PublicWorkspaceProfiles.SingleAsync(x => x.Role == ActorRole.Creator);
        Assert.True(creator.CreatorNumber >= 1000);
        Assert.Equal("CR-1", creator.PublicId);
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.Role == ActorRole.Customer && x.IsActive));
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.Role == ActorRole.Creator && x.SubjectId == creator.SubjectId && x.IsActive));
    }

    [Fact]
    public async Task Public_creator_application_requires_valid_social_link_and_activates_only_after_operations_review()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var actor = new Actor(user, ActorRole.Customer, CustomerId: customer);
        var request = new RoleEnrollmentRequest(ActorRole.Creator, "Bella", null, "Addis", "Food", "Recipes",
            SocialProfiles: [new("TikTok", "https://www.tiktok.com/@bella")]);
        var missing = await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(actor,
            request with { SocialProfiles = [] }, "missing-social", default));
        Assert.Equal(FailureKind.Validation, missing.Kind);
        var invalid = await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(actor,
            request with { SocialProfiles = [new("TikTok", "https://evil.example/@bella")] }, "invalid-social", default));
        Assert.Equal(FailureKind.Validation, invalid.Kind);
        var pending = await service.SubmitAsync(actor, request, "creator-application", default);
        const string reviewEmail = "creator-review@example.test";
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = user, Kind = "Email",
            IdentifierHash = EmailAuthService.HashIdentifier(reviewEmail), DeliveryAddress = reviewEmail,
            IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var replay = await service.SubmitAsync(actor, request, "creator-application", default);
        Assert.Equal(pending.Id, replay.Id);
        Assert.Equal(RoleEnrollmentStatus.Pending, pending.Status);
        Assert.StartsWith("CR-", pending.PublicId, StringComparison.Ordinal);
        Assert.False(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.Role == ActorRole.Creator));
        Assert.Single(await db.OutboxMessages.Where(x => x.EventType == "RoleEnrollmentSubmitted").ToListAsync());
        Assert.Null(Assert.Single(await service.MineAsync(user, default)).FullEmail);
        var admin = new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin);
        var review = Assert.Single(await service.PendingAsync(admin, default));
        Assert.Equal(reviewEmail, review.FullEmail);
        Assert.Equal("Food", review.Category);
        Assert.Equal("Recipes", review.Submission);
        Assert.Equal("TikTok", Assert.Single(review.SocialProfiles!).Platform);
        var approved = await service.ReviewAsync(admin, pending.Id, true, null, pending.Version, "approve", default);
        Assert.Equal(RoleEnrollmentStatus.Approved, approved.Status);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Creator);
        Assert.True(permission.IsActive);
        var profile = await db.PublicWorkspaceProfiles.SingleAsync(x => x.SubjectId == permission.SubjectId);
        Assert.Equal(pending.PublicId, profile.PublicId);
        Assert.True(profile.CreatorNumber >= 1000);
        var social = await db.CreatorSocialProfiles.SingleAsync(x => x.CreatorId == permission.SubjectId);
        Assert.Equal("SelfReported", social.VerificationStatus);
        Assert.Equal("https://www.tiktok.com/@bella", social.ProfileUrl);
    }

    [Fact]
    public async Task Verified_public_business_registration_is_immediate_idempotent_and_zero_balance()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var legal = await SeedAccountLegalAsync(db);
        var request = new RoleEnrollmentRequest(ActorRole.Business, "Bella Cafe", null, "Addis", "Restaurant", "Cafe");
        var approved = await service.SubmitAsync(new Actor(user, ActorRole.Customer), request with { AccountLegal = legal }, "business-application", default);
        var replay = await service.SubmitAsync(new Actor(user, ActorRole.Customer), request with { AccountLegal = legal }, "business-application", default);
        Assert.Equal(RoleEnrollmentStatus.Approved, approved.Status);
        Assert.Equal(approved.Id, replay.Id);
        Assert.StartsWith("BU-", approved.PublicId, StringComparison.Ordinal);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Business);
        Assert.True(permission.IsActive);
        Assert.True(permission.CanCheckout);
        Assert.Equal(permission.SubjectId, permission.BusinessId);
        var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == permission.BusinessId);
        Assert.Equal(0m, wallet.AvailableBalance.Amount);
        Assert.Equal(0m, wallet.ReservedBalance.Amount);
        Assert.Empty(await service.PendingAsync(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), default));
        Assert.Empty(await db.OutboxMessages.Where(x => x.EventType == "RoleEnrollmentSubmitted").ToListAsync());
        Assert.Contains(await db.AuditEvents.Where(x => x.ActorId == user).ToListAsync(),
            x => x.EventType == "BusinessProfileActivated");
        var denied = await Assert.ThrowsAsync<ApplicationFailure>(() => service.ReviewAsync(new Actor(user, ActorRole.Customer),
            approved.Id, true, null, approved.Version, "self-review", default));
        Assert.Equal(FailureKind.Forbidden, denied.Kind);
    }

    [Fact]
    public async Task Verified_public_business_registration_preserves_role_details_without_admin_review()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        const string email = "new-owner@example.test";
        const string phone = "+251911111111";
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = user, Kind = "Email",
            IdentifierHash = EmailAuthService.HashIdentifier(email), DeliveryAddress = email,
            IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
        var legal = await SeedAccountLegalAsync(db);
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var request = new RoleEnrollmentRequest(ActorRole.Business, "ABC Trading", null, null,
            "Retail", null, AccountLegal: legal, LegalName: "Abebe Kebede", RegisteredPhone: phone);

        var approved = await service.SubmitAsync(new Actor(user, ActorRole.Customer), request,
            "public-business-registration", default);
        var replay = await service.SubmitAsync(new Actor(user, ActorRole.Customer), request,
            "public-business-registration", default);

        Assert.Equal(approved.Id, replay.Id);
        Assert.Equal(RoleEnrollmentStatus.Approved, approved.Status);
        Assert.True(await db.CommercePermissions.AnyAsync(x => x.UserId == user && x.Role == ActorRole.Business && x.IsActive));
        var registeredPhone = Assert.Single(await db.AuthIdentifiers.Where(x => x.UserId == user && x.Kind == "Phone").ToListAsync());
        Assert.Equal(phone, registeredPhone.DeliveryAddress);
        Assert.False(registeredPhone.IsVerified);
        Assert.Single(await db.RoleEnrollments.Where(x => x.UserId == user && x.RequestedRole == ActorRole.Business
            && x.Status == RoleEnrollmentStatus.Approved).ToListAsync());
        Assert.Empty(await db.OutboxMessages.Where(x => x.EventType == "RoleEnrollmentSubmitted").ToListAsync());
        Assert.Empty(await service.PendingAsync(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), default));
        var saved = Assert.Single(await service.MineAsync(user, default));
        Assert.Equal("ABC Trading", saved.DisplayName);
        Assert.Equal("Abebe Kebede", saved.LegalName);
        Assert.Equal("Retail", saved.Category);
    }

    [Fact]
    public async Task Creator_registration_preserves_follower_and_subscriber_counts_for_review()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        const string email = "new-creator@example.test";
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = user, Kind = "Email",
            IdentifierHash = EmailAuthService.HashIdentifier(email), DeliveryAddress = email,
            IsVerified = true, CreatedAtUtc = DateTime.UtcNow });
        var legal = await SeedAccountLegalAsync(db);
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var request = new RoleEnrollmentRequest(ActorRole.Creator, "Hana Bekele", null, null, null, null,
            AccountLegal: legal, SocialProfiles: [new("YouTube", "https://www.youtube.com/@hana", 0, 800, 1200)],
            LegalName: "Hana Bekele", RegisteredPhone: "+251922222222");

        await service.SubmitAsync(new Actor(user, ActorRole.Customer), request, "public-creator-registration", default);
        var review = Assert.Single(await service.PendingAsync(new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin), default));
        var social = Assert.Single(review.SocialProfiles!);
        Assert.Equal(800, social.FollowerCount);
        Assert.Equal(1200, social.SubscriberCount);
        Assert.Equal(1200, social.AudienceCount);
    }

    [Fact]
    public async Task Existing_pending_business_review_is_not_auto_approved_and_remains_reviewable()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-2" });
        await db.SaveChangesAsync();
        var row = new RoleEnrollmentRecord
        {
            UserId = user, RequestedRole = ActorRole.Business, SubmittedAtUtc = DateTime.UtcNow,
            IdempotencyKey = "legacy-pending-business",
            SubmissionJson = """{"DisplayName":"ABC Café","PublicId":"BUS-2","Region":"Addis","Category":"Restaurant","Submission":"Business details","LegalName":null,"SocialProfiles":[]}"""
        };
        db.RoleEnrollments.Add(row);
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        Assert.Equal(RoleEnrollmentStatus.Pending, (await service.MineAsync(user, default)).Single().Status);
        var approved = await service.ReviewAsync(new Actor(admin, ActorRole.PlatformAdmin), row.Id, true, null, row.Version, "review-1", default);
        var replay = await service.ReviewAsync(new Actor(admin, ActorRole.PlatformAdmin), row.Id, true, null, approved.Version, "review-2", default);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Business);
        Assert.Equal(RoleEnrollmentStatus.Approved, replay.Status);
        Assert.Equal(permission.SubjectId, permission.BusinessId);
        Assert.Single(await db.CommercePermissions.Where(x => x.UserId == user && x.Role == ActorRole.Business).ToListAsync());
        Assert.True(await db.BusinessWallets.AnyAsync(x => x.BusinessId == permission.BusinessId));
    }

    [Fact]
    public async Task Conflicting_submission_reference_and_tampered_business_are_rejected()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var legal = await SeedAccountLegalAsync(db);
        var first = new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-3", null, null, null, AccountLegal: legal);
        await service.SubmitAsync(new Actor(user, ActorRole.Customer, CustomerId: Guid.NewGuid()), first, "same-key", default);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), first, "another-key", default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), first with { DisplayName = "Other" }, "same-key", default));
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), first with { ProposedBusinessId = Guid.NewGuid() }, "other-key", default));
    }

    [Fact]
    public async Task Suspended_membership_cannot_be_resurrected_by_a_new_request()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Creator, Guid.NewGuid(), null, false, false));
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(user, ActorRole.Customer), new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-4", null, null, null), "new-request", default));
    }

    [Fact]
    public async Task Public_submission_cannot_request_cashier_or_admin()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        foreach (var role in new[] { ActorRole.Cashier, ActorRole.PlatformAdmin })
            await Assert.ThrowsAsync<ApplicationFailure>(() => service.SubmitAsync(new Actor(Guid.NewGuid(), ActorRole.Customer), new RoleEnrollmentRequest(role, "No", "NO", null, null, null), Guid.NewGuid().ToString("N"), default));
    }

    private sealed class Verifier : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string _, CancellationToken __)
            => Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", "uid", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
    }
}
