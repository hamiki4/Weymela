using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;

namespace Weymela.Infrastructure.Deposits;

public sealed class ManualApprovalDepositProvider : IDepositProvider
{
    public string Name => "ManualApproval";
    public Task<DepositEvidence> SubmitAsync(Guid businessId, DepositSubmission submission, string requestKey, CancellationToken ct)
    {
        InputRules.Id(businessId); InputRules.Reference(requestKey, "request key", 200);
        var reference = InputRules.Reference(submission.ExternalReference, "external deposit reference").ToUpperInvariant();
        var proof = submission.ProofReference is null ? null : InputRules.Reference(submission.ProofReference, "proof reference");
        return Task.FromResult(new DepositEvidence(Name, reference, proof, true));
    }
}
public sealed class DisabledDepositProvider : IDepositProvider
{
    public string Name => "Disabled";
    public Task<DepositEvidence> SubmitAsync(Guid businessId, DepositSubmission submission, string requestKey, CancellationToken ct) =>
        throw new ApplicationFailure(FailureKind.Validation, "Deposits are not connected. No funds have been credited.");
}
