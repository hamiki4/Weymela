using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("Promotions", t =>
        {
            t.HasCheckConstraint("CK_Promotion_Budgets", "\"TotalBudget\" > 0 AND \"ReservedBudget\" >= 0 AND \"UsedBudget\" >= 0 AND \"ReservedBudget\" + \"UsedBudget\" <= \"TotalBudget\" AND \"AllocatedBudget\" >= \"UsedBudget\" AND \"AllocatedBudget\" <= \"TotalBudget\"");
            t.HasCheckConstraint("CK_Promotion_Dates", "\"EndDateUtc\" > \"StartDateUtc\"");
        });
        Mapping.Scalars(b, "AllocatedBudget", "RemainingBudget", "UnallocatedBudget", "DomainEvents", "Platforms");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.PublicPromotionId).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.Status });
        b.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.Property<decimal>("StoredAllocatedBudget").HasColumnName("AllocatedBudget").HasPrecision(18, 2);
        b.Property<decimal>("StoredRemainingBudget").HasColumnName("RemainingBudget").HasPrecision(18, 2).HasComputedColumnSql("\"TotalBudget\" - \"UsedBudget\"", true);
        b.OwnsOne(x => x.Eligibility);
        b.OwnsOne(x => x.PricingSnapshot, s => { s.ToTable("PricingSnapshots"); Mapping.Pricing(s); });
        b.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Allocations).HasField("allocations").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Platforms).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Platforms).HasField("platforms").UsePropertyAccessMode(PropertyAccessMode.Field);
        Mapping.Version(b);
    }
}

internal sealed class PromotionPlatformConfiguration : IEntityTypeConfiguration<PromotionPlatform>
{
    public void Configure(EntityTypeBuilder<PromotionPlatform> b)
    {
        b.ToTable("PromotionPlatforms", t =>
        {
            t.HasCheckConstraint("CK_PromotionPlatform_Capacity", "\"Capacity\" > 0 AND \"ApprovedCount\" >= 0 AND \"ApprovedCount\" <= \"Capacity\"");
        });
        Mapping.Scalars(b, "Available"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.PromotionId, x.Platform }).IsUnique();
        Mapping.Version(b);
    }
}

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<CreatorApplication>
{
    public void Configure(EntityTypeBuilder<CreatorApplication> b)
    {
        b.ToTable("CreatorApplications"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PromotionId, x.CreatorId }).IsUnique().HasFilter("\"Status\" IN ('Pending', 'Approved')");
        b.HasIndex(x => new { x.PromotionId, x.Status });
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class AllocationConfiguration : IEntityTypeConfiguration<CreatorAllocation>
{
    public void Configure(EntityTypeBuilder<CreatorAllocation> b)
    {
        b.ToTable("CreatorAllocations", t => t.HasCheckConstraint("CK_Allocation_Budgets", "\"OriginalAllocation\" > 0 AND \"UsedAmount\" >= 0 AND \"UsedAmount\" <= \"OriginalAllocation\""));
        Mapping.Scalars(b, "RemainingAmount"); b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.PromotionId, x.CreatorId });
        b.HasIndex(x => new { x.PromotionId, x.CreatorId }).IsUnique();
        b.Property<decimal>("StoredRemainingAmount").HasColumnName("RemainingAmount").HasPrecision(18, 2)
            .HasComputedColumnSql("\"OriginalAllocation\" - \"UsedAmount\"", true);
        Mapping.Version(b);
    }
}
