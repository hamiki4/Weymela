using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal static class OperationalReadinessConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var identity = model.Entity<IdentityBinding>(); Mapping.Scalars(identity); identity.HasKey(x => x.Id);
        identity.ToTable("IdentityBindings"); identity.Property(x => x.Provider).HasMaxLength(40);
        identity.Property(x => x.ProjectId).HasMaxLength(100); identity.Property(x => x.ExternalSubject).HasMaxLength(128);
        identity.HasIndex(x => new { x.Provider, x.ProjectId, x.ExternalSubject }).IsUnique(); identity.HasIndex(x => x.UserId).IsUnique(); Mapping.Version(identity);

        var profile = model.Entity<PublicWorkspaceProfile>(); Mapping.Scalars(profile); profile.HasKey(x => new { x.SubjectId, x.Role });
        profile.ToTable("PublicWorkspaceProfiles", t =>
        {
            t.HasCheckConstraint("CK_PublicProfile_Metrics", "\"VerifiedFollowers\" >= 0 AND \"VerifiedViews\" >= 0");
            t.HasCheckConstraint("CK_PublicProfile_LatitudeRange", "\"Latitude\" IS NULL OR \"Latitude\" BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_PublicProfile_LongitudeRange", "\"Longitude\" IS NULL OR \"Longitude\" BETWEEN -180 AND 180");
            t.HasCheckConstraint("CK_PublicProfile_CoordinatesPair", "(\"Latitude\" IS NULL AND \"Longitude\" IS NULL) OR (\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL)");
        });
        profile.Property(x => x.DisplayName).HasMaxLength(120); profile.Property(x => x.PublicId).HasMaxLength(80);
        profile.Property(x => x.Region).HasMaxLength(80); profile.Property(x => x.Category).HasMaxLength(80);
        profile.Property(x => x.PortfolioUrl).HasMaxLength(500); profile.Property(x => x.DirectionsUrl).HasMaxLength(500);
        profile.Property(x => x.Latitude).HasPrecision(9, 6); profile.Property(x => x.Longitude).HasPrecision(9, 6);
        profile.HasIndex(x => new { x.Role, x.PublicId }).IsUnique();

        var customer = model.Entity<CustomerProfileRecord>(); Mapping.Scalars(customer);
        customer.HasKey(x => x.CustomerId);
        customer.ToTable("CustomerProfiles", t => t.HasCheckConstraint("CK_CustomerProfile_PreferredName",
            "char_length(btrim(\"PreferredName\")) BETWEEN 1 AND 120"));
        customer.Property(x => x.PreferredName).HasMaxLength(120);
        customer.HasIndex(x => x.UserId).IsUnique();
        Mapping.Version(customer);

        var deposit = model.Entity<DepositRequest>(); Mapping.Scalars(deposit); deposit.HasKey(x => x.Id);
        deposit.ToTable("DepositRequests", t =>
        {
            t.HasCheckConstraint("CK_DepositRequest_Positive", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_DepositRequest_Review", "(\"Status\"='Pending' AND \"ReviewedBy\" IS NULL AND \"ReviewedAtUtc\" IS NULL) OR (\"Status\" IN ('Approved','Rejected') AND \"ReviewedBy\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ConfirmationReference\" IS NOT NULL)");
            t.HasCheckConstraint("CK_DepositRequest_Journal", "(\"Status\"='Approved' AND \"JournalId\" IS NOT NULL) OR (\"Status\" IN ('Pending','Rejected') AND \"JournalId\" IS NULL)");
        });
        deposit.Property(x => x.Provider).HasMaxLength(40); deposit.Property(x => x.ExternalReference).HasMaxLength(120);
        deposit.Property(x => x.ProofReference).HasMaxLength(120); deposit.Property(x => x.ConfirmationReference).HasMaxLength(120);
        deposit.HasIndex(x => new { x.BusinessId, x.Provider, x.ExternalReference }).IsUnique();
        deposit.HasIndex(x => new { x.Provider, x.ConfirmationReference }).IsUnique().HasFilter("\"Status\"='Approved'");
        deposit.HasIndex(x => new { x.Status, x.SubmittedAtUtc });
        deposit.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict); Mapping.Version(deposit);
        deposit.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);

        var notification = model.Entity<InAppNotification>(); Mapping.Scalars(notification); notification.HasKey(x => x.Id);
        notification.ToTable("InAppNotifications", t => t.HasCheckConstraint("CK_Notification_ReadTime", "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"CreatedAtUtc\""));
        notification.Property(x => x.SourceKey).HasMaxLength(160); notification.Property(x => x.EventType).HasMaxLength(100);
        notification.Property(x => x.Title).HasMaxLength(160); notification.Property(x => x.Message).HasMaxLength(500);
        notification.Property(x => x.Route).HasMaxLength(200); notification.Property(x => x.LastPushErrorCode).HasMaxLength(80);
        notification.HasIndex(x => new { x.UserId, x.Role, x.SourceKey }).IsUnique();
        notification.HasIndex(x => new { x.UserId, x.Role, x.CreatedAtUtc }); notification.HasIndex(x => x.NextPushAtUtc).HasFilter("\"PushState\"='Pending'"); Mapping.Version(notification);
        var checkpoint = model.Entity<WorkerCheckpoint>(); Mapping.Scalars(checkpoint); checkpoint.HasKey(x => x.Name);
        checkpoint.ToTable("WorkerCheckpoints"); checkpoint.Property(x => x.Name).HasMaxLength(100); checkpoint.Property(x => x.LastErrorCode).HasMaxLength(80); Mapping.Version(checkpoint);
        model.Entity<OutboxMessage>().HasIndex(x => x.NextAttemptAtUtc).HasFilter("\"ProcessedAtUtc\" IS NULL AND \"FailedAtUtc\" IS NULL");
        model.Entity<OfferQrSession>().HasIndex(x => x.ExpiresAtUtc).HasFilter("\"Status\"='Issued'");

        var identifier = model.Entity<AuthIdentifierRecord>();
        identifier.HasKey(x => x.Id); identifier.ToTable("AuthIdentifiers");
        identifier.Property(x => x.Kind).HasMaxLength(20);
        identifier.Property(x => x.IdentifierHash).HasMaxLength(64);
        identifier.Property(x => x.DeliveryAddress).HasMaxLength(320);
        identifier.HasIndex(x => new { x.Kind, x.IdentifierHash }).IsUnique();
        identifier.HasIndex(x => x.UserId);

        var challenge = model.Entity<EmailAuthChallengeRecord>();
        challenge.HasKey(x => x.Id); challenge.ToTable("EmailAuthChallenges", t =>
            t.HasCheckConstraint("CK_EmailAuthChallenge_Attempts", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= \"MaxAttempts\""));
        challenge.Property(x => x.IdentifierHash).HasMaxLength(64);
        challenge.Property(x => x.EmailIdentifierHash).HasMaxLength(64);
        challenge.Property(x => x.PhoneIdentifierHash).HasMaxLength(64);
        challenge.Property(x => x.Purpose).HasMaxLength(40);
        challenge.Property(x => x.CodeHash).HasMaxLength(128);
        challenge.Property(x => x.RecoveryGrantHash).HasMaxLength(64);
        challenge.HasIndex(x => new { x.IdentifierHash, x.Purpose, x.CreatedAtUtc });
        challenge.HasIndex(x => new { x.ExpiresAtUtc, x.ConsumedAtUtc });
        challenge.HasIndex(x => x.RecoveryGrantHash).IsUnique().HasFilter("\"RecoveryGrantHash\" IS NOT NULL");

        var password = model.Entity<PasswordCredentialRecord>();
        password.HasKey(x => x.UserId); password.ToTable("PasswordCredentials", t =>
        {
            t.HasCheckConstraint("CK_PasswordCredential_FailedAttempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 10");
            t.HasCheckConstraint("CK_PasswordCredential_Hash", "\"HashVersion\" > 0 AND \"WorkFactor\" >= 100000");
        });
        password.Property(x => x.PasswordHash).HasMaxLength(256);
        password.Property(x => x.Algorithm).HasMaxLength(40);
        password.HasIndex(x => x.LockedUntilUtc);
        Mapping.Version(password);

        var device = model.Entity<AuthorizedDeviceRecord>();
        device.HasKey(x => x.Id); device.ToTable("AuthorizedDevices", t =>
        {
            t.HasCheckConstraint("CK_AuthorizedDevice_FailedAttempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 10");
            t.HasCheckConstraint("CK_AuthorizedDevice_Recovery", "\"RequiresRecovery\" = (\"FailedAttempts\" = 10)");
            t.HasCheckConstraint("CK_AuthorizedDevice_Cooldown", "\"LockedUntilUtc\" IS NULL OR \"FailedAttempts\" >= 5");
            t.HasCheckConstraint("CK_AuthorizedDevice_Expiry", "\"ExpiresAtUtc\" IS NULL OR \"ExpiresAtUtc\" = \"EnrolledAtUtc\" + INTERVAL '30 days'");
        });
        device.Property(x => x.CredentialKind).HasMaxLength(30);
        device.Property(x => x.CredentialIdHash).HasMaxLength(128);
        device.Property(x => x.PinVerifier).HasMaxLength(128);
        device.HasIndex(x => new { x.UserId, x.CredentialIdHash }).IsUnique();
        device.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        Mapping.Version(device);

        var session = model.Entity<DeviceSessionRecord>();
        session.HasKey(x => x.Id);
        session.ToTable("DeviceSessions", t =>
        {
            t.HasCheckConstraint("CK_DeviceSession_Lifetime", "\"ExpiresAtUtc\" > \"CreatedAtUtc\" AND \"ExpiresAtUtc\" <= \"CreatedAtUtc\" + INTERVAL '1 hour'");
            t.HasCheckConstraint("CK_DeviceSession_Generation", "\"Generation\" > 0");
        });
        session.Property(x => x.SessionIdentifierHash).HasMaxLength(64);
        session.HasIndex(x => x.SessionIdentifierHash).IsUnique();
        session.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        session.HasIndex(x => new { x.AuthorizedDeviceId, x.CreatedAtUtc });
        session.HasOne<AuthorizedDeviceRecord>().WithMany().HasForeignKey(x => x.AuthorizedDeviceId).OnDelete(DeleteBehavior.Restrict);
        session.HasOne<IdentityBinding>().WithMany().HasForeignKey(x => x.IdentityBindingId).OnDelete(DeleteBehavior.Restrict);
        Mapping.Version(session);

        var enrollment = model.Entity<RoleEnrollmentRecord>();
        Mapping.Scalars(enrollment);
        enrollment.HasKey(x => x.Id); enrollment.ToTable("RoleEnrollments", t =>
            t.HasCheckConstraint("CK_RoleEnrollment_Status", "\"Status\" IN ('Pending','Approved','Rejected')"));
        enrollment.Property(x => x.SubmissionJson).HasMaxLength(6000);
        enrollment.Property(x => x.DecisionReason).HasMaxLength(500);
        enrollment.Property(x => x.IdempotencyKey).HasMaxLength(200);
        enrollment.HasIndex(x => new { x.UserId, x.RequestedRole, x.Status });
        enrollment.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        Mapping.Version(enrollment);

        var handoff = model.Entity<ProductHandoffTransaction>();
        Mapping.Scalars(handoff);
        handoff.HasKey(x => x.Id);
        handoff.ToTable("ProductHandoffTransactions", t =>
        {
            t.HasCheckConstraint("CK_ProductHandoff_Lifetime",
                "\"ExpiresAtUtc\" > \"IssuedAtUtc\" AND \"ExpiresAtUtc\" <= \"IssuedAtUtc\" + INTERVAL '60 seconds'");
            t.HasCheckConstraint("CK_ProductHandoff_Purpose",
                "\"Purpose\" IN ('PROFILE_ONBOARDING','EXISTING_WORKSPACE')");
        });
        handoff.Property(x => x.CodeHash).HasMaxLength(64);
        handoff.Property(x => x.Purpose).HasMaxLength(40);
        handoff.Property(x => x.Audience).HasMaxLength(100);
        handoff.Property(x => x.Environment).HasMaxLength(40);
        handoff.Property(x => x.CallbackId).HasMaxLength(80);
        handoff.HasIndex(x => x.CodeHash).IsUnique();
        handoff.HasIndex(x => new { x.ExpiresAtUtc, x.ConsumedAtUtc });
        handoff.HasOne<IdentityBinding>().WithMany().HasForeignKey(x => x.IdentityBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        Mapping.Version(handoff);

        var preauthorization = model.Entity<AccountPreauthorizationRecord>();
        Mapping.Scalars(preauthorization); preauthorization.HasKey(x => x.Id);
        preauthorization.ToTable("AccountPreauthorizations", t =>
            t.HasCheckConstraint("CK_AccountPreauthorization_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\" AND \"ActivationSecretExpiresAtUtc\" = \"ExpiresAtUtc\""));
        preauthorization.Property(x => x.EmailIdentifierHash).HasMaxLength(64);
        preauthorization.Property(x => x.PhoneIdentifierHash).HasMaxLength(64);
        preauthorization.Property(x => x.DisplayName).HasMaxLength(120);
        preauthorization.Property(x => x.PublicId).HasMaxLength(80);
        preauthorization.Property(x => x.Region).HasMaxLength(80);
        preauthorization.Property(x => x.Category).HasMaxLength(80);
        preauthorization.Property(x => x.SubmissionJson).HasMaxLength(6000);
        preauthorization.Property(x => x.ActivationSecretHash).HasMaxLength(128);
        preauthorization.HasIndex(x => new { x.EmailIdentifierHash, x.TargetRole, x.Status });
        preauthorization.HasIndex(x => new { x.UserId, x.TargetRole, x.Status });
        Mapping.Version(preauthorization);

        var lifecycle = model.Entity<AccountLifecycleRecord>();
        Mapping.Scalars(lifecycle); lifecycle.HasKey(x => x.UserId);
        lifecycle.ToTable("AccountLifecycles", t =>
            t.HasCheckConstraint("CK_AccountLifecycle_Reason", "\"Reason\" IS NULL OR char_length(btrim(\"Reason\")) BETWEEN 1 AND 500"));
        lifecycle.Property(x => x.Reason).HasMaxLength(500);
        lifecycle.HasIndex(x => new { x.Status, x.UpdatedAtUtc });
        Mapping.Version(lifecycle);

        var roleHistory = model.Entity<AccountRoleHistoryRecord>();
        Mapping.Scalars(roleHistory); roleHistory.HasKey(x => x.Id);
        roleHistory.ToTable("AccountRoleHistory");
        roleHistory.Property(x => x.Action).HasMaxLength(60);
        roleHistory.Property(x => x.Reason).HasMaxLength(500);
        roleHistory.HasIndex(x => new { x.TargetUserId, x.OccurredAtUtc });
        roleHistory.HasIndex(x => new { x.ActorUserId, x.OccurredAtUtc });

    }
}
