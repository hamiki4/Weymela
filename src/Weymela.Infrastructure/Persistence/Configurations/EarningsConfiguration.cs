using Microsoft.EntityFrameworkCore;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal static class EarningsConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var creators = model.Entity<CreatorEarningsAccount>();
        Mapping.Scalars(creators); creators.HasKey(x => x.CreatorId);
        creators.ToTable("CreatorEarningsAccounts", t => t.HasCheckConstraint("CK_CreatorEarnings_NonNegative", "\"AvailableEarnings\" >= 0"));
        creators.Property<uint>("xmin").IsRowVersion();
        creators.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
        var ce = model.Entity<CreatorEarningEntry>(); Mapping.Scalars(ce); ce.HasKey(x => x.Id);
        ce.ToTable("CreatorEarningEntries", t => t.HasCheckConstraint("CK_CreatorEarning_Positive", "\"Amount\" > 0"));
        ce.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        ce.HasOne<UgcAssignment>().WithMany().HasForeignKey(x => x.UgcAssignmentId).OnDelete(DeleteBehavior.Restrict);
        JournalLink<CreatorEarningEntry>(model);

        var customers = model.Entity<CustomerCashbackAccount>();
        Mapping.Scalars(customers); customers.HasKey(x => x.CustomerId);
        customers.ToTable("CustomerCashbackAccounts", t => t.HasCheckConstraint("CK_Cashback_NonNegative", "\"AvailableCashback\" >= 0"));
        customers.Property<uint>("xmin").IsRowVersion();
        customers.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        var cb = model.Entity<CustomerCashbackEntry>(); Mapping.Scalars(cb); cb.HasKey(x => x.Id);
        cb.ToTable("CustomerCashbackEntries", t => t.HasCheckConstraint("CK_Cashback_PositiveSale", "\"Amount\" > 0 AND (\"Source\" <> 'VerifiedSale' OR \"VerifiedSaleId\" IS NOT NULL)"));
        cb.HasOne<VerifiedSale>().WithMany().HasForeignKey(x => x.VerifiedSaleId).OnDelete(DeleteBehavior.Restrict);
        JournalLink<CustomerCashbackEntry>(model);

        var revenue = model.Entity<PlatformRevenueEntry>(); Mapping.Scalars(revenue); revenue.HasKey(x => x.Id);
        revenue.ToTable("PlatformRevenueEntries", t => t.HasCheckConstraint("CK_Revenue_Positive", "\"Amount\" > 0"));
        revenue.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        revenue.HasOne<UgcAssignment>().WithMany().HasForeignKey(x => x.UgcAssignmentId).OnDelete(DeleteBehavior.Restrict);
        revenue.HasIndex(x => new { x.Source, x.CreatedAtUtc });
        JournalLink<PlatformRevenueEntry>(model);
        var settlement = model.Entity<PlatformSettlement>(); Mapping.Scalars(settlement); settlement.HasKey(x => x.Id);
        settlement.ToTable("PlatformSettlements", t => t.HasCheckConstraint("CK_Settlement_Positive", "\"Amount\" > 0"));
        settlement.HasIndex(x => x.Reference).IsUnique(); JournalLink<PlatformSettlement>(model);

        var sale = model.Entity<VerifiedSale>(); Mapping.Scalars(sale); sale.HasKey(x => x.Id);
        sale.ToTable("VerifiedSales", t => t.HasCheckConstraint("CK_Sale_Amounts", "\"PurchaseAmount\" > 0 AND \"CreatorCommissionAmount\" >= 0 AND \"CustomerCashbackAmount\" >= 0 AND \"PlatformRevenueAmount\" >= 0 AND \"TotalPromotionCharge\" = \"CreatorCommissionAmount\" + \"CustomerCashbackAmount\" + \"PlatformRevenueAmount\""));
        sale.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId })
            .HasPrincipalKey(x => new { x.Id, x.PromotionId, x.CreatorId }).OnDelete(DeleteBehavior.Restrict);
        sale.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique();
        JournalLink<VerifiedSale>(model);

        var views = model.Entity<PromotionViewVerification>(); Mapping.Scalars(views, "IsValid");
        views.ToTable("PromotionViewVerifications", t => t.HasCheckConstraint("CK_Views_Valid", "\"PreviousVerifiedViews\" >= 0 AND \"CurrentVerifiedViews\" >= \"PreviousVerifiedViews\" AND \"RewardedViewCount\" >= 0"));
        views.Property<Guid>("Id").ValueGeneratedOnAdd(); views.HasKey("Id");
        views.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId })
            .HasPrincipalKey(x => new { x.Id, x.PromotionId, x.CreatorId }).OnDelete(DeleteBehavior.Restrict);
        views.HasIndex(x => x.IdempotencyKey).IsUnique();
    }

    private static void JournalLink<T>(ModelBuilder model) where T : class
    {
        var b = model.Entity<T>(); b.Property<Guid>("JournalId");
        b.HasOne<FinancialJournal>().WithMany().HasForeignKey("JournalId").OnDelete(DeleteBehavior.Restrict);
    }
}
