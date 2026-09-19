using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class FinancialConfigurationMapping : IEntityTypeConfiguration<FinancialConfigurationVersion>
{
    public void Configure(EntityTypeBuilder<FinancialConfigurationVersion> b)
    {
        b.ToTable("FinancialConfigurationVersions"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<FinancialConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ConfigurationId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.EffectiveFromUtc, x.Version });
        b.OwnsOne(x => x.ViewOnly, Mapping.Pricing);
        b.OwnsOne(x => x.ViewPlusCommission, Mapping.Pricing);
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
