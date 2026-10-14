using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Providers;
using Xunit;
namespace Weymela.Infrastructure.Tests;
public sealed class PublicationProviderTests
{
    [Fact] public async Task Fixed_fee_publication_requires_ownership_and_post_evidence_but_no_view_capability()
    {
        var clock=new TestClock();var adapter=new PublicationOnly(clock);
        var router=new SocialProviderRouter([adapter],clock);
        var request=new VerifiedViewRequest(Guid.NewGuid(),Guid.NewGuid(),"TikTok","7412345678901234567");
        Assert.Equal(0,(await router.VerifyPublicationAsync(request,default)).Count);
        Assert.Equal("ProviderUnavailable",(await Assert.ThrowsAsync<ApplicationFailure>(()=>router.VerifyAsync(request,default))).Code);
        adapter.WrongCreator=true;
        Assert.Equal("WrongCreator",(await Assert.ThrowsAsync<ApplicationFailure>(()=>router.VerifyPublicationAsync(request,default))).Code);
        Assert.Equal("ProviderUnavailable",(await Assert.ThrowsAsync<ApplicationFailure>(()=>new SocialProviderRouter([],clock).VerifyPublicationAsync(request,default))).Code);
    }
    private sealed class PublicationOnly(TestClock clock):ISocialVerificationAdapter
    {
        public bool WrongCreator{get;set;}
        public ProviderCapabilities Capabilities=>new("TikTok",true,true,false);
        public Task<ProviderHealth> HealthAsync(CancellationToken ct)=>Task.FromResult(new ProviderHealth("TikTok",ProviderHealthState.Healthy,"Healthy",clock.Now));
        public Task<VerifiedContentEvidence> VerifyAsync(VerifiedViewRequest request,CancellationToken ct)=>Task.FromResult(
            new VerifiedContentEvidence(WrongCreator?Guid.NewGuid():request.CreatorId,"TikTok",request.ExternalContentId,true,0,clock.Now,"isolated-publication-proof"));
    }
}
