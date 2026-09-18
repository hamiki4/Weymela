using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class JournalConfiguration : IEntityTypeConfiguration<FinancialJournal>
{
    public void Configure(EntityTypeBuilder<FinancialJournal> b)
    {
        b.ToTable("FinancialJournals", t => t.HasCheckConstraint("CK_Journal_Posted", "\"IsPosted\""));
        Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => x.CorrelationId); b.HasIndex(x => x.SourceType); b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => x.IdempotencyReference);
        foreach (var name in new[] { "BusinessId", "PromotionId", "CreatorId", "CustomerId", "UgcOpportunityId", "UgcAssignmentId" })
        {
            b.Property<Guid?>(name); b.HasIndex(name);
        }
        b.HasOne<Promotion>().WithMany().HasForeignKey("PromotionId").OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey("JournalId").OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).HasField("lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<FinancialJournalLine>
{
    public void Configure(EntityTypeBuilder<FinancialJournalLine> b)
    {
        b.ToTable("FinancialJournalLines", t => t.HasCheckConstraint("CK_JournalLine_Positive", "\"Amount\" > 0 AND \"Type\" IN ('Debit', 'Credit')"));
        Mapping.Scalars(b); b.Property<Guid>("Id").ValueGeneratedOnAdd(); b.HasKey("Id");
        b.Property<Guid>("JournalId"); b.HasIndex("JournalId"); b.HasIndex(x => x.Account);
    }
}
