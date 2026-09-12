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
        profile.ToTable("PublicWorkspaceProfiles", t => t.HasCheckConstraint("CK_PublicProfile_Metrics", "\"VerifiedFollowers\" >= 0 AND \"VerifiedViews\" >= 0"));
        profile.Property(x => x.DisplayName).HasMaxLength(120); profile.Property(x => x.PublicId).HasMaxLength(80);
        profile.Property(x => x.Region).HasMaxLength(80); profile.Property(x => x.Category).HasMaxLength(80);
        profile.Property(x => x.PortfolioUrl).HasMaxLength(500); profile.Property(x => x.DirectionsUrl).HasMaxLength(500);
        profile.HasIndex(x => new { x.Role, x.PublicId }).IsUnique();

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
    }
}
