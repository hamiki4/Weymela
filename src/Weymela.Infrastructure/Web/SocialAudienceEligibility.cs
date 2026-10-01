using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Web;

public static class SocialAudienceEligibility
{
    public const string SelfReported = "SelfReported";
    public const string AdminVerified = "AdminVerified";

    public static bool Matches(CreatorSocialProfileRecord profile, CreatorPlatform platform,
        long? minimumAudience, bool enforceAudienceRequirements)
    {
        if (!profile.IsActive || profile.Platform != platform) return false;
        if (!enforceAudienceRequirements) return true;
        return profile.AudienceVerificationSource == AdminVerified
            && (minimumAudience is null or <= 0 || profile.VerifiedAudience is { } verified && verified >= minimumAudience);
    }

    public static bool Matches(CreatorSocialProfileRecord profile, UgcPlatformRequirement requirement,
        bool enforceAudienceRequirements) => Matches(profile, requirement.Platform, requirement.MinimumAudience, enforceAudienceRequirements);
}
