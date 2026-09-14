using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public sealed class AuthChallengeUnavailableException : Exception
{
    public AuthChallengeUnavailableException(string message) : base(message) { }
}

public sealed class AuthChallengeInvalidException : Exception
{
    public AuthChallengeInvalidException() : base("The code is invalid or expired.") { }
}

public sealed record EmailCodeStartOutcome(bool Accepted, DateTime ExpiresAtUtc, int ResendAfterSeconds);

/// <summary>
/// Email verification is deliberately separate from Firebase sign-in. Codes are
/// short-lived, purpose-bound and persisted only as salted keyed hashes. The
/// default adapters are disabled; Pilot must provide both explicitly.
/// </summary>
public sealed class EmailAuthService(
    WeymelaDbContext db,
    IEmailCodeDelivery delivery,
    IFirebaseCustomTokenIssuer tokenIssuer,
    RuntimeOptions options,
    TimeProvider clock)
{
    private static readonly Regex Email = new("^[^@\\s]{1,96}@[^@\\s]{1,96}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Phone = new("^\\+[1-9][0-9]{6,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendWindow = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    public async Task<EmailCodeStartOutcome> StartAsync(string identifier, string? phone, EmailCodePurpose purpose, CancellationToken ct)
    {
        EnsureConfigured();
        var normalizedIdentifier = NormalizeIdentifier(identifier, out var identifierKind);
        var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : NormalizePhone(phone);
        if (purpose == EmailCodePurpose.Signup && (identifierKind != AuthIdentifierKind.Email || normalizedPhone is null))
            throw new ApplicationFailure(FailureKind.Validation, "Phone number and email are required.");

        var identifierHash = HashIdentifier(normalizedIdentifier);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existingEmail = identifierKind == AuthIdentifierKind.Email
            ? await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Email" && x.IdentifierHash == identifierHash, ct)
            : null;
        var existingPhone = identifierKind == AuthIdentifierKind.Phone
            ? await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone" && x.IdentifierHash == identifierHash, ct)
            : normalizedPhone is null ? null : await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone" && x.IdentifierHash == HashIdentifier(normalizedPhone), ct);

        // Conflicting identifiers never overwrite or merge accounts. The public endpoint
        // returns the same generic response for this branch as for a normal request.
        if (purpose == EmailCodePurpose.Signup && (existingEmail is not null || existingPhone is not null))
        {
            await tx.CommitAsync(ct);
            return new(false, now.Add(Lifetime), (int)ResendWindow.TotalSeconds);
        }
        AuthIdentifierRecord? deliveryEmail;
        if (purpose == EmailCodePurpose.Signup)
        {
            deliveryEmail = null;
        }
        else if (identifierKind == AuthIdentifierKind.Email)
        {
            deliveryEmail = existingEmail;
            if (deliveryEmail is null || !deliveryEmail.IsVerified)
            {
                await tx.CommitAsync(ct);
                return new(false, now.Add(Lifetime), (int)ResendWindow.TotalSeconds);
            }
        }
        else
        {
            deliveryEmail = existingPhone is null
                ? null
                : await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Email" && x.UserId == existingPhone.UserId && x.IsVerified, ct);
            if (existingPhone is null || deliveryEmail is null || string.IsNullOrWhiteSpace(deliveryEmail.DeliveryAddress))
            {
                await tx.CommitAsync(ct);
                return new(false, now.Add(Lifetime), (int)ResendWindow.TotalSeconds);
            }
        }
        var emailHash = purpose == EmailCodePurpose.Signup
            ? HashIdentifier(normalizedIdentifier)
            : identifierKind == AuthIdentifierKind.Email ? identifierHash : HashIdentifier(deliveryEmail!.DeliveryAddress!);
        var challengeKeyHash = purpose == EmailCodePurpose.Signup ? emailHash : identifierHash;
        var userId = purpose == EmailCodePurpose.Signup ? (Guid?)Guid.NewGuid() : deliveryEmail!.UserId;
        var deliveryAddress = purpose == EmailCodePurpose.Signup
            ? normalizedIdentifier
            : identifierKind == AuthIdentifierKind.Email ? normalizedIdentifier : deliveryEmail!.DeliveryAddress!;
        if (purpose != EmailCodePurpose.Signup && deliveryEmail is null)
        {
            await tx.CommitAsync(ct);
            return new(false, now.Add(Lifetime), (int)ResendWindow.TotalSeconds);
        }
        var recent = await db.EmailAuthChallenges.AsTracking()
            .Where(x => x.IdentifierHash == challengeKeyHash && x.Purpose == purpose.ToString() && x.ConsumedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (recent is not null && recent.LastSentAtUtc is not null && recent.LastSentAtUtc > now - ResendWindow)
        {
            await tx.CommitAsync(ct);
            return new(true, recent.ExpiresAtUtc, (int)Math.Ceiling((recent.LastSentAtUtc.Value.Add(ResendWindow) - now).TotalSeconds));
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var challenge = new EmailAuthChallengeRecord
        {
            UserId = userId,
            IdentifierHash = challengeKeyHash,
            EmailIdentifierHash = emailHash,
            PhoneIdentifierHash = purpose == EmailCodePurpose.Signup ? HashIdentifier(normalizedPhone!) : identifierKind == AuthIdentifierKind.Phone ? identifierHash : null,
            Purpose = purpose.ToString(),
            CodeHash = AuthCodeHashing.Hash(code, options.AuthCodeHashKey!),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime),
            LastSentAtUtc = now,
            MaxAttempts = MaxAttempts
        };
        db.EmailAuthChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await delivery.SendAsync(deliveryAddress, code, purpose, ct);
        await tx.CommitAsync(ct);
        return new(true, challenge.ExpiresAtUtc, (int)ResendWindow.TotalSeconds);
    }

    public async Task<FirebaseCustomTokenResult> VerifyAsync(string email, EmailCodePurpose purpose, string code, CancellationToken ct)
    {
        EnsureConfigured();
        if (purpose == EmailCodePurpose.PinRecovery) throw new AuthChallengeUnavailableException("Secure PIN reset is not configured.");
        var normalizedIdentifier = NormalizeIdentifier(email, out _);
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) throw new AuthChallengeInvalidException();
        var hash = HashIdentifier(normalizedIdentifier);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var challenge = await db.EmailAuthChallenges.AsTracking().Where(x => x.IdentifierHash == hash && x.Purpose == purpose.ToString())
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (challenge is null || challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= now || challenge.AttemptCount >= challenge.MaxAttempts)
            throw new AuthChallengeInvalidException();
        challenge.AttemptCount++;
        if (!AuthCodeHashing.Verify(code, challenge.CodeHash, options.AuthCodeHashKey!))
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new AuthChallengeInvalidException();
        }
        if (challenge.UserId is null) throw new AuthChallengeInvalidException();
        var userId = challenge.UserId.Value;
        var emailHash = challenge.EmailIdentifierHash ?? challenge.IdentifierHash;
        var emailIdentifier = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Email" && x.IdentifierHash == emailHash, ct);
        if (purpose == EmailCodePurpose.Signup)
        {
            if (emailIdentifier is not null && (emailIdentifier.UserId != userId || emailIdentifier.IsVerified)) throw new AuthChallengeInvalidException();
            if (emailIdentifier is null)
                db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Email", IdentifierHash = emailHash, DeliveryAddress = normalizedIdentifier, IsVerified = true, CreatedAtUtc = now });
            else emailIdentifier.IsVerified = true;
            if (!string.IsNullOrWhiteSpace(challenge.PhoneIdentifierHash))
            {
                var phoneIdentifier = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone" && x.IdentifierHash == challenge.PhoneIdentifierHash, ct);
                if (phoneIdentifier is not null && phoneIdentifier.UserId != userId) throw new AuthChallengeInvalidException();
                if (phoneIdentifier is null)
                    db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Phone", IdentifierHash = challenge.PhoneIdentifierHash, IsVerified = false, CreatedAtUtc = now });
            }
        }
        else if (emailIdentifier is null || !emailIdentifier.IsVerified || emailIdentifier.UserId != userId)
            throw new AuthChallengeInvalidException();
        else if (!string.IsNullOrWhiteSpace(challenge.PhoneIdentifierHash))
        {
            var phoneIdentifier = await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone" && x.IdentifierHash == challenge.PhoneIdentifierHash, ct);
            if (phoneIdentifier is null || phoneIdentifier.UserId != userId) throw new AuthChallengeInvalidException();
        }

        challenge.ConsumedAtUtc = now;
        var result = await tokenIssuer.IssueAsync(userId, options.FirebaseProjectId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public Task ResetPinAsync(string email, string code, string newPin, CancellationToken ct)
    {
        // A browser cannot safely implement native-style device PIN reset yet. Never
        // report success or consume a recovery code until a reviewed device credential
        // provider is configured.
        throw new AuthChallengeUnavailableException("Secure device PIN reset is not configured.");
    }

    private void EnsureConfigured()
    {
        if (!delivery.Enabled || !tokenIssuer.Enabled || string.IsNullOrWhiteSpace(options.AuthCodeHashKey))
            throw new AuthChallengeUnavailableException("Email authentication is temporarily unavailable.");
    }

    private static string NormalizeEmail(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!Email.IsMatch(normalized) || normalized.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "Enter a valid email address.");
        return normalized;
    }

    internal static string NormalizeIdentifier(string value, out AuthIdentifierKind kind)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            kind = AuthIdentifierKind.Email;
            return NormalizeEmail(trimmed);
        }
        kind = AuthIdentifierKind.Phone;
        return NormalizePhone(trimmed);
    }

    private static string NormalizePhone(string value)
    {
        var normalized = value.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (!Phone.IsMatch(normalized)) throw new ApplicationFailure(FailureKind.Validation, "Enter a valid phone number.");
        return normalized;
    }

    internal static string HashIdentifier(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class AuthCodeHashing
{
    private const int Iterations = 120_000;
    public static string Hash(string code, string key)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key + code), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string code, string encoded, string key)
    {
        try
        {
            var parts = encoded.Split('$');
            if (parts.Length != 3 || parts[0] != "v1") return false;
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key + code), Convert.FromBase64String(parts[1]), Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }
}

public sealed class DisabledEmailCodeDelivery : IEmailCodeDelivery
{
    public bool Enabled => false;
    public Task SendAsync(string destination, string code, EmailCodePurpose purpose, CancellationToken ct) => throw new AuthChallengeUnavailableException("Email delivery is not configured.");
}

public sealed class DisabledFirebaseCustomTokenIssuer : IFirebaseCustomTokenIssuer
{
    public bool Enabled => false;
    public Task<FirebaseCustomTokenResult> IssueAsync(Guid userId, string projectId, CancellationToken ct) => throw new AuthChallengeUnavailableException("Firebase custom-token signing is not configured.");
}
