using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal static class Phase4Configuration
{
    public static void Configure(ModelBuilder model)
    {
        var participation = model.Entity<CreatorPromotionParticipation>(); Mapping.Scalars(participation, "CampaignVerifiedViews");
        participation.ToTable("CreatorPromotionParticipations", t => t.HasCheckConstraint("CK_Participation_Views",
            "\"BaselineViews\" >= 0 AND \"LatestVerifiedViews\" >= \"BaselineViews\" AND \"RewardedViewCount\" >= 0 AND \"RewardedViewCount\" <= \"LatestVerifiedViews\" - \"BaselineViews\""));
        participation.HasKey(x => x.Id); participation.HasIndex(x => x.CreatorAllocationId).IsUnique();
        participation.HasIndex(x => new { x.Provider, x.ExternalContentId }).IsUnique();
        participation.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId })
            .HasPrincipalKey(x => new { x.Id, x.PromotionId, x.CreatorId }).OnDelete(DeleteBehavior.Restrict);
        Mapping.Version(participation);
        var views = model.Entity<PromotionViewVerification>();
        views.HasOne<CreatorPromotionParticipation>().WithMany().HasForeignKey(x => x.ParticipationId).OnDelete(DeleteBehavior.Restrict);
        var reward = model.Entity<ViewRewardReceipt>(); Mapping.Scalars(reward); reward.HasKey(x => x.Id);
        reward.ToTable("ViewRewardReceipts", t => t.HasCheckConstraint("CK_ViewReward_Amounts",
            "\"Blocks\" > 0 AND \"RewardedThrough\" > 0 AND \"BusinessCharge\" > 0 AND \"BusinessCharge\" = \"CreatorEarning\" + \"PlatformEarning\""));
        reward.HasOne<CreatorPromotionParticipation>().WithMany().HasForeignKey(x => x.ParticipationId).OnDelete(DeleteBehavior.Restrict);
        reward.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        reward.HasIndex(x => new { x.ParticipationId, x.RewardedThrough }).IsUnique();
        var qr = model.Entity<OfferQrSession>(); Mapping.Scalars(qr); qr.HasKey(x => x.Id);
        qr.ToTable("OfferQrSessions", t =>
        {
            t.HasCheckConstraint("CK_Qr_Expiry", "\"ExpiresAtUtc\" = \"IssuedAtUtc\" + INTERVAL '5 minutes'");
            t.HasCheckConstraint("CK_Qr_SourceBinding", "(\"Source\" = 'ViewAndSalePromotion' AND \"PromotionId\" IS NOT NULL AND \"CreatorId\" IS NOT NULL AND \"CreatorAllocationId\" IS NOT NULL AND \"UgcCustomerOfferId\" IS NULL) OR (\"Source\" = 'UgcCustomerOffer' AND \"PromotionId\" IS NULL AND \"CreatorId\" IS NULL AND \"CreatorAllocationId\" IS NULL AND \"UgcCustomerOfferId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_Qr_SaleBinding", "(\"SaleId\" IS NULL OR \"UgcCustomerOfferSaleId\" IS NULL) AND (\"Status\" <> 'Used' OR ((\"Source\" = 'ViewAndSalePromotion' AND \"SaleId\" IS NOT NULL AND \"UgcCustomerOfferSaleId\" IS NULL) OR (\"Source\" = 'UgcCustomerOffer' AND \"SaleId\" IS NULL AND \"UgcCustomerOfferSaleId\" IS NOT NULL)))");
        });
        qr.HasIndex(x => x.TokenHash).IsUnique(); qr.Property(x => x.TokenHash).HasMaxLength(64);
        qr.HasIndex(x => x.SaleId).IsUnique(); qr.HasIndex(x => x.UgcCustomerOfferSaleId).IsUnique();
        qr.HasIndex(x => new { x.CustomerId, x.IssuedAtUtc });
        qr.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId })
            .HasPrincipalKey(x => new { x.Id, x.PromotionId, x.CreatorId }).OnDelete(DeleteBehavior.Restrict);
        qr.HasOne<VerifiedSale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        qr.HasOne<UgcCustomerOffer>().WithMany().HasForeignKey(x => x.UgcCustomerOfferId).OnDelete(DeleteBehavior.Restrict);
        qr.HasOne<UgcCustomerOfferSale>().WithMany().HasForeignKey(x => x.UgcCustomerOfferSaleId).OnDelete(DeleteBehavior.Restrict);
        Mapping.Version(qr);
        var payout = model.Entity<PayoutRecord>(); Mapping.Scalars(payout, "BeneficiaryId"); payout.HasKey(x => x.Id);
        payout.ToTable("PayoutRecords", t => t.HasCheckConstraint("CK_Payout_Amounts",
            "\"Amount\" > 0 AND \"Amount\" = \"ThresholdUsed\" AND ((\"Beneficiary\"='Creator' AND \"CreatorId\" IS NOT NULL AND \"CustomerId\" IS NULL) OR (\"Beneficiary\"='Customer' AND \"CustomerId\" IS NOT NULL AND \"CreatorId\" IS NULL))"));
        payout.HasOne<CreatorEarningsAccount>().WithMany().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
        payout.HasOne<CustomerCashbackAccount>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        payout.HasOne<FinancialConfigurationVersion>().WithMany().HasForeignKey(x => x.ConfigurationVersionId).OnDelete(DeleteBehavior.Restrict);
        payout.HasOne<FinancialJournal>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        payout.HasIndex(x => x.CreatorId).IsUnique().HasFilter("\"Status\"='Eligible' AND \"CreatorId\" IS NOT NULL");
        payout.HasIndex(x => x.CustomerId).IsUnique().HasFilter("\"Status\"='Eligible' AND \"CustomerId\" IS NOT NULL");
        payout.HasIndex(x => x.Reference).IsUnique().HasFilter("\"Reference\" IS NOT NULL");
        Mapping.Version(payout);
        var permission = model.Entity<CommercePermission>(); Mapping.Scalars(permission); permission.ToTable("CommercePermissions");
        permission.HasKey(x => new { x.UserId, x.Role, x.SubjectId });
        permission.HasIndex(x => new { x.SubjectId, x.Role, x.IsActive });
        model.Entity<PlatformSettlement>().Property<Guid?>("SettledBy");
    }
}
