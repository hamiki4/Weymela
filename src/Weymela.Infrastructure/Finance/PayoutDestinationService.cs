using System.Data;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Identity;

namespace Weymela.Infrastructure.Finance;

public sealed partial class PayoutDestinationService(WeymelaDbContext db, IPayoutDestinationProtector protector, TimeProvider clock)
{
    [GeneratedRegex("^[A-Z0-9-]{4,34}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountNumberPattern();
    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPattern();
    [GeneratedRegex("^[A-Z0-9]{8}(?:[A-Z0-9]{3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex SwiftPattern();
    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    public async Task<PayoutDestinationView?> OwnAsync(Actor actor, CancellationToken ct = default)
    {
        var (kind, subject) = Owner(actor);
        var registeredPhone = await VerifiedPhoneOrNull(actor.UserId, ct);
        var row = await db.PayoutDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, ct);
        if (row is null)
        {
            var legalName = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == actor.Role && x.SubjectId == subject)
                .Select(x => x.DisplayName).SingleAsync(ct);
            return PhoneView(new("Telebirr", "Telebirr", registeredPhone ?? string.Empty, legalName,
                null, false, false, registeredPhone), registeredPhone);
        }
        if (row.Method is PayoutDestinationMethod.Telebirr or PayoutDestinationMethod.Mpesa)
        {
            var phone = await VerifiedPhoneOrNull(actor.UserId, ct);
            return phone is null
                ? PhoneView(new(row.Method.ToString(), row.Provider, string.Empty, row.LegalName,
                    row.UpdatedAtUtc, false, false), null)
                : PhoneView(View(row, phone, false) with { RegisteredPhone = registeredPhone }, phone);
        }
        var secret = DecodeBank(protector.Unprotect(row.ProtectedAccount));
        var account = new string('•', Math.Max(4, 8 - row.AccountLast4.Length)) + row.AccountLast4;
        return View(row, account, true) with { RegisteredPhone = registeredPhone,
            PhoneCountry = PhoneCountry(registeredPhone), TelebirrEligible = TelebirrCompatible(registeredPhone),
            MpesaEligible = MpesaCompatible(registeredPhone), BankCountry = secret.Country,
            Iban = Mask(secret.Iban), SwiftBic = Mask(secret.SwiftBic), RoutingNumber = Mask(secret.RoutingNumber) };
    }

    public Task<PayoutDestinationView> UpdateAsync(Actor actor, PayoutDestinationInput input, CancellationToken ct = default) =>
        new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var (kind, subject) = Owner(actor);
            if (!Enum.TryParse<PayoutDestinationMethod>(input.Method, true, out var method) || !Enum.IsDefined(method))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Telebirr, M-PESA or Bank.");
            string provider; string account; BankSecret? bank = null;
            if (method is PayoutDestinationMethod.Telebirr or PayoutDestinationMethod.Mpesa)
            {
                if (!string.IsNullOrWhiteSpace(input.BankName) || !string.IsNullOrWhiteSpace(input.AccountNumber)
                    || !string.IsNullOrWhiteSpace(input.BankCountry) || !string.IsNullOrWhiteSpace(input.Iban)
                    || !string.IsNullOrWhiteSpace(input.SwiftBic) || !string.IsNullOrWhiteSpace(input.RoutingNumber))
                    throw new ApplicationFailure(FailureKind.Validation, "Mobile money uses your verified Weymela phone number.");
                provider = method == PayoutDestinationMethod.Mpesa ? "M-PESA" : "Telebirr";
                account = await VerifiedPhone(actor.UserId, token);
                if (method == PayoutDestinationMethod.Mpesa && !MpesaCompatible(account))
                    throw new ApplicationFailure(FailureKind.Validation, "M-PESA is currently available only for a verified Safaricom Ethiopia +2517 number.");
                if (method == PayoutDestinationMethod.Telebirr && !TelebirrCompatible(account))
                    throw new ApplicationFailure(FailureKind.Validation, "Telebirr is currently available only for an eligible verified Ethiopian phone number.");
            }
            else
            {
                provider = Clean(input.BankName, 100, "Bank name is required.");
                account = Clean(input.AccountNumber, 34, "Account number is required.").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                if (!AccountNumberPattern().IsMatch(account))
                    throw new ApplicationFailure(FailureKind.Validation, "Enter a valid bank account number.");
                var country = (string.IsNullOrWhiteSpace(input.BankCountry) ? "ET" : input.BankCountry.Trim()).ToUpperInvariant();
                if (!CountryPattern().IsMatch(country))
                    throw new ApplicationFailure(FailureKind.Validation, "Choose a valid bank country.");
                var iban = OptionalIdentifier(input.Iban, 34)?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                var swift = OptionalIdentifier(input.SwiftBic, 11)?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                var routing = OptionalIdentifier(input.RoutingNumber, 9)?.Replace(" ", "", StringComparison.Ordinal);
                if (iban is not null && (!IbanPattern().IsMatch(iban) || !iban.StartsWith(country, StringComparison.Ordinal)))
                    throw new ApplicationFailure(FailureKind.Validation, "Enter an IBAN that matches the bank country.");
                if (swift is not null && !SwiftPattern().IsMatch(swift))
                    throw new ApplicationFailure(FailureKind.Validation, "Enter a valid SWIFT/BIC.");
                if (country == "US" && (routing is null || !ValidUsRouting(routing)))
                    throw new ApplicationFailure(FailureKind.Validation, "Enter a valid 9-digit U.S. routing number.");
                if (country != "ET" && country != "US" && swift is null)
                    throw new ApplicationFailure(FailureKind.Validation, "SWIFT/BIC is required for this bank country.");
                if (country == "GB" && iban is null)
                    throw new ApplicationFailure(FailureKind.Validation, "IBAN is required for a United Kingdom bank account.");
                bank = new(account, country, iban, swift, routing);
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
            row.Method = method; row.Provider = provider;
            row.ProtectedAccount = protector.Protect(bank is null || bank.IsLegacyLocal ? account : EncodeBank(bank));
            row.AccountLast4 = account[^Math.Min(4, account.Length)..]; row.LegalName = legalName; row.UpdatedAtUtc = now;
            db.AuditEvents.Add(new(Guid.NewGuid(), "PayoutDestinationUpdated", actor.UserId, actor.BusinessId, null,
                kind == PayoutBeneficiary.Creator ? subject : null, row.Id, now, $"Method={method};Provider={provider}"));
            await db.SaveChangesAsync(token);
            if (bank is null) return PhoneView(View(row, account, false), account);
            return View(row, new string('•', 4) + row.AccountLast4, true) with { BankCountry = bank.Country,
                Iban = Mask(bank.Iban), SwiftBic = Mask(bank.SwiftBic), RoutingNumber = Mask(bank.RoutingNumber) };
        }, ct);

    public async Task<PayoutDestinationView?> AdminAsync(Actor actor, PayoutBeneficiary kind, Guid subject, CancellationToken ct = default)
    {
        DemandProcessor(actor, kind);
        var row = await db.PayoutDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, ct);
        if (row is null) return null;
        var raw = protector.Unprotect(row.ProtectedAccount);
        if (row.Method != PayoutDestinationMethod.Bank) return PhoneView(View(row, raw, false), raw);
        var bank = DecodeBank(raw);
        return View(row, bank.Account, false) with { BankCountry = bank.Country, Iban = bank.Iban,
            SwiftBic = bank.SwiftBic, RoutingNumber = bank.RoutingNumber };
    }

    internal async Task<PayoutDestination> RequiredAsync(PayoutBeneficiary kind, Guid subject, CancellationToken ct)
    {
        var row = await db.PayoutDestinations.SingleOrDefaultAsync(x => x.Beneficiary == kind && x.SubjectId == subject, ct)
            ?? throw new ApplicationFailure(FailureKind.Validation, "The beneficiary must configure a payout destination first.");
        if (row.Method is PayoutDestinationMethod.Mpesa or PayoutDestinationMethod.Telebirr)
        {
            var role = kind == PayoutBeneficiary.Creator ? ActorRole.Creator : ActorRole.Customer;
            var owners = db.CommercePermissions.Where(x => x.Role == role && x.SubjectId == subject && x.IsActive).Select(x => x.UserId);
            var phones = await db.AuthIdentifiers.AsNoTracking().Where(x => owners.Contains(x.UserId) && x.Kind == "Phone"
                && x.IsVerified && x.DeliveryAddress != null).Select(x => x.DeliveryAddress!).ToListAsync(ct);
            var account = protector.Unprotect(row.ProtectedAccount);
            var compatible = row.Method == PayoutDestinationMethod.Mpesa ? MpesaCompatible(account) : TelebirrCompatible(account);
            if (phones.Count != 1 || PhoneNumberNormalizer.Normalize(phones[0]) != account || !compatible)
                throw new ApplicationFailure(FailureKind.Validation, $"Update the {row.Provider} destination using your current eligible verified phone before payment.");
        }
        return row;
    }

    internal string Unprotect(string value)
    {
        var raw = protector.Unprotect(value);
        return DisplayUnprotected(raw);
    }
    internal static string DisplayUnprotected(string raw) =>
        raw.StartsWith(BankPrefix, StringComparison.Ordinal) ? DecodeBank(raw).Display : raw;

    private async Task<string> VerifiedPhone(Guid userId, CancellationToken ct)
        => await VerifiedPhoneOrNull(userId, ct)
            ?? throw new ApplicationFailure(FailureKind.Validation, "A single verified Weymela phone number is required for mobile money.");

    // Service compatibility only: wallet registration and recipient identity must be
    // checked in the external provider before an Admin records the manual payment.
    [GeneratedRegex("^\\+2517[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex MpesaPhonePattern();
    [GeneratedRegex("^\\+251[79][0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex TelebirrPhonePattern();

    private async Task<string?> VerifiedPhoneOrNull(Guid userId, CancellationToken ct)
    {
        var phones = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == userId && x.Kind == "Phone"
            && x.IsVerified && x.DeliveryAddress != null).Select(x => x.DeliveryAddress!).ToListAsync(ct);
        if (phones.Count != 1) return null;
        try { return PhoneNumberNormalizer.Normalize(phones[0]); }
        catch (ApplicationFailure) { return null; }
    }

    private static PayoutDestinationView View(PayoutDestination row, string account, bool masked) =>
        new(row.Method.ToString(), row.Provider, account, row.LegalName, row.UpdatedAtUtc, masked, true);
    private static PayoutDestinationView PhoneView(PayoutDestinationView view, string? phone) => view with
    {
        RegisteredPhone = phone,
        PhoneCountry = PhoneCountry(phone),
        TelebirrEligible = TelebirrCompatible(phone),
        MpesaEligible = MpesaCompatible(phone)
    };
    private static string? PhoneCountry(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : PhoneNumberNormalizer.Region(phone);
    private static bool TelebirrCompatible(string? phone) => !string.IsNullOrWhiteSpace(phone) && TelebirrPhonePattern().IsMatch(phone);
    private static bool MpesaCompatible(string? phone) => !string.IsNullOrWhiteSpace(phone) && MpesaPhonePattern().IsMatch(phone);
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
    private static string? OptionalIdentifier(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        if (clean.Length > max) throw new ApplicationFailure(FailureKind.Validation, "Bank identifier is too long.");
        return clean;
    }
    private static bool ValidUsRouting(string value)
    {
        if (value.Length != 9 || value.Any(c => c is < '0' or > '9')) return false;
        var sum = 3 * (value[0] - '0' + value[3] - '0' + value[6] - '0')
            + 7 * (value[1] - '0' + value[4] - '0' + value[7] - '0')
            + (value[2] - '0' + value[5] - '0' + value[8] - '0');
        return sum % 10 == 0;
    }
    private const string BankPrefix = "bank-v2:";
    private static string EncodeBank(BankSecret secret) => BankPrefix + JsonSerializer.Serialize(secret);
    private static BankSecret DecodeBank(string value)
    {
        if (!value.StartsWith(BankPrefix, StringComparison.Ordinal)) return new(value, "ET", null, null, null);
        return JsonSerializer.Deserialize<BankSecret>(value[BankPrefix.Length..])
            ?? throw new ApplicationFailure(FailureKind.Validation, "The bank destination is unavailable.");
    }
    private static string? Mask(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : new string('•', Math.Max(4, value.Length - Math.Min(4, value.Length))) + value[^Math.Min(4, value.Length)..];
    private sealed record BankSecret(string Account, string Country, string? Iban, string? SwiftBic, string? RoutingNumber)
    {
        public bool IsLegacyLocal => Country == "ET" && Iban is null && SwiftBic is null && RoutingNumber is null;
        public string Display => string.Join(" · ", new[] { Account, Country,
            Iban is null ? null : $"IBAN {Iban}", SwiftBic is null ? null : $"SWIFT {SwiftBic}",
            RoutingNumber is null ? null : $"Routing {RoutingNumber}" }.Where(x => x is not null));
    }
    private static void DemandProcessor(Actor actor, PayoutBeneficiary kind)
    {
        var capability = kind == PayoutBeneficiary.Customer ? AdministrativeCapability.CustomerPayoutProcessing : AdministrativeCapability.CreatorPayoutProcessing;
        if (!AdministrativeAuthority.For(new RealActor(actor)).Allows(capability))
            throw new ApplicationFailure(FailureKind.Forbidden, "Admin payout permission is required.");
    }
}
