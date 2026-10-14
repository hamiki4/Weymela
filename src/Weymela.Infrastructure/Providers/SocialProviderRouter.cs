using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;

namespace Weymela.Infrastructure.Providers;

public sealed class SocialProviderRouter(IEnumerable<ISocialVerificationAdapter> adapters, TimeProvider clock) : IVerifiedViewProvider, IVerifiedPublicationProvider
{
    private readonly IReadOnlyDictionary<string, ISocialVerificationAdapter> providers = adapters.ToDictionary(x => x.Capabilities.Name, StringComparer.Ordinal);
    public IReadOnlyList<ProviderCapabilities> Capabilities => providers.Values.Select(x => x.Capabilities).ToArray();
    public async Task<VerifiedViewResult> VerifyAsync(VerifiedViewRequest request, CancellationToken ct)
        => await VerifyCoreAsync(request, true, ct);
    public Task<VerifiedViewResult> VerifyPublicationAsync(VerifiedViewRequest request, CancellationToken ct)
        => VerifyCoreAsync(request, false, ct);
    private async Task<VerifiedViewResult> VerifyCoreAsync(VerifiedViewRequest request, bool requireViews, CancellationToken ct)
    {
        if (!providers.TryGetValue(request.Provider, out var adapter) || !adapter.Capabilities.AccountOwnership || !adapter.Capabilities.ContentIdentity || requireViews && !adapter.Capabilities.VerifiedViews)
            throw new ApplicationFailure(FailureKind.Validation, "Verified activity is not available for this platform yet.", code: "ProviderUnavailable");
        Operations.InputRules.Id(request.CreatorId); Operations.InputRules.Id(request.PromotionId); Operations.InputRules.Reference(request.ExternalContentId, "video reference", 100);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var health = await adapter.HealthAsync(timeout.Token);
            if (health.State != ProviderHealthState.Healthy) throw new InvalidOperationException();
            var evidence = await adapter.VerifyAsync(request, timeout.Token);
            if (!evidence.OwnershipVerified || evidence.CreatorId != request.CreatorId)
                throw new ApplicationFailure(FailureKind.Validation, "This post does not belong to the selected Creator.", code: "WrongCreator");
            if (evidence.Provider != request.Provider || evidence.ExternalContentId != request.ExternalContentId
                || evidence.Views < 0 || evidence.ObservedAtUtc.Kind != DateTimeKind.Utc || evidence.ObservedAtUtc > clock.GetUtcNow().UtcDateTime
                || evidence.ObservedAtUtc < clock.GetUtcNow().UtcDateTime.AddMinutes(-15) || string.IsNullOrWhiteSpace(evidence.EvidenceReference) || evidence.EvidenceReference.Length > 500)
                throw new InvalidOperationException();
            return new(evidence.Views, evidence.Provider, evidence.ExternalContentId, evidence.ObservedAtUtc, evidence.EvidenceReference);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ApplicationFailure) { throw; }
        catch { OperationalTelemetry.ProviderErrors.Add(1); throw new ApplicationFailure(FailureKind.Validation, "Verified activity could not be confirmed. Please try again later.", code: "ProviderUnavailable"); }
    }
}
