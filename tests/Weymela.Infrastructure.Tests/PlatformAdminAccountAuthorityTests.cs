using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PlatformAdminAccountAuthorityTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 24, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(AccountLifecycleStatus.Pending, "activate", AccountLifecycleStatus.Active, true)]
    [InlineData(AccountLifecycleStatus.Pending, "cancel", AccountLifecycleStatus.Cancelled, true)]
    [InlineData(AccountLifecycleStatus.Active, "suspend", AccountLifecycleStatus.Suspended, true)]
    [InlineData(AccountLifecycleStatus.Active, "disable", AccountLifecycleStatus.Disabled, true)]
    [InlineData(AccountLifecycleStatus.Active, "revoke", AccountLifecycleStatus.Revoked, true)]
    [InlineData(AccountLifecycleStatus.Suspended, "disable", AccountLifecycleStatus.Disabled, true)]
    [InlineData(AccountLifecycleStatus.Suspended, "reactivate", AccountLifecycleStatus.Active, true)]
    [InlineData(AccountLifecycleStatus.Disabled, "reactivate", AccountLifecycleStatus.Active, true)]
    [InlineData(AccountLifecycleStatus.Active, "activate", AccountLifecycleStatus.Active, false)]
    [InlineData(AccountLifecycleStatus.Active, "reactivate", AccountLifecycleStatus.Active, false)]
    [InlineData(AccountLifecycleStatus.Suspended, "suspend", AccountLifecycleStatus.Suspended, false)]
    [InlineData(AccountLifecycleStatus.Disabled, "disable", AccountLifecycleStatus.Disabled, false)]
    [InlineData(AccountLifecycleStatus.Pending, "suspend", AccountLifecycleStatus.Pending, false)]
    [InlineData(AccountLifecycleStatus.Cancelled, "activate", AccountLifecycleStatus.Cancelled, false)]
    [InlineData(AccountLifecycleStatus.Cancelled, "reactivate", AccountLifecycleStatus.Cancelled, false)]
    [InlineData(AccountLifecycleStatus.Cancelled, "suspend", AccountLifecycleStatus.Cancelled, false)]
    [InlineData(AccountLifecycleStatus.Revoked, "reactivate", AccountLifecycleStatus.Revoked, false)]
    [InlineData(AccountLifecycleStatus.Revoked, "suspend", AccountLifecycleStatus.Revoked, false)]
    public void Account_lifecycle_policy_has_no_implicit_or_terminal_transitions(AccountLifecycleStatus current, string action,
        AccountLifecycleStatus expected, bool valid)
    {
        var actual = AccountLifecyclePolicy.TryTransition(current, action, out var desired);
        Assert.Equal(valid, actual);
        Assert.Equal(expected, desired);
    }

    [Fact]
    public async Task Platform_admin_can_preauthorize_each_supported_role_including_platform_admin_but_not_cashier()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), new CapturingDelivery());

        foreach (var role in new[] { "Customer", "Creator", "Business", "OperationsAdmin" })
        {
            var result = await service.PreauthorizeAsync(admin, new AccountPreauthorizationInput(
                role, $"{role.ToLowerInvariant()}@example.test", null, $"{role} target",
                role is "Creator" or "Business" ? $"{role.ToUpperInvariant()}-BOOTSTRAP" : null), $"preauth-{role}", default);
            Assert.Equal("Pending", result.Status);
            Assert.True(result.ActivationInstructionsSent);
            var row = await db.AccountPreauthorizations.SingleAsync(x => x.Id == result.PreauthorizationId);
            Assert.NotEmpty(row.ActivationSecretHash);
            Assert.DoesNotContain("ActivationSecret", await db.AuditEvents.Select(x => x.Detail).ToListAsync());
        }

        var platform = await service.PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("PlatformAdmin", "new-platform@example.test", null, "Platform target", Reason: "Approved staffing change"), "platform", default);
        Assert.Equal("PlatformAdmin", platform.Role);
        var cashier = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("Cashier", "new-cashier@example.test", null, "Not allowed"), "cashier", default));
        Assert.Equal(FailureKind.Validation, cashier.Kind);
    }

    [Fact]
    public async Task Existing_identity_is_reused_and_unsafe_identity_collision_is_rejected()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        db.AuthIdentifiers.AddRange(
            new AuthIdentifierRecord { UserId = first, Kind = "Email", IdentifierHash = EmailAuthService.HashIdentifier("reuse@example.test"), DeliveryAddress = "reuse@example.test", IsVerified = true, CreatedAtUtc = Now },
            new AuthIdentifierRecord { UserId = second, Kind = "Phone", IdentifierHash = EmailAuthService.HashIdentifier("+251911111111"), DeliveryAddress = "+251911111111", IsVerified = false, CreatedAtUtc = Now });
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), new CapturingDelivery());
        var reused = await service.PreauthorizeAsync(admin, new AccountPreauthorizationInput("Customer", "reuse@example.test", null, "Reuse"), "reuse", default);
        Assert.Equal(first, reused.UserId);
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = first, Kind = "Email", IdentifierHash = EmailAuthService.HashIdentifier("collision@example.test"), DeliveryAddress = "collision@example.test", IsVerified = true, CreatedAtUtc = Now });
        await db.SaveChangesAsync();
        var collision = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("Creator", "collision@example.test", "+251911111111", "Collision", "CREATOR-1"), "collision", default));
        Assert.Equal(FailureKind.Validation, collision.Kind);
    }

    [Fact]
    public async Task Platform_admin_preauthorization_activates_only_platform_authority_and_is_audited()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        await db.SaveChangesAsync();
        var delivery = new CapturingDelivery();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), delivery);
        var preauth = await service.PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("PlatformAdmin", "new-admin@example.test", null, "New admin", Reason: "Approved staffing change"),
            "platform-create", default);
        Assert.True(preauth.ActivationInstructionsSent);
        Assert.NotNull(delivery.Code);

        var identity = await db.AuthIdentifiers.SingleAsync(x => x.UserId == preauth.UserId && x.Kind == "Email");
        identity.IsVerified = true;
        db.PasswordCredentials.Add(new PasswordCredentialRecord { UserId = preauth.UserId, PasswordHash = "private-hash", WorkFactor = 100000, CreatedAtUtc = Now, ChangedAtUtc = Now, Version = 1 });
        db.AuthorizedDevices.Add(new AuthorizedDeviceRecord { UserId = preauth.UserId, CredentialKind = "Passkey", CredentialIdHash = "private-device", PinVerifier = "private-pin-verifier", EnrolledAtUtc = Now, ExpiresAtUtc = Now.AddDays(30), Version = 1 });
        await db.SaveChangesAsync();

        await service.ActivateAsync(new Actor(preauth.UserId, ActorRole.PlatformAdmin), preauth.PreauthorizationId, delivery.Code!, default);
        var permissions = await db.CommercePermissions.Where(x => x.UserId == preauth.UserId && x.IsActive).ToListAsync();
        var permission = Assert.Single(permissions);
        Assert.Equal(ActorRole.PlatformAdmin, permission.Role);
        Assert.Equal(preauth.UserId, permission.SubjectId);
        Assert.Contains(await db.AdminGrants.ToListAsync(), x => x.UserId == preauth.UserId && x.Role == ActorRole.PlatformAdmin && x.IsActive);
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "PlatformAdminAccountPreauthorized");
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.EventType == "PlatformAdminAccountActivated");
        Assert.DoesNotContain(await db.AuditEvents.Select(x => x.Detail).ToListAsync(), x => x.Contains(delivery.Code!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Operations_admin_activation_receives_only_operations_authority()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        await db.SaveChangesAsync();
        var delivery = new CapturingDelivery();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), delivery);
        var preauth = await service.PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("OperationsAdmin", "new-operations@example.test", null, "Operations target"),
            "operations-create", default);
        var identity = await db.AuthIdentifiers.SingleAsync(x => x.UserId == preauth.UserId && x.Kind == "Email");
        identity.IsVerified = true;
        db.PasswordCredentials.Add(new PasswordCredentialRecord { UserId = preauth.UserId, PasswordHash = "private-hash", WorkFactor = 100000, CreatedAtUtc = Now, ChangedAtUtc = Now, Version = 1 });
        db.AuthorizedDevices.Add(new AuthorizedDeviceRecord { UserId = preauth.UserId, CredentialKind = "Passkey", CredentialIdHash = "private-device", PinVerifier = "private-pin-verifier", EnrolledAtUtc = Now, ExpiresAtUtc = Now.AddDays(30), Version = 1 });
        await db.SaveChangesAsync();

        await service.ActivateAsync(new Actor(preauth.UserId, ActorRole.OperationsAdmin), preauth.PreauthorizationId, delivery.Code!, default);
        var permissions = await db.CommercePermissions.Where(x => x.UserId == preauth.UserId && x.IsActive).ToListAsync();
        Assert.Equal(ActorRole.OperationsAdmin, Assert.Single(permissions).Role);
        Assert.DoesNotContain(permissions, x => x.Role == ActorRole.PlatformAdmin);
        Assert.Contains(await db.AdminGrants.ToListAsync(), x => x.UserId == preauth.UserId && x.Role == ActorRole.OperationsAdmin && x.IsActive);
    }

    [Fact]
    public async Task Business_activation_preserves_can_checkout_true()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        await db.SaveChangesAsync();
        var delivery = new CapturingDelivery();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), delivery);
        var preauth = await service.PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("Business", "new-business@example.test", null, "New business", "BUSINESS-B2"),
            "business-create", default);
        var identity = await db.AuthIdentifiers.SingleAsync(x => x.UserId == preauth.UserId && x.Kind == "Email");
        identity.IsVerified = true;
        db.PasswordCredentials.Add(new PasswordCredentialRecord { UserId = preauth.UserId, PasswordHash = "private-hash", WorkFactor = 100000, CreatedAtUtc = Now, ChangedAtUtc = Now, Version = 1 });
        db.AuthorizedDevices.Add(new AuthorizedDeviceRecord { UserId = preauth.UserId, CredentialKind = "Passkey", CredentialIdHash = "private-device", PinVerifier = "private-pin-verifier", EnrolledAtUtc = Now, ExpiresAtUtc = Now.AddDays(30), Version = 1 });
        await db.SaveChangesAsync();

        await service.ActivateAsync(new Actor(preauth.UserId, ActorRole.Business), preauth.PreauthorizationId, delivery.Code!, default);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == preauth.UserId && x.Role == ActorRole.Business);
        Assert.True(permission.IsActive);
        Assert.True(permission.CanCheckout);
        Assert.Equal(permission.SubjectId, permission.BusinessId);
        Assert.Single(await db.BusinessWallets.Where(x => x.BusinessId == permission.BusinessId).ToListAsync());
    }

    [Fact]
    public async Task Only_an_active_real_platform_admin_can_use_the_provisioning_authority()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        var operations = new Actor(Guid.NewGuid(), ActorRole.OperationsAdmin);
        db.CommercePermissions.AddRange(
            new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false),
            new(operations.UserId, ActorRole.OperationsAdmin, operations.UserId, null, true, false));
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), new CapturingDelivery());
        var request = new AccountPreauthorizationInput("PlatformAdmin", "authority-target@example.test", null, "Authority target", Reason: "Approved staffing change");

        foreach (var actor in new[]
        {
            operations,
            new Actor(Guid.NewGuid(), ActorRole.Business),
            new Actor(Guid.NewGuid(), ActorRole.Creator),
            new Actor(Guid.NewGuid(), ActorRole.Customer),
            new Actor(Guid.NewGuid(), ActorRole.Cashier)
        })
        {
            var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PreauthorizeAsync(actor, request, $"denied-{actor.Role}", default));
            Assert.Equal(FailureKind.Forbidden, failure.Kind);
        }

        var forged = new AuthorityContext(
            new RealActor(operations),
            EffectiveSubject.Real(new RealActor(operations)),
            new AdministrativeAuthority(ActorRole.PlatformAdmin),
            null);
        var forgedFailure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PreauthorizeAsync(forged, request, "denied-forged", default));
        Assert.Equal(FailureKind.Forbidden, forgedFailure.Kind);

        var viewed = new EffectiveSubject(new Actor(Guid.NewGuid(), ActorRole.Business), true);
        var support = new SupportSessionContext(Guid.NewGuid(), admin.UserId, viewed, Now.AddHours(1));
        var viewAs = AuthorityContext.ForValidatedViewAs(new RealActor(admin), viewed, support, Now);
        var viewAsFailure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PreauthorizeAsync(viewAs, request, "denied-view-as", default));
        Assert.Equal(FailureKind.Forbidden, viewAsFailure.Kind);
    }

    [Fact]
    public async Task Provisioning_is_idempotent_and_does_not_return_or_audit_the_activation_secret()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        await db.SaveChangesAsync();
        var delivery = new CapturingDelivery();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), delivery);
        var request = new AccountPreauthorizationInput("Customer", "idempotent@example.test", null, "Idempotent target");
        var first = await service.PreauthorizeAsync(admin, request, "same-request", default);
        var secret = delivery.Code;
        var replay = await service.PreauthorizeAsync(admin, request, "same-request", default);
        Assert.Equal(first.PreauthorizationId, replay.PreauthorizationId);
        Assert.True(replay.ActivationInstructionsSent);
        Assert.Equal(secret, delivery.Code);
        Assert.Single(await db.AccountPreauthorizations.ToListAsync());
        var conflict = await Assert.ThrowsAsync<ApplicationFailure>(() => service.PreauthorizeAsync(admin,
            request with { DisplayName = "Different target" }, "same-request", default));
        Assert.Equal(FailureKind.IdempotencyConflict, conflict.Kind);
        Assert.DoesNotContain(await db.AuditEvents.Select(x => x.Detail).ToListAsync(), x => x.Contains(secret!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Customer_activation_reuses_authoritative_profile_and_consumes_secret_once()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin); var user = Guid.NewGuid();
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        db.IdentityBindings.Add(new IdentityBinding { UserId = user, Provider = "Firebase", ProjectId = "isolated-v3-test", ExternalSubject = user.ToString("N"), IsActive = true, ValidAfterUtc = Now.AddHours(-1), Version = 1 });
        db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = user, Kind = "Email", IdentifierHash = EmailAuthService.HashIdentifier("activate@example.test"), DeliveryAddress = "activate@example.test", IsVerified = true, CreatedAtUtc = Now });
        db.PasswordCredentials.Add(new PasswordCredentialRecord { UserId = user, PasswordHash = "private-hash", WorkFactor = 100000, CreatedAtUtc = Now, ChangedAtUtc = Now, Version = 1 });
        db.AuthorizedDevices.Add(new AuthorizedDeviceRecord { UserId = user, CredentialKind = "Passkey", CredentialIdHash = "private-device", PinVerifier = "private-pin-verifier", EnrolledAtUtc = Now, ExpiresAtUtc = Now.AddDays(30), Version = 1 });
        var terms = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.TermsOfService, "1", "terms", Now.AddHours(-1));
        var privacy = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.PrivacyPolicy, "1", "privacy", Now.AddHours(-1));
        db.LegalDocumentVersions.AddRange(terms, privacy); await db.SaveChangesAsync();
        await new AccountLegalOnboardingService(db, new FixedTime(Now)).AcceptCurrentAsync(user,
            new AccountLegalConfirmation(new(terms.Id, terms.ContentHash, true), new(privacy.Id, privacy.ContentHash, true)), null, null, default);
        await db.SaveChangesAsync();
        var delivery = new CapturingDelivery();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), delivery);
        var preauth = await service.PreauthorizeAsync(admin, new AccountPreauthorizationInput("Customer", "activate@example.test", null, "Activated customer"), "activate", default);
        var detail = await service.ActivateAsync(new Actor(user, ActorRole.Customer), preauth.PreauthorizationId, delivery.Code!, default);
        var permission = await db.CommercePermissions.SingleAsync(x => x.UserId == user && x.Role == ActorRole.Customer);
        Assert.True(permission.IsActive); Assert.False(permission.CanCheckout);
        Assert.Single(await db.CustomerProfiles.Where(x => x.UserId == user).ToListAsync());
        Assert.Empty((await db.AccountPreauthorizations.SingleAsync(x => x.Id == preauth.PreauthorizationId)).ActivationSecretHash);
        Assert.Equal(user, detail.Account.UserId);
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.ActivateAsync(new Actor(user, ActorRole.Customer), preauth.PreauthorizationId, delivery.Code!, default));
    }

    [Fact]
    public async Task Account_lifecycle_is_audited_and_non_platform_actor_is_denied()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin); var target = Guid.NewGuid(); var subject = Guid.NewGuid();
        db.CommercePermissions.AddRange(
            new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false),
            new(target, ActorRole.Creator, subject, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = ActorRole.Creator, DisplayName = "Creator", PublicId = "CREATOR-1" });
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), new CapturingDelivery());
        var forbidden = await Assert.ThrowsAsync<ApplicationFailure>(() => service.ListAsync(new Actor(target, ActorRole.Creator), new(), default));
        Assert.Equal(FailureKind.Forbidden, forbidden.Kind);
        await service.ChangeLifecycleAsync(admin, target, new AccountLifecycleInput("suspend", "support review"), "suspend", default);
        Assert.Equal(AccountLifecycleStatus.Suspended, (await db.AccountLifecycles.SingleAsync(x => x.UserId == target)).Status);
        Assert.Contains(await db.AccountRoleHistory.Where(x => x.TargetUserId == target).ToListAsync(), x => x.Action == "Suspended" && x.ActorUserId == admin.UserId);
        Assert.Contains(await db.AuditEvents.Where(x => x.TargetUserId == target).ToListAsync(), x => x.Operation == "suspend" && x.Reason == "support review");
        await service.ChangeLifecycleAsync(admin, target, new AccountLifecycleInput("reactivate", "review complete"), "reactivate", default);
        Assert.Equal(AccountLifecycleStatus.Active, (await db.AccountLifecycles.SingleAsync(x => x.UserId == target)).Status);
        Assert.True((await db.CommercePermissions.SingleAsync(x => x.UserId == target && x.Role == ActorRole.Creator)).IsActive);
    }

    [Fact]
    public async Task Activation_uses_dedicated_secret_expiry_at_the_boundary_and_cancellation_is_idempotent()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin);
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        await db.SaveChangesAsync();
        var delivery = new CapturingDelivery();
        var preauth = await new PlatformAdminAccountService(db, new FixedTime(Now), delivery).PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("Customer", "expiry@example.test", null, "Expiry target"), "expiry-preauth", default);
        var expired = new PlatformAdminAccountService(db, new FixedTime(Now.AddDays(7)), delivery);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => expired.ActivateAsync(
            new Actor(preauth.UserId, ActorRole.Customer), preauth.PreauthorizationId, delivery.Code!, default));
        Assert.Equal(FailureKind.Forbidden, failure.Kind);
        Assert.Equal(AccountPreauthorizationStatus.Expired,
            (await db.AccountPreauthorizations.SingleAsync(x => x.Id == preauth.PreauthorizationId)).Status);

        var pending = await new PlatformAdminAccountService(db, new FixedTime(Now), delivery).PreauthorizeAsync(admin,
            new AccountPreauthorizationInput("Customer", "cancel@example.test", null, "Cancel target"), "cancel-preauth", default);
        var cancelService = new PlatformAdminAccountService(db, new FixedTime(Now), delivery);
        await cancelService.CancelPreauthorizationAsync(admin, pending.PreauthorizationId, "duplicate safety", "cancel-once", default);
        await cancelService.CancelPreauthorizationAsync(admin, pending.PreauthorizationId, "duplicate safety", "cancel-once", default);
        Assert.Single(await db.AccountRoleHistory.Where(x => x.ReferenceId == pending.PreauthorizationId && x.Action == "Cancelled").ToListAsync());
        Assert.Single(await db.AuditEvents.Where(x => x.EventType == "AccountPreauthorizationCancelled" && x.TargetUserId == pending.UserId).ToListAsync());
        Assert.Single(await db.IdempotencyRecords.Where(x => x.OperationType == "AccountCancelPreauthorization").ToListAsync());
        var conflict = await Assert.ThrowsAsync<ApplicationFailure>(() => cancelService.CancelPreauthorizationAsync(admin,
            pending.PreauthorizationId, "different reason", "cancel-once", default));
        Assert.Equal(FailureKind.IdempotencyConflict, conflict.Kind);
        Assert.Equal(AccountLifecycleStatus.Cancelled, (await db.AccountLifecycles.SingleAsync(x => x.UserId == pending.UserId)).Status);
    }

    [Fact]
    public async Task Profile_revoke_requires_current_account_version_and_increments_it()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin); var target = Guid.NewGuid(); var subject = Guid.NewGuid();
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        db.CommercePermissions.Add(new(target, ActorRole.Creator, subject, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = ActorRole.Creator, DisplayName = "Versioned Creator", PublicId = "VERSIONED-CREATOR" });
        db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = target, Status = AccountLifecycleStatus.Active,
            ChangedByUserId = admin.UserId, CreatedAtUtc = Now, UpdatedAtUtc = Now, Version = 1 });
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), new CapturingDelivery());
        var stale = await Assert.ThrowsAsync<ApplicationFailure>(() => service.RevokeProfileAsync(admin, target,
            new RevokeAccountProfileInput("Creator", subject, "stale revoke", ExpectedVersion: 0), "stale", default));
        Assert.Equal(FailureKind.ConcurrencyConflict, stale.Kind);
        var current = await db.AccountLifecycles.AsNoTracking().SingleAsync(x => x.UserId == target);
        await service.RevokeProfileAsync(admin, target,
            new RevokeAccountProfileInput("Creator", subject, "approved revoke", current.Version), "current", default);
        var after = await db.AccountLifecycles.AsNoTracking().SingleAsync(x => x.UserId == target);
        Assert.True(after.Version > current.Version);
        Assert.False((await db.CommercePermissions.SingleAsync(x => x.UserId == target && x.Role == ActorRole.Creator)).IsActive);
        Assert.Single(await db.AccountRoleHistory.Where(x => x.TargetUserId == target && x.Action == "Revoked").ToListAsync());
        Assert.Single(await db.AuditEvents.Where(x => x.TargetUserId == target && x.Operation == "revoke-profile").ToListAsync());
        var second = await Assert.ThrowsAsync<ApplicationFailure>(() => service.RevokeProfileAsync(admin, target,
            new RevokeAccountProfileInput("Creator", subject, "repeat revoke", after.Version), "repeat", default));
        Assert.Equal(FailureKind.NotFound, second.Kind);
    }

    [Fact]
    public async Task Concurrent_conflicting_lifecycle_actions_cannot_overwrite_each_other()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin); var target = Guid.NewGuid(); var subject = Guid.NewGuid();
        db.CommercePermissions.Add(new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false));
        db.CommercePermissions.Add(new(target, ActorRole.Creator, subject, null, true, false));
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = ActorRole.Creator, DisplayName = "Concurrent Creator", PublicId = "CONCURRENT-CREATOR" });
        db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = target, Status = AccountLifecycleStatus.Active,
            ChangedByUserId = admin.UserId, CreatedAtUtc = Now, UpdatedAtUtc = Now, Version = 1 });
        await db.SaveChangesAsync();
        var version = (await db.AccountLifecycles.AsNoTracking().SingleAsync(x => x.UserId == target)).Version;

        async Task<(bool Succeeded, FailureKind? Failure)> AttemptAsync(string action)
        {
            await using var context = database.Open();
            try
            {
                await new PlatformAdminAccountService(context, new FixedTime(Now), new CapturingDelivery()).ChangeLifecycleAsync(admin, target,
                    new AccountLifecycleInput(action, $"concurrent {action}", version), $"concurrent-{action}", default);
                return (true, null);
            }
            catch (ApplicationFailure failure)
            {
                return (false, failure.Kind);
            }
        }

        var outcomes = await Task.WhenAll(AttemptAsync("suspend"), AttemptAsync("disable"));
        Assert.Single(outcomes, x => x.Succeeded);
        Assert.Single(outcomes, x => x.Failure == FailureKind.ConcurrencyConflict);
    }

    [Fact]
    public async Task Platform_admin_is_visible_but_stage_a_generic_mutations_are_denied()
    {
        var database = await fixture.CreateAsync(); await using var db = database.Open();
        var admin = new Actor(Guid.NewGuid(), ActorRole.PlatformAdmin); var other = Guid.NewGuid();
        db.CommercePermissions.AddRange(
            new(admin.UserId, ActorRole.PlatformAdmin, admin.UserId, null, true, false),
            new(other, ActorRole.PlatformAdmin, other, null, true, false));
        await db.SaveChangesAsync();
        var service = new PlatformAdminAccountService(db, new FixedTime(Now), new CapturingDelivery());
        Assert.Contains(await service.ListAsync(admin, new AdminAccountFilterInput("PlatformAdmin"), default), x => x.UserId == admin.UserId);
        var lifecycle = await Assert.ThrowsAsync<ApplicationFailure>(() => service.ChangeLifecycleAsync(admin, other,
            new AccountLifecycleInput("suspend", "not in Stage A"), "platform-suspend", default));
        Assert.Equal(FailureKind.Forbidden, lifecycle.Kind);
        var revoke = await Assert.ThrowsAsync<ApplicationFailure>(() => service.RevokeProfileAsync(admin, other,
            new RevokeAccountProfileInput("PlatformAdmin", other, "not in Stage A"), "platform-revoke", default));
        Assert.Equal(FailureKind.Forbidden, revoke.Kind);
    }

    private sealed class CapturingDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public string? Code { get; private set; }
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct)
        {
            Code = code;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTime value) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(value); }
}
