using Weymela.Domain;

namespace Weymela.Application;

public sealed class CreatorEligibility : ICreatorEligibility
{
    public bool IsEligible(Guid creatorId, CreatorEligibilityCriteria criteria, CreatorVerifiedProfile profile) =>
        creatorId != Guid.Empty &&
        (string.IsNullOrWhiteSpace(criteria.Market) || string.Equals(criteria.Market, profile.Region, StringComparison.OrdinalIgnoreCase));
}
