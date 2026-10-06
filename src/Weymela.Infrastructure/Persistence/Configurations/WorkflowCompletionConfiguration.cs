using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class PrivateReviewMediaAssetConfiguration : IEntityTypeConfiguration<PrivateReviewMediaAsset>
{
    public void Configure(EntityTypeBuilder<PrivateReviewMediaAsset> b)
    {
        b.ToTable("PrivateReviewMediaAssets", t =>
        {
            t.HasCheckConstraint("CK_ReviewMedia_Work", "(\"CreatorAllocationId\" IS NOT NULL AND \"UgcAssignmentId\" IS NULL) OR (\"CreatorAllocationId\" IS NULL AND \"UgcAssignmentId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_ReviewMedia_Revision", "\"RevisionNumber\" > 0");
            t.HasCheckConstraint("CK_ReviewMedia_Key", "\"StorageKey\" ~ '^m_[0-9a-f]{64}$'");
            t.HasCheckConstraint("CK_ReviewMedia_Type", "\"ContentType\" IN ('video/mp4','image/jpeg','image/png')");
            t.HasCheckConstraint("CK_ReviewMedia_Length", "\"Length\" > 0");
            t.HasCheckConstraint("CK_ReviewMedia_Digest", "\"Sha256\" ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("CK_ReviewMedia_State", "\"State\" IN ('Active','Retained')");
        });
        Mapping.Scalars(b);
        b.HasKey(x => x.Id);
        b.Property(x => x.StorageKey).HasMaxLength(66);
        b.Property(x => x.ContentType).HasMaxLength(32);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.Property(x => x.OriginalFileName).HasMaxLength(120);
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        b.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => x.CreatorAllocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UgcAssignment>().WithMany().HasForeignKey(x => x.UgcAssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => new { x.CreatorAllocationId, x.RevisionNumber }).IsUnique().HasFilter("\"CreatorAllocationId\" IS NOT NULL");
        b.HasIndex(x => new { x.UgcAssignmentId, x.RevisionNumber }).IsUnique().HasFilter("\"UgcAssignmentId\" IS NOT NULL");
    }
}

internal sealed class CreatorPublicationVerificationConfiguration
    : IEntityTypeConfiguration<CreatorPublicationVerification>
{
    public void Configure(EntityTypeBuilder<CreatorPublicationVerification> b)
    {
        b.ToTable("CreatorPublicationVerifications", t =>
        {
            t.HasCheckConstraint("CK_Publication_Work", "(\"CreatorAllocationId\" IS NOT NULL AND \"UgcAssignmentId\" IS NULL AND \"PromotionContentSubmissionId\" IS NOT NULL AND \"UgcSubmissionId\" IS NULL) OR (\"CreatorAllocationId\" IS NULL AND \"UgcAssignmentId\" IS NOT NULL AND \"PromotionContentSubmissionId\" IS NULL AND \"UgcSubmissionId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_Publication_Provider", "\"Provider\" IN ('TikTok','YouTube','Instagram')");
            t.HasCheckConstraint("CK_Publication_Content", "length(trim(\"ExternalContentId\")) > 0 AND length(\"ExternalContentId\") <= 100");
            t.HasCheckConstraint("CK_Publication_Status", "\"Status\" IN ('VerificationPending','Verified','Failed','Expired')");
            t.HasCheckConstraint("CK_Publication_Verification", "(\"Status\" = 'Verified' AND \"VerifiedAtUtc\" IS NOT NULL AND \"EvidenceReference\" IS NOT NULL AND \"VerificationMethod\" IS NOT NULL) OR \"Status\" <> 'Verified'");
            t.HasCheckConstraint("CK_Publication_GoLive", "\"WentLiveAtUtc\" IS NULL OR \"VerifiedAtUtc\" IS NOT NULL");
            t.HasCheckConstraint("CK_Publication_Baseline", "\"BaselineViews\" IS NULL OR \"BaselineViews\" >= 0");
        });
        Mapping.Scalars(b);
        b.Ignore(x => x.IsLive);
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(32);
        b.Property(x => x.ExternalContentId).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.VerificationMethod).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.EvidenceReference).HasMaxLength(500);
        b.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => x.CreatorAllocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UgcAssignment>().WithMany().HasForeignKey(x => x.UgcAssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CreatorPromotionContentSubmission>().WithMany().HasForeignKey(x => x.PromotionContentSubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UgcSubmission>().WithMany().HasForeignKey(x => x.UgcSubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CreatorSocialProfileRecord>().WithMany().HasForeignKey(x => x.CreatorSocialProfileId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CreatorAllocationId).IsUnique().HasFilter("\"CreatorAllocationId\" IS NOT NULL AND \"Status\" IN ('VerificationPending','Verified')");
        b.HasIndex(x => x.UgcAssignmentId).IsUnique().HasFilter("\"UgcAssignmentId\" IS NOT NULL AND \"Status\" IN ('VerificationPending','Verified')");
        b.HasIndex(x => new { x.Status, x.RequestedAtUtc });
        Mapping.Version(b);
    }
}
