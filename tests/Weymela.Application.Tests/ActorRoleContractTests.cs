using Weymela.Application;
using Xunit;

namespace Weymela.Application.Tests;

public sealed class ActorRoleContractTests
{
    [Fact]
    public void Published_numeric_role_contract_matches_the_authoritative_enum()
    {
        Assert.Equal(0, (int)ActorRole.PlatformAdmin);
        Assert.Equal(1, (int)ActorRole.OperationsAdmin);
        Assert.Equal(2, (int)ActorRole.Business);
        Assert.Equal(3, (int)ActorRole.Creator);
        Assert.Equal(4, (int)ActorRole.Customer);
        Assert.Equal(5, (int)ActorRole.Cashier);
    }
}
