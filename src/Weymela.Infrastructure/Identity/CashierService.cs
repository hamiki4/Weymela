using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Finance;
using Weymela.Application.Web;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Identity;

public sealed class CashierService(
    WeymelaDbContext db,
    RuntimeOptions options,
    IFirebaseCustomTokenIssuer tokenIssuer,
    TimeProvider clock)
{
    private static readonly TimeSpan ActivationLifetime = TimeSpan.FromHours(24);
    private const int MaximumActivationAttempts = 5;

    public async Task<IReadOnlyList<CashierView>> ListAsync(Actor actor, CancellationToken ct)
    {
        var businessId = RequireBusiness(actor);
        return (await db.CashierPreauthorizations.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .OrderBy(x => x.Status).ThenBy(x => x.DisplayName)
                .ToListAsync(ct))
            .Select(View)
            .ToArray();
    }

    public Task<CashierCreated> CreateAsync(Actor actor, CreateCashierInput input, string key, CancellationToken ct)
        => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var businessId = RequireBusiness(actor);
            if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
                throw new ApplicationFailure(FailureKind.Validation, "A request reference is required.");
            var name = CleanName(input.Name);
            var phone = NormalizePhone(input.Phone);
            var fingerprint = RequestFingerprint.Create(name, phone);
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "CreateCashier", key, fingerprint, token) is { } replay)
            {
                var previous = await db.CashierPreauthorizations.AsNoTracking()
                    .SingleAsync(x => x.Id == Guid.Parse(replay), token);
                return new CashierCreated(View(previous), "");
            }

            if (await db.CashierPreauthorizations.AnyAsync(x => x.BusinessId == businessId
                    && x.CanonicalPhone == phone
                    && (x.Status == CashierPreauthorizationStatus.PendingActivation
                        || x.Status == CashierPreauthorizationStatus.Active),
                token))
                throw new ApplicationFailure(FailureKind.Validation, "This phone already has a Cashier for your Business.");

            var now = clock.GetUtcNow().UtcDateTime;
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
                .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var row = new CashierPreauthorization(
                businessId,
                name,
                phone,
                EmailAuthService.HashIdentifier(phone),
                AuthCodeHashing.Hash(code, options.AuthCodeHashKey ?? throw new AuthChallengeUnavailableException("Cashier activation is unavailable.")),
                now.Add(ActivationLifetime),
                now);
            db.CashierPreauthorizations.Add(row);
            operation.Remember(actor, "CreateCashier", key, fingerprint, row.Id.ToString("D"), now);
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "CashierPreauthorizationCreated", actor.UserId,
                businessId, null, null, Guid.NewGuid(), now, $"cashier={row.Id:D}"));
            return new CashierCreated(View(row), code);
        }, ct);

    public Task<CashierCreated> RegenerateActivationCodeAsync(Actor actor, Guid id, string key, CancellationToken ct)
        => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var businessId = RequireBusiness(actor);
            var row = await Owned(id, businessId, token);
            if (row.Status != CashierPreauthorizationStatus.PendingActivation)
                throw new ApplicationFailure(FailureKind.Validation, "Only a pending Cashier can receive a new activation code.");
            var now = clock.GetUtcNow().UtcDateTime;
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
                .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            row.ActivationCodeHash = AuthCodeHashing.Hash(code, options.AuthCodeHashKey ?? throw new AuthChallengeUnavailableException("Cashier activation is unavailable."));
            row.ActivationCodeExpiresAtUtc = now.Add(ActivationLifetime);
            row.ActivationAttemptCount = 0;
            row.Version++;
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "CashierActivationCodeRegenerated", actor.UserId,
                businessId, null, null, Guid.NewGuid(), now, $"cashier={row.Id:D}"));
            return new CashierCreated(View(row), code);
        }, ct);

    public Task<CashierView> SetStateAsync(Actor actor, Guid id, string state, string key, CancellationToken ct)
        => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var businessId = RequireBusiness(actor);
            var row = await Owned(id, businessId, token);
            var requested = state.Trim().ToLowerInvariant();
            var now = clock.GetUtcNow().UtcDateTime;
            if (requested is "disable" or "disabled" or "revoke" or "revoked")
            {
                row.Status = requested.StartsWith("revoke", StringComparison.Ordinal)
                    ? CashierPreauthorizationStatus.Revoked
                    : CashierPreauthorizationStatus.Disabled;
                row.DisabledAtUtc = now;
                if (row.UserId is { } userId)
                {
                    var permission = await db.CommercePermissions.SingleOrDefaultAsync(x => x.UserId == userId
                        && x.Role == ActorRole.Cashier && x.BusinessId == businessId, token);
                    if (permission is not null) permission.IsActive = false;
                }
            }
            else if (requested is "enable" or "active")
            {
                if (row.Status == CashierPreauthorizationStatus.Revoked)
                    throw new ApplicationFailure(FailureKind.Validation, "A revoked Cashier cannot be re-enabled.");
                if (row.UserId is null)
                {
                    row.Status = CashierPreauthorizationStatus.PendingActivation;
                    row.DisabledAtUtc = null;
                }
                else
                {
                    row.Status = CashierPreauthorizationStatus.Active;
                    row.DisabledAtUtc = null;
                    var permission = await db.CommercePermissions.SingleOrDefaultAsync(x => x.UserId == row.UserId
                        && x.Role == ActorRole.Cashier && x.BusinessId == businessId, token);
                    if (permission is null)
                        throw new ApplicationFailure(FailureKind.Validation, "The Cashier profile is unavailable.");
                    permission.IsActive = true;
                    permission.CanCheckout = true;
                }
            }
            else throw new ApplicationFailure(FailureKind.Validation, "Choose a supported Cashier state.");
            row.Version++;
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "CashierAccessChanged", actor.UserId,
                businessId, null, null, Guid.NewGuid(), now, $"cashier={row.Id:D};state={row.Status}"));
            return View(row);
        }, ct);

    public Task<CashierActivationResult> ActivateAsync(CashierActivationInput input, CancellationToken ct)
        => new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var phone = NormalizePhone(input.Phone);
            var code = input.ActivationCode.Trim();
            if (code.Length != 6 || !code.All(char.IsAsciiDigit))
                throw InvalidActivation();
            var now = clock.GetUtcNow().UtcDateTime;
            var phoneHash = EmailAuthService.HashIdentifier(phone);
            var candidates = await db.CashierPreauthorizations.AsTracking()
                .Where(x => x.PhoneIdentifierHash == phoneHash
                    && x.Status == CashierPreauthorizationStatus.PendingActivation)
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync(token);
            var row = candidates.FirstOrDefault(x => x.ActivationCodeExpiresAtUtc > now
                && x.ActivationAttemptCount < MaximumActivationAttempts
                && AuthCodeHashing.Verify(code, x.ActivationCodeHash,
                    options.AuthCodeHashKey ?? throw new AuthChallengeUnavailableException("Cashier activation is unavailable.")));
            if (row is null)
            {
                var attempt = candidates.FirstOrDefault();
                if (attempt is not null)
                {
                    attempt.ActivationAttemptCount = Math.Min(MaximumActivationAttempts, attempt.ActivationAttemptCount + 1);
                    if (attempt.ActivationAttemptCount >= MaximumActivationAttempts)
                        attempt.Status = CashierPreauthorizationStatus.Disabled;
                    attempt.Version++;
                }
                throw InvalidActivation();
            }

            if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Business
                    && x.SubjectId == row.BusinessId && x.BusinessId == row.BusinessId && x.IsActive,
                token))
                throw InvalidActivation();

            var phoneIdentifier = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone"
                && x.IdentifierHash == phoneHash, token);
            var userId = phoneIdentifier?.UserId ?? Guid.NewGuid();
            if (phoneIdentifier is null)
            {
                phoneIdentifier = new AuthIdentifierRecord
                {
                    UserId = userId,
                    Kind = "Phone",
                    IdentifierHash = phoneHash,
                    DeliveryAddress = phone,
                    IsVerified = true,
                    CreatedAtUtc = now
                };
                db.AuthIdentifiers.Add(phoneIdentifier);
            }
            else
            {
                phoneIdentifier.DeliveryAddress = phone;
                phoneIdentifier.IsVerified = true;
            }

            var permission = await db.CommercePermissions.SingleOrDefaultAsync(x => x.UserId == userId
                && x.Role == ActorRole.Cashier && x.BusinessId == row.BusinessId, token);
            if (permission is not null)
            {
                permission.IsActive = true;
                permission.CanCheckout = true;
            }
            else
            {
                db.CommercePermissions.Add(new CommercePermission(userId, ActorRole.Cashier,
                    userId, row.BusinessId, true, true));
            }
            row.UserId = userId;
            row.Status = CashierPreauthorizationStatus.Active;
            row.ActivatedAtUtc = now;
            row.ActivationCodeHash = "consumed";
            row.ActivationCodeExpiresAtUtc = now;
            row.ActivationAttemptCount = 0;
            row.Version++;
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "CashierActivated", userId,
                row.BusinessId, null, null, Guid.NewGuid(), now, $"cashier={row.Id:D}"));
            var tokenResult = await tokenIssuer.IssueAsync(userId, options.FirebaseProjectId, token);
            return new CashierActivationResult(tokenResult);
        }, ct);

    private async Task<CashierPreauthorization> Owned(Guid id, Guid businessId, CancellationToken ct)
        => await db.CashierPreauthorizations.SingleOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Cashier not found.");

    private static Guid RequireBusiness(Actor actor)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null || actor.BusinessId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Forbidden, "Business access is required.");
        return actor.BusinessId.Value;
    }

    private static string CleanName(string value)
    {
        var result = value?.Trim() ?? "";
        if (result.Length is < 1 or > 120) throw new ApplicationFailure(FailureKind.Validation, "Enter a Cashier name.");
        return result;
    }

    private static string NormalizePhone(string value)
    {
        try { return PhoneNumberNormalizer.Normalize(value); }
        catch (ApplicationFailure) { throw new ApplicationFailure(FailureKind.Validation, "Enter a valid phone number."); }
    }

    private static CashierView View(CashierPreauthorization row) => new(row.Id, row.DisplayName,
        Mask(row.CanonicalPhone), row.Status switch
        {
            CashierPreauthorizationStatus.PendingActivation => "Pending Activation",
            CashierPreauthorizationStatus.Active => "Active",
            CashierPreauthorizationStatus.Disabled => "Disabled",
            CashierPreauthorizationStatus.Revoked => "Revoked",
            _ => "Unavailable"
        }, row.CreatedAtUtc, row.ActivatedAtUtc);

    private static string Mask(string phone)
        => phone.Length < 7 ? "••••" : phone[..4] + " ••• ••" + phone[^3..];

    private static ApplicationFailure InvalidActivation() =>
        new(FailureKind.Forbidden, "The Cashier activation code is invalid or expired.");
}
