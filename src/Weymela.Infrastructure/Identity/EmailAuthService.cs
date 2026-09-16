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
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RecoveryGrantLifetime = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 5;

    public async Task<EmailCodeStartOutcome> StartAsync(string identifier, string? phone, EmailCodePurpose purpose, CancellationToken ct)
    {
        var normalizedIdentifier = NormalizeEmail(identifier);
        if (!string.IsNullOrWhiteSpace(phone))
            throw new ApplicationFailure(FailureKind.Validation, "Enter a valid email address.");
        EnsureConfigured();

        var identifierHash = HashIdentifier(normalizedIdentifier);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existingEmail = await db.AuthIdentifiers.AsTracking()
            .SingleOrDefaultAsync(x => x.Kind == "Email" && x.IdentifierHash == identifierHash, ct);

        // Conflicting identifiers never overwrite or merge accounts. The public endpoint
        // returns the same generic response for this branch as for a normal request.
        if (purpose == EmailCodePurpose.Signup && existingEmail is not null)
        {
            await tx.CommitAsync(ct);
            return new(false, now.Add(Lifetime), (int)ResendWindow.TotalSeconds);
        }
        AuthIdentifierRecord? deliveryEmail;
        if (purpose == EmailCodePurpose.Signup)
        {
            deliveryEmail = null;
        }
        else
        {
            deliveryEmail = existingEmail;
            if (deliveryEmail is null || !deliveryEmail.IsVerified)
            {
                await tx.CommitAsync(ct);
                return new(false, now.Add(Lifetime), (int)ResendWindow.TotalSeconds);
            }
        }
        var emailHash = identifierHash;
        var challengeKeyHash = identifierHash;
        var userId = purpose == EmailCodePurpose.Signup ? (Guid?)Guid.NewGuid() : deliveryEmail!.UserId;
        var deliveryAddress = purpose == EmailCodePurpose.Signup ? normalizedIdentifier : deliveryEmail!.DeliveryAddress!;
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
            PhoneIdentifierHash = null,
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
        if (purpose is EmailCodePurpose.PinRecovery or EmailCodePurpose.PasswordRecovery)
            throw new AuthChallengeUnavailableException("Use the secure recovery completion flow.");
        var normalizedIdentifier = NormalizeEmail(email);
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
            if (challenge.PhoneIdentifierHash is not null) throw new AuthChallengeInvalidException();
        }
        else if (emailIdentifier is null || !emailIdentifier.IsVerified || emailIdentifier.UserId != userId)
            throw new AuthChallengeInvalidException();

        challenge.ConsumedAtUtc = now;
        var result = await tokenIssuer.IssueAsync(userId, options.FirebaseProjectId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<PasswordRecoveryGrantResult> VerifyPasswordRecoveryAsync(
        string email,
        string code,
        CancellationToken ct)
    {
        EnsureConfigured();
        var normalizedEmail = NormalizeEmail(email);
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) throw new AuthChallengeInvalidException();
        var identifierHash = HashIdentifier(normalizedEmail);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var challenge = await db.EmailAuthChallenges.AsTracking()
            .Where(x => x.IdentifierHash == identifierHash
                && x.Purpose == EmailCodePurpose.PasswordRecovery.ToString())
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (challenge is null || challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= now
            || challenge.AttemptCount >= challenge.MaxAttempts || challenge.UserId is null)
            throw new AuthChallengeInvalidException();
        challenge.AttemptCount++;
        if (!AuthCodeHashing.Verify(code, challenge.CodeHash, options.AuthCodeHashKey!))
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new AuthChallengeInvalidException();
        }
        var identifier = await db.AuthIdentifiers.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == "Email"
            && x.IdentifierHash == identifierHash && x.UserId == challenge.UserId && x.IsVerified, ct);
        if (identifier is null) throw new AuthChallengeInvalidException();

        var grant = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        challenge.ConsumedAtUtc = now;
        challenge.RecoveryGrantHash = PasswordCredentialService.HashRecoveryGrant(grant);
        challenge.RecoveryGrantExpiresAtUtc = now.Add(RecoveryGrantLifetime);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(grant, challenge.RecoveryGrantExpiresAtUtc.Value);
    }

    private void EnsureConfigured()
    {
        if (!delivery.Enabled || !tokenIssuer.Enabled || string.IsNullOrWhiteSpace(options.AuthCodeHashKey))
            throw new AuthChallengeUnavailableException("Email authentication is temporarily unavailable.");
    }

    internal static string NormalizeEmailAddress(string value) => NormalizeEmail(value);

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

    private static string NormalizePhone(string value) => PhoneNumberNormalizer.Normalize(value);

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
