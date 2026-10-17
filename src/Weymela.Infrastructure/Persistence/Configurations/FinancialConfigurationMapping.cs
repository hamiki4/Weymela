using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class FinancialConfigurationMapping : IEntityTypeConfiguration<FinancialConfigurationVersion>
{
    public void Configure(EntityTypeBuilder<FinancialConfigurationVersion> b)
    {
        b.ToTable("FinancialConfigurationVersions", t => {
            t.HasCheckConstraint("CK_Configuration_PromotionLiveDuration",
                $"\"PromotionLiveDurationDays\" BETWEEN {PromotionLiveDurationPolicy.MinimumDays} AND {PromotionLiveDurationPolicy.MaximumDays}");
            t.HasCheckConstraint("CK_Configuration_OptionalPayoutMinimums", "(\"CreatorPayoutThreshold\" IS NULL OR \"CreatorPayoutThreshold\">0) AND (\"CustomerPayoutThreshold\" IS NULL OR \"CustomerPayoutThreshold\">0)");
            t.HasCheckConstraint("CK_Configuration_ApprovedSaleRates", "(\"ApprovedCreatorCommissionPercent\" IS NOT NULL AND \"ApprovedCustomerCashbackPercent\" IS NOT NULL AND \"ApprovedPlatformPercent\" IS NOT NULL AND \"ApprovedCreatorCommissionPercent\" BETWEEN 0 AND 100 AND \"ApprovedCustomerCashbackPercent\" BETWEEN 0 AND 100 AND \"ApprovedPlatformPercent\" BETWEEN 0 AND 100 AND \"ApprovedCreatorCommissionPercent\"+\"ApprovedCustomerCashbackPercent\"+\"ApprovedPlatformPercent\"<=100) OR (\"ApprovedCreatorCommissionPercent\" IS NULL AND \"ApprovedCustomerCashbackPercent\" IS NULL AND \"ApprovedPlatformPercent\" IS NULL AND \"ViewPlusCommission_CreatorCommissionPercent\" IS NOT NULL AND \"ViewPlusCommission_CustomerCashbackPercent\" IS NOT NULL AND \"ViewPlusCommission_PlatformPercent\" IS NOT NULL)");
        });
        Mapping.Scalars(b, nameof(FinancialConfigurationVersion.CreatorCommissionPercent),
            nameof(FinancialConfigurationVersion.CustomerCashbackPercent), nameof(FinancialConfigurationVersion.PlatformPercent)); b.HasKey(x => x.Id);
        b.HasOne<FinancialConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ConfigurationId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.EffectiveFromUtc, x.Version });
        b.Property(x => x.LowPromotionBalanceAlert).HasPrecision(18, 2);
        b.OwnsOne(x => x.ViewOnly, Mapping.Pricing);
        b.OwnsOne(x => x.ViewPlusCommission, Mapping.Pricing);
        b.Navigation(x => x.ViewOnly).IsRequired(false);
        b.Navigation(x => x.ViewPlusCommission).IsRequired(false);
        b.OwnsOne(x => x.Ugc, ugc =>
        {
            ugc.Ignore(x => x.IsValid);
            Mapping.Money(ugc.Property(x => x.MinimumCreatorPayment));
            ugc.Property(x => x.PlatformFeePercent).HasPrecision(9, 4);
            Mapping.Money(ugc.Property(x => x.MinimumUgcBudget), true);
            ugc.Property(x => x.CustomerOfferPlatformSalePercent).HasPrecision(9, 4);
            ugc.Property(x => x.EffectiveFromUtc);
            ugc.Property(x => x.ConfigurationVersionId);
        });
        b.Navigation(x => x.Ugc).IsRequired(false);
    }
}
