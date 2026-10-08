using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

public sealed partial class PayoutDestinationService(WeymelaDbContext db, IPayoutDestinationProtector protector, TimeProvider clock)
{
    [GeneratedRegex("^[0-9]{6,34}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountNumberPattern();

    public async Task<PayoutDestinationView?> OwnAsync(Actor actor, CancellationToken ct = default)
    {
        var (kind, subject) = Owner(actor);
        var registeredPhone = await VerifiedPhoneOrNull(actor.UserId, ct);
        var row = await db.PayoutDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, ct);
        if (row is null)
        {
            var legalName = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == actor.Role && x.SubjectId == subject)
                .Select(x => x.DisplayName).SingleAsync(ct);
            return new("Telebirr", "Telebirr", registeredPhone ?? string.Empty, legalName, null, false, false, registeredPhone);
        }
        if (row.Method is PayoutDestinationMethod.Telebirr or PayoutDestinationMethod.Mpesa)
        {
            var phone = await VerifiedPhoneOrNull(actor.UserId, ct);
            return phone is null
                ? new(row.Method.ToString(), row.Provider, string.Empty, row.LegalName, row.UpdatedAtUtc, false, false)
                : View(row, phone, false) with { RegisteredPhone = registeredPhone };
        }
        var account = new string('•', Math.Max(4, 8 - row.AccountLast4.Length)) + row.AccountLast4;
        return View(row, account, true) with { RegisteredPhone = registeredPhone };
    }

    public Task<PayoutDestinationView> UpdateAsync(Actor actor, PayoutDestinationInput input, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var (kind, subject) = Owner(actor);
            if (!Enum.TryParse<PayoutDestinationMethod>(input.Method, true, out var method) || !Enum.IsDefined(method))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Telebirr, M-PESA or Bank.");
            string provider; string account;
            if (method is PayoutDestinationMethod.Telebirr or PayoutDestinationMethod.Mpesa)
            {
                if (!string.IsNullOrWhiteSpace(input.BankName) || !string.IsNullOrWhiteSpace(input.AccountNumber))
                    throw new ApplicationFailure(FailureKind.Validation, "Mobile money uses your verified Weymela phone number.");
                provider = method == PayoutDestinationMethod.Mpesa ? "M-PESA" : "Telebirr";
                account = await VerifiedPhone(actor.UserId, token);
                if (method == PayoutDestinationMethod.Mpesa && !MpesaPhonePattern().IsMatch(account))
                    throw new ApplicationFailure(FailureKind.Validation, "M-PESA requires a verified Safaricom Ethiopia phone number (+2517 or 07).");
            }
            else
            {
                provider = Clean(input.BankName, 100, "Bank name is required.");
                account = Clean(input.AccountNumber, 34, "Account number is required.").Replace(" ", "", StringComparison.Ordinal);
                if (!AccountNumberPattern().IsMatch(account))
                    throw new ApplicationFailure(FailureKind.Validation, "Enter a valid bank account number.");
            }
            var legalName = await db.PublicWorkspaceProfiles.AsNoTracking()
                .Where(x => x.Role == (kind == PayoutBeneficiary.Creator ? ActorRole.Creator : ActorRole.Customer) && x.SubjectId == subject)
                .Select(x => x.DisplayName).SingleAsync(token);
            var now = clock.GetUtcNow().UtcDateTime;
            var row = await db.PayoutDestinations.SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, token);
            if (row is null)
            {
                row = new PayoutDestination { Beneficiary = kind, SubjectId = subject };
                db.PayoutDestinations.Add(row);
            }
            row.Method = method; row.Provider = provider; row.ProtectedAccount = protector.Protect(account);
            row.AccountLast4 = account[^Math.Min(4, account.Length)..]; row.LegalName = legalName; row.UpdatedAtUtc = now;
            db.AuditEvents.Add(new(Guid.NewGuid(), "PayoutDestinationUpdated", actor.UserId, actor.BusinessId, null,
                kind == PayoutBeneficiary.Creator ? subject : null, row.Id, now, $"Method={method};Provider={provider}"));
            await db.SaveChangesAsync(token);
            return View(row, method != PayoutDestinationMethod.Bank ? account : new string('•', 4) + row.AccountLast4,
                method == PayoutDestinationMethod.Bank);
        }, ct);

    public async Task<PayoutDestinationView?> AdminAsync(Actor actor, PayoutBeneficiary kind, Guid subject, CancellationToken ct = default)
    {
        DemandProcessor(actor, kind);
        var row = await db.PayoutDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, ct);
        return row is null ? null : View(row, protector.Unprotect(row.ProtectedAccount), false);
    }

    internal async Task<PayoutDestination> RequiredAsync(PayoutBeneficiary kind, Guid subject, CancellationToken ct)
    {
        var row = await db.PayoutDestinations.SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, ct)
            ?? throw new ApplicationFailure(FailureKind.Validation, "The beneficiary must configure a payout destination first.");
        if (row.Method == PayoutDestinationMethod.Mpesa)
        {
            var role = kind == PayoutBeneficiary.Creator ? ActorRole.Creator : ActorRole.Customer;
            var owners = db.CommercePermissions.Where(x => x.Role == role && x.SubjectId == subject && x.IsActive).Select(x => x.UserId);
            var phones = await db.AuthIdentifiers.AsNoTracking().Where(x => owners.Contains(x.UserId) && x.Kind == "Phone"
                && x.IsVerified && x.DeliveryAddress != null).Select(x => x.DeliveryAddress!).ToListAsync(ct);
            var account = protector.Unprotect(row.ProtectedAccount);
            if (phones.Count != 1 || phones[0] != account || !MpesaPhonePattern().IsMatch(account))
                throw new ApplicationFailure(FailureKind.Validation, "Update the M-PESA destination using your current verified phone before payment.");
        }
        return row;
    }

    internal string Unprotect(string value) => protector.Unprotect(value);

    private async Task<string> VerifiedPhone(Guid userId, CancellationToken ct)
        => await VerifiedPhoneOrNull(userId, ct)
            ?? throw new ApplicationFailure(FailureKind.Validation, "A single verified Weymela phone number is required for mobile money.");

    // Service compatibility only: wallet registration and recipient identity must be
    // checked in the external provider before an Admin records the manual payment.
    [GeneratedRegex("^(?:\\+251|0)7[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex MpesaPhonePattern();

    private async Task<string?> VerifiedPhoneOrNull(Guid userId, CancellationToken ct)
    {
        var phones = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == userId && x.Kind == "Phone"
            && x.IsVerified && x.DeliveryAddress != null).Select(x => x.DeliveryAddress!).ToListAsync(ct);
        return phones.Count == 1 ? phones[0] : null;
    }

    private static PayoutDestinationView View(PayoutDestination row, string account, bool masked) =>
        new(row.Method.ToString(), row.Provider, account, row.LegalName, row.UpdatedAtUtc, masked, true);
    private static (PayoutBeneficiary Kind, Guid Subject) Owner(Actor actor) => actor.Role switch
    {
        ActorRole.Creator when actor.CreatorId is { } id => (PayoutBeneficiary.Creator, id),
        ActorRole.Customer when actor.CustomerId is { } id => (PayoutBeneficiary.Customer, id),
        _ => throw new ApplicationFailure(FailureKind.Forbidden, "Creator or Customer access is required.")
    };
    private static string Clean(string? value, int max, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ApplicationFailure(FailureKind.Validation, message);
        var clean = value.Trim(); if (clean.Length > max) throw new ApplicationFailure(FailureKind.Validation, message); return clean;
    }
    private static void DemandProcessor(Actor actor, PayoutBeneficiary kind)
    {
        var capability = kind == PayoutBeneficiary.Customer ? AdministrativeCapability.CustomerPayoutProcessing : AdministrativeCapability.CreatorPayoutProcessing;
        if (!AdministrativeAuthority.For(new RealActor(actor)).Allows(capability))
            throw new ApplicationFailure(FailureKind.Forbidden, "Admin payout permission is required.");
    }
}
