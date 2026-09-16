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
        Assert.Empty(await service.PendingAsync(default));
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
    public async Task Existing_customer_can_request_creator_without_losing_customer_access()
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
    }

    [Fact]
    public async Task Business_approval_creates_new_scoped_business_and_is_idempotent()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var user = Guid.NewGuid(); var customer = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.CommercePermissions.Add(new CommercePermission(user, ActorRole.Customer, customer, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = customer, Role = ActorRole.Customer, DisplayName = "Customer", PublicId = "CU-2" });
        await db.SaveChangesAsync();
        var service = new RoleEnrollmentService(db, TimeProvider.System);
        var row = await service.SubmitAsync(new Actor(user, ActorRole.Customer, CustomerId: customer), new RoleEnrollmentRequest(ActorRole.Business, "ABC Café", "BUS-2", "Addis", "Restaurant", "Business details"), "enroll-business", default);
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
        var first = new RoleEnrollmentRequest(ActorRole.Creator, "Bella", "CR-3", null, null, null);
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
