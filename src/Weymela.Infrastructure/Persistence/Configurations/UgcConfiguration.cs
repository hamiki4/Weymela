using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class UgcOpportunityConfiguration : IEntityTypeConfiguration<UgcOpportunity>
{
    public void Configure(EntityTypeBuilder<UgcOpportunity> b)
    {
        b.ToTable("UgcOpportunities", t =>
        {
            t.HasCheckConstraint("CK_UgcOpportunity_Capacity", "\"CreatorCapacity\" > 0 AND \"ApprovedCreatorCount\" >= 0 AND \"ApprovedCreatorCount\" <= \"CreatorCapacity\"");
            t.HasCheckConstraint("CK_UgcOpportunity_Funding", "\"RequiredFunding\" > 0 AND \"ReservedFunding\" >= 0 AND \"UsedFunding\" >= 0 AND \"ReservedFunding\" + \"UsedFunding\" <= \"RequiredFunding\"");
        });
        Mapping.Scalars(b, "PlatformFee", "PerAssignmentFee", "RemainingFunding", "PlatformRequirements");
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.BusinessId, x.Status });
        b.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsOne(x => x.PricingSnapshot, p =>
        {
            p.Ignore(x => x.IsValid); Mapping.Money(p.Property(x => x.MinimumCreatorPayment));
            p.Property(x => x.PlatformFeePercent).HasPrecision(9, 4);
            Mapping.Money(p.Property(x => x.MinimumUgcBudget), true);
            p.Property(x => x.CustomerOfferPlatformSalePercent).HasPrecision(9, 4);
            p.Property(x => x.MaximumCustomerDiscountPercent).HasPrecision(9, 4);
            p.Property(x => x.EffectiveFromUtc); p.Property(x => x.ConfigurationVersionId);
        });
        b.HasMany(x => x.PlatformRequirements).WithOne().HasForeignKey(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.PlatformRequirements).HasField("platformRequirements").UsePropertyAccessMode(PropertyAccessMode.Field);
        Mapping.Version(b);
    }
}

internal sealed class UgcCustomerOfferConfiguration : IEntityTypeConfiguration<UgcCustomerOffer>
{
    public void Configure(EntityTypeBuilder<UgcCustomerOffer> b)
    {
        b.ToTable("UgcCustomerOffers", t =>
        {
            t.HasCheckConstraint("CK_UgcCustomerOffer_Percent", "\"CustomerDiscountPercent\" > 0 AND \"CustomerDiscountPercent\" <= 100");
            t.HasCheckConstraint("CK_UgcCustomerOffer_Funding", "\"FundedLimit\" > 0 AND \"ReservedFunding\" >= 0 AND \"UsedFunding\" >= 0 AND \"ReservedFunding\" + \"UsedFunding\" <= \"FundedLimit\"");
            t.HasCheckConstraint("CK_UgcCustomerOffer_Dates", "\"EndsAtUtc\" > \"StartsAtUtc\"");
        });
        Mapping.Scalars(b, "RemainingFunding"); b.HasKey(x => x.Id);
        b.HasIndex(x => x.UgcOpportunityId).IsUnique(); b.HasIndex(x => new { x.BusinessId, x.Status });
        b.HasOne<UgcOpportunity>().WithOne().HasForeignKey<UgcCustomerOffer>(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessWallet>().WithMany().HasForeignKey(x => x.BusinessId).HasPrincipalKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsOne(x => x.PricingSnapshot, p =>
        {
            p.Ignore(x => x.IsValid); p.Property(x => x.PlatformSalePercent).HasPrecision(9, 4);
            p.Property(x => x.EffectiveFromUtc); p.Property(x => x.ConfigurationVersionId);
        });
        Mapping.Version(b);
    }
}

internal sealed class UgcCustomerOfferSaleConfiguration : IEntityTypeConfiguration<UgcCustomerOfferSale>
{
    public void Configure(EntityTypeBuilder<UgcCustomerOfferSale> b)
    {
        b.ToTable("UgcCustomerOfferSales", t => t.HasCheckConstraint("CK_UgcCustomerOfferSale_Amounts",
            "\"PurchaseAmount\" > 0 AND \"CustomerDiscountAmount\" > 0 AND \"CustomerPaysAmount\" >= 0 AND \"PlatformRevenueAmount\" >= 0 AND \"CustomerPaysAmount\" + \"CustomerDiscountAmount\" = \"PurchaseAmount\" AND \"TotalOfferCharge\" = \"CustomerDiscountAmount\" + \"PlatformRevenueAmount\""));
        Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<UgcCustomerOffer>().WithMany().HasForeignKey(x => x.UgcCustomerOfferId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UgcOpportunity>().WithMany().HasForeignKey(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique();
        b.Property<Guid>("JournalId"); b.HasOne<FinancialJournal>().WithMany().HasForeignKey("JournalId").OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UgcPlatformRequirementConfiguration : IEntityTypeConfiguration<UgcPlatformRequirement>
{
    public void Configure(EntityTypeBuilder<UgcPlatformRequirement> b)
    {
        b.ToTable("UgcPlatformRequirements", t => t.HasCheckConstraint("CK_UgcPlatformRequirement_Audience", "\"MinimumAudience\" IS NULL OR \"MinimumAudience\" >= 0"));
        Mapping.Scalars(b); b.HasKey(x => x.Id); b.HasIndex(x => new { x.UgcOpportunityId, x.Platform }).IsUnique();
    }
}

internal sealed class UgcRevisionConfiguration : IEntityTypeConfiguration<UgcRevision>
{
    public void Configure(EntityTypeBuilder<UgcRevision> b)
    {
        b.ToTable("UgcRevisions"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<UgcOpportunity>().WithMany().HasForeignKey(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UgcOpportunityId, x.RevisionNumber }).IsUnique(); b.Property(x => x.SnapshotJson).HasColumnType("jsonb");
    }
}

internal sealed class UgcCreatorRequestConfiguration : IEntityTypeConfiguration<UgcCreatorRequest>
{
    public void Configure(EntityTypeBuilder<UgcCreatorRequest> b)
    {
        b.ToTable("UgcCreatorRequests"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<UgcOpportunity>().WithMany().HasForeignKey(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UgcOpportunityId, x.CreatorId }).IsUnique().HasFilter("\"Status\" IN ('Pending','Approved')");
        b.HasIndex(x => new { x.UgcOpportunityId, x.Status });
    }
}

internal sealed class UgcAssignmentConfiguration : IEntityTypeConfiguration<UgcAssignment>
{
    public void Configure(EntityTypeBuilder<UgcAssignment> b)
    {
        b.ToTable("UgcAssignments"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<UgcOpportunity>().WithMany().HasForeignKey(x => x.UgcOpportunityId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UgcCreatorRequest>().WithOne().HasForeignKey<UgcAssignment>(x => x.UgcCreatorRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UgcOpportunityId, x.CreatorId }).IsUnique(); Mapping.Version(b);
    }
}

internal sealed class UgcSubmissionConfiguration : IEntityTypeConfiguration<UgcSubmission>
{
    public void Configure(EntityTypeBuilder<UgcSubmission> b)
    {
        b.ToTable("UgcSubmissions"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasOne<UgcAssignment>().WithMany().HasForeignKey(x => x.UgcAssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UgcAssignmentId, x.SubmittedAtUtc });
    }
}

internal sealed class CreatorSocialProfileRecordConfiguration : IEntityTypeConfiguration<CreatorSocialProfileRecord>
{
    public void Configure(EntityTypeBuilder<CreatorSocialProfileRecord> b)
    {
        b.ToTable("CreatorSocialProfiles", t => t.HasCheckConstraint("CK_CreatorSocialProfile_Audience", "\"SelfReportedAudience\" >= 0 AND (\"VerifiedAudience\" IS NULL OR \"VerifiedAudience\" >= 0)"));
        Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CreatorId, x.Platform }).IsUnique().HasFilter("\"IsActive\""); Mapping.Version(b);
    }
}

internal sealed class AdminGrantRecordConfiguration : IEntityTypeConfiguration<AdminGrantRecord>
{
    public void Configure(EntityTypeBuilder<AdminGrantRecord> b)
    {
        b.ToTable("AdminGrants"); Mapping.Scalars(b); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.Role }).IsUnique().HasFilter("\"IsActive\""); Mapping.Version(b);
    }
}
