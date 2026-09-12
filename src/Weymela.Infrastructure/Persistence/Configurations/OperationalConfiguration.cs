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
        var budget = model.Entity<PromotionBudgetEntry>(); Mapping.Scalars(budget); budget.HasKey(x => x.Id);
        budget.ToTable("PromotionBudgetEntries", t => t.HasCheckConstraint("CK_BudgetEntry_Positive", "\"Amount\" > 0"));
        budget.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        budget.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);
        budget.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        budget.HasIndex(x => new { x.PromotionId, x.CreatedAtUtc });

        var idem = model.Entity<StoredIdempotencyRecord>(); Mapping.Scalars(idem); idem.ToTable("IdempotencyRecords");
        idem.HasKey(x => new { x.ActorId, x.OperationType, x.Key });
        idem.Property(x => x.Key).HasMaxLength(200); idem.Property(x => x.OperationType).HasMaxLength(100);
        var audit = model.Entity<AuditEvent>(); Mapping.Scalars(audit); audit.HasKey(x => x.Id); audit.ToTable("AuditEvents");
        audit.HasIndex(x => x.CorrelationId); audit.HasIndex(x => new { x.PromotionId, x.OccurredAtUtc });
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
