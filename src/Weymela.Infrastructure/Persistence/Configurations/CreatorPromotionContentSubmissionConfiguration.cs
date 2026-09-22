using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class CreatorPromotionContentSubmissionConfiguration
    : IEntityTypeConfiguration<CreatorPromotionContentSubmission>
{
    public void Configure(EntityTypeBuilder<CreatorPromotionContentSubmission> b)
    {
        b.ToTable("CreatorPromotionContentSubmissions", t =>
        {
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_Revision", "\"RevisionNumber\" > 0");
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_Provider", "\"Provider\" IN ('TikTok','YouTube','Instagram')");
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_Reference", "length(trim(\"ContentReference\")) > 0 AND length(\"ContentReference\") <= 100");
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_Review", "(\"ReviewStatus\" = 'UnderReview' AND \"ReviewedAtUtc\" IS NULL AND \"ReviewedByUserId\" IS NULL) OR (\"ReviewStatus\" IN ('ChangesRequested','Approved','Rejected') AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedByUserId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_Status", "\"ReviewStatus\" IN ('UnderReview','ChangesRequested','Approved','Rejected')");
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_Feedback", "\"Feedback\" IS NULL OR length(\"Feedback\") <= 2000");
            t.HasCheckConstraint("CK_CreatorPromotionContentSubmission_ChangesFeedback", "\"ReviewStatus\" <> 'ChangesRequested' OR length(trim(coalesce(\"Feedback\",''))) > 0");
        });
        Mapping.Scalars(b);
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(64);
        b.Property(x => x.ContentReference).HasMaxLength(100);
        b.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(64);
        b.Property(x => x.Feedback).HasMaxLength(2000);
        b.HasOne<CreatorAllocation>().WithMany().HasForeignKey(x => x.CreatorAllocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CreatorAllocationId, x.RevisionNumber }).IsUnique();
        b.HasIndex(x => new { x.CreatorAllocationId, x.SubmittedAtUtc });
        Mapping.Version(b);
    }
}
