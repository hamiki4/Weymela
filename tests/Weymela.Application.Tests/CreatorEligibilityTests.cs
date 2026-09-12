using System;
using Weymela.Application;
using Weymela.Domain;
using Xunit;
namespace Weymela.Application.Tests;

public sealed class CreatorEligibilityTests
{
    [Fact] public void Matching_verified_category_region_and_followers_are_eligible() => Assert.True(new CreatorEligibility().IsEligible(Guid.NewGuid(), new("Food", 1000, "Addis Ababa", null), new("food", "addis ababa", 2000, true)));
    [Fact] public void Wrong_category_is_not_eligible() => Assert.False(new CreatorEligibility().IsEligible(Guid.NewGuid(), new("Food", null, null, null), new("Fashion", null, 0, false)));
    [Fact] public void Wrong_region_is_not_eligible() => Assert.False(new CreatorEligibility().IsEligible(Guid.NewGuid(), new(null, null, "Addis Ababa", null), new(null, "Hawassa", 0, false)));
    [Fact] public void Unverified_follower_count_is_not_accepted() => Assert.False(new CreatorEligibility().IsEligible(Guid.NewGuid(), new(null, 1000, null, null), new(null, null, 999999, false)));
    [Fact] public void Insufficient_verified_followers_are_not_eligible() => Assert.False(new CreatorEligibility().IsEligible(Guid.NewGuid(), new(null, 1000, null, null), new(null, null, 999, true)));
    [Fact] public void No_criteria_does_not_create_a_minimum_followers_rule() => Assert.True(new CreatorEligibility().IsEligible(Guid.NewGuid(), new(null, null, null, null), new(null, null, 0, false)));
    [Fact] public void Empty_creator_identity_is_not_eligible() => Assert.False(new CreatorEligibility().IsEligible(Guid.Empty, new(null, null, null, null), new(null, null, 0, true)));
}
