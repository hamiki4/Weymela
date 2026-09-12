using System.Collections.Concurrent;
using Weymela.Application;

namespace Weymela.Infrastructure.Development;

public sealed class DevelopmentViewProvider(TimeProvider clock) : IVerifiedViewProvider
{
    private readonly ConcurrentDictionary<string,long> counts=new();
    public void SetCount(string contentId,long count)=>counts[contentId]=count;
    public Task<VerifiedViewResult> VerifyAsync(VerifiedViewRequest request,CancellationToken ct)=>Task.FromResult(new VerifiedViewResult(
        counts.GetValueOrDefault(request.ExternalContentId,1000),request.Provider,request.ExternalContentId,clock.GetUtcNow().UtcDateTime,"local-development-evidence"));
}
public sealed class UnavailableViewProvider : IVerifiedViewProvider
{
    public Task<VerifiedViewResult> VerifyAsync(VerifiedViewRequest request,CancellationToken ct)=>throw new ApplicationFailure(FailureKind.Validation,"Verified social views are not connected yet.");
}
