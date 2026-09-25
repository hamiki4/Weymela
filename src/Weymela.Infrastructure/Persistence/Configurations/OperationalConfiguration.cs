using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal static class OperationalConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var reservation = model.Entity<PromotionReservation>(); Mapping.Scalars(reservation);
        reservation.ToTable("PromotionReservations", t => t.HasCheckConstraint("CK_Reservation_Positive", "\"OriginalAmount\" > 0"));
        reservation.HasKey(x => x.PromotionId);
        reservation.HasOne<Promotion>().WithOne().HasForeignKey<PromotionReservation>(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);

        var wallet = model.Entity<WalletEntry>(); Mapping.Scalars(wallet); wallet.HasKey(x => x.Id);
        wallet.ToTable("WalletEntries", t => t.HasCheckConstraint("CK_WalletEntry_Positive", "\"Amount\" > 0"));
        wallet.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        wallet.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        wallet.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
        var promotionalFunding = model.Entity<PlatformPromotionalFundingRecord>(); Mapping.Scalars(promotionalFunding);
        promotionalFunding.HasKey(x => x.Id);
        promotionalFunding.ToTable("PlatformPromotionalFundings", t =>
            t.HasCheckConstraint("CK_PlatformPromotionalFunding_Valid", "\"Amount\" > 0 AND \"Reason\" <> '' AND \"PlatformAdminDisplayNameSnapshot\" <> ''"));
        promotionalFunding.Property(x => x.Reason).HasMaxLength(500);
        promotionalFunding.Property(x => x.PlatformAdminDisplayNameSnapshot).HasMaxLength(200);
        promotionalFunding.Property(x => x.IdempotencyKey).HasMaxLength(200);
        promotionalFunding.Property(x => x.RequestFingerprint).HasMaxLength(64);
        promotionalFunding.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        promotionalFunding.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        promotionalFunding.HasIndex(x => x.JournalId).IsUnique();
        promotionalFunding.HasIndex(x => new { x.PlatformAdminUserId, x.IdempotencyKey }).IsUnique();
        promotionalFunding.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
        var budget = model.Entity<PromotionBudgetEntry>(); Mapping.Scalars(budget); budget.HasKey(x => x.Id);
        budget.ToTable("PromotionBudgetEntries", t => t.HasCheckConstraint("CK_BudgetEntry_Positive", "\"Amount\" > 0"));
        budget.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        budget.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);
        budget.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        budget.HasIndex(x => new { x.PromotionId, x.CreatedAtUtc });

        var ugcReservation = model.Entity<UgcReservation>(); Mapping.Scalars(ugcReservation);
        ugcReservation.ToTable("UgcReservations", t => t.HasCheckConstraint("CK_UgcReservation_Positive", "\"OriginalAmount\" > 0"));
        ugcReservation.HasKey(x => x.UgcOpportunityId);
        ugcReservation.HasOne<UgcOpportunity>().WithOne().HasForeignKey<UgcReservation>(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        ugcReservation.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        ugcReservation.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);

        var ugcBudget = model.Entity<UgcBudgetEntry>(); Mapping.Scalars(ugcBudget); ugcBudget.HasKey(x => x.Id);
        ugcBudget.ToTable("UgcBudgetEntries", t => t.HasCheckConstraint("CK_UgcBudgetEntry_Positive", "\"Amount\" > 0"));
        ugcBudget.HasOne<UgcOpportunity>().WithMany().HasForeignKey(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        ugcBudget.HasOne<UgcAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        ugcBudget.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        ugcBudget.HasIndex(x => new { x.UgcOpportunityId, x.CreatedAtUtc });

        var offerReservation = model.Entity<UgcCustomerOfferReservation>(); Mapping.Scalars(offerReservation);
        offerReservation.ToTable("UgcCustomerOfferReservations", t => t.HasCheckConstraint("CK_UgcCustomerOfferReservation_Positive", "\"OriginalAmount\" > 0"));
        offerReservation.HasKey(x => x.UgcCustomerOfferId);
        offerReservation.HasOne<UgcCustomerOffer>().WithOne().HasForeignKey<UgcCustomerOfferReservation>(x => x.UgcCustomerOfferId).OnDelete(DeleteBehavior.Restrict);
        offerReservation.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        offerReservation.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);

        var offerBudget = model.Entity<UgcCustomerOfferBudgetEntry>(); Mapping.Scalars(offerBudget); offerBudget.HasKey(x => x.Id);
        offerBudget.ToTable("UgcCustomerOfferBudgetEntries", t => t.HasCheckConstraint("CK_UgcCustomerOfferBudgetEntry_Positive", "\"Amount\" > 0"));
        offerBudget.HasOne<UgcCustomerOffer>().WithMany().HasForeignKey(x => x.UgcCustomerOfferId).OnDelete(DeleteBehavior.Restrict);
        offerBudget.HasOne<UgcCustomerOfferSale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        offerBudget.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        offerBudget.HasIndex(x => new { x.UgcCustomerOfferId, x.CreatedAtUtc });

        var idem = model.Entity<StoredIdempotencyRecord>(); Mapping.Scalars(idem); idem.ToTable("IdempotencyRecords");
        idem.HasKey(x => new { x.ActorId, x.OperationType, x.Key });
        idem.Property(x => x.Key).HasMaxLength(200); idem.Property(x => x.OperationType).HasMaxLength(100);
        var audit = model.Entity<AuditEvent>(); Mapping.Scalars(audit); audit.HasKey(x => x.Id); audit.ToTable("AuditEvents");
        audit.HasIndex(x => x.CorrelationId); audit.HasIndex(x => new { x.PromotionId, x.OccurredAtUtc });
        audit.HasIndex(x => new { x.UgcOpportunityId, x.OccurredAtUtc });
        audit.HasIndex(x => new { x.UgcCustomerOfferId, x.OccurredAtUtc });
        audit.HasIndex(x => new { x.TargetUserId, x.OccurredAtUtc });
        audit.HasIndex(x => new { x.SupportSessionId, x.OccurredAtUtc });
        var outbox = model.Entity<OutboxMessage>(); Mapping.Scalars(outbox); outbox.HasKey(x => x.Id); outbox.ToTable("OutboxMessages");
        outbox.Property(x => x.Payload).HasColumnType("jsonb");
        outbox.HasIndex(x => x.OccurredAtUtc).HasFilter("\"ProcessedAtUtc\" IS NULL");
        var config = model.Entity<FinancialConfiguration>(); Mapping.Scalars(config); config.HasKey(x => x.Id);
        config.ToTable("FinancialConfigurations"); config.HasIndex(x => x.Name).IsUnique();
        var legal = model.Entity<LegalDocumentVersion>(); Mapping.Scalars(legal); legal.HasKey(x => x.Id);
        legal.ToTable("LegalDocumentVersions"); legal.HasIndex(x => new { x.Type, x.Version }).IsUnique();
        legal.HasIndex(x => new { x.Type, x.EffectiveFromUtc });
        var acceptance = model.Entity<LegalAcceptance>(); Mapping.Scalars(acceptance); acceptance.ToTable("LegalAcceptances");
        acceptance.HasKey(x => new { x.UserId, x.Role, x.DocumentVersionId });
        acceptance.HasOne<LegalDocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
