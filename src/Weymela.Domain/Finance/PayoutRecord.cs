namespace Weymela.Domain;

public enum PayoutBeneficiary { Creator, Customer }
public enum PayoutStatus { Eligible, Paid }

public sealed class PayoutRecord
{
    private PayoutRecord() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public PayoutBeneficiary Beneficiary { get; private set; }
    public Guid? CreatorId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid BeneficiaryId => CreatorId ?? CustomerId!.Value;
    public Money ThresholdUsed { get; private set; }
    public Money Amount { get; private set; }
    public Guid ConfigurationVersionId { get; private set; }
    public DateTime EligibleAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public Guid? PaidBy { get; private set; }
    public string? Reference { get; private set; }
    public Guid? JournalId { get; private set; }
    public PayoutStatus Status { get; private set; } = PayoutStatus.Eligible;
    public long Version { get; private set; }
    public PayoutRecord(PayoutBeneficiary beneficiary, Guid subjectId, Money available, Money threshold, Guid version, DateTime eligibleAt)
    {
        if (threshold.Amount <= 0 || available.Currency != threshold.Currency || available.Amount < threshold.Amount)
            throw new InvalidOperationException("Payout threshold has not been reached.");
        Beneficiary = beneficiary;
        if (beneficiary == PayoutBeneficiary.Creator) CreatorId = subjectId; else CustomerId = subjectId;
        Amount = threshold; ThresholdUsed = threshold; ConfigurationVersionId = version; EligibleAtUtc = eligibleAt;
    }
    public void MarkPaid(Guid admin, string reference, Guid journal, DateTime at)
    {
        if (Status != PayoutStatus.Eligible || string.IsNullOrWhiteSpace(reference)) throw new InvalidOperationException("Payout cannot be marked paid.");
        PaidBy = admin; Reference = reference.Trim(); JournalId = journal; PaidAtUtc = at; Status = PayoutStatus.Paid; Version++;
    }
}
