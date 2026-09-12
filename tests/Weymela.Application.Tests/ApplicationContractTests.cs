using System;
using Weymela.Application;
using Xunit;

namespace Weymela.Application.Tests;

public sealed class ApplicationContractTests
{
    [Fact] public void Business_owner_authorization_rejects_other_business() { var service = new AuthorizationService(); var actor = new Actor(Guid.NewGuid(), ActorRole.Business, Guid.NewGuid()); Assert.Throws<ApplicationFailure>(() => service.DemandBusinessOwner(actor, Guid.NewGuid())); }
    [Fact] public void Creator_self_authorization_rejects_other_creator() { var service = new AuthorizationService(); var actor = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: Guid.NewGuid()); Assert.Throws<ApplicationFailure>(() => service.DemandCreatorSelf(actor, Guid.NewGuid())); }
    [Fact] public void Customer_and_cashier_have_no_promotion_admin_role() { var service = new AuthorizationService(); Assert.Throws<ApplicationFailure>(() => service.Demand(new Actor(Guid.NewGuid(), ActorRole.Customer), ActorRole.Business)); Assert.Throws<ApplicationFailure>(() => service.Demand(new Actor(Guid.NewGuid(), ActorRole.Cashier), ActorRole.PlatformAdmin)); }
    [Fact] public void Idempotency_record_carries_operation_actor_fingerprint_and_result_reference() { var id = Guid.NewGuid(); var record = new IdempotencyRecord("key", "FundPromotion", id, "fingerprint", Guid.NewGuid(), DateTime.UtcNow); Assert.Equal(id, record.ActorId); Assert.Equal("FundPromotion", record.OperationType); }
    [Fact] public void Creator_projection_contract_has_no_business_wallet_or_private_contact_fields() { var names = typeof(CreatorPromotionProjection).GetProperties(); Assert.DoesNotContain(names, x => x.Name.Contains("Wallet", StringComparison.OrdinalIgnoreCase)); Assert.DoesNotContain(names, x => x.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase)); Assert.DoesNotContain(names, x => x.Name.Contains("Email", StringComparison.OrdinalIgnoreCase)); }
}
