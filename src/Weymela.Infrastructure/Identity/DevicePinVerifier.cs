using System.Security.Cryptography;
using System.Text;
using Weymela.Application;

namespace Weymela.Infrastructure.Identity;

/// <summary>
/// A five-digit PIN is NOT an Internet password. This verifier is usable only
/// after proof of a random device credential, with durable device attempt limits.
/// PBKDF2-HMAC-SHA256 uses fixed, bounded parameters and an independent external
/// pepper. It deliberately does not reuse the email-code hashing format/key.
/// </summary>
public static class DevicePinVerifier
{
    private const int Iterations = 600_000;
    private static readonly SemaphoreSlim Work = new(2, 2);

    public static void Validate(string pin)
    {
        if (pin is not { Length: 5 } || pin.Any(c => c is < '0' or > '9'))
            throw new ApplicationFailure(FailureKind.Validation, "Enter exactly five digits.");
    }

    public static void EnsureConfigured(string? pepper)
    {
        if (string.IsNullOrWhiteSpace(pepper)) throw Unavailable();
        byte[] key;
        try { key = Convert.FromBase64String(pepper); }
        catch (FormatException) { throw Unavailable(); }
        try { if (key.Length < 32) throw Unavailable(); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static bool ConfirmationMatches(string pin, string confirmation)
    {
        Validate(pin);
        Validate(confirmation);
        var left = Encoding.ASCII.GetBytes(pin);
        var right = Encoding.ASCII.GetBytes(confirmation);
        try { return CryptographicOperations.FixedTimeEquals(left, right); }
        finally { CryptographicOperations.ZeroMemory(left); CryptographicOperations.ZeroMemory(right); }
    }

    public static async Task<string> HashAsync(string pin, string pepper, CancellationToken ct)
    {
        Validate(pin);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = await DeriveAsync(pin, pepper, salt, ct);
        try { return $"pin-v1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }

    public static async Task<bool> VerifyAsync(string pin, string encoded, string pepper, CancellationToken ct)
    {
        Validate(pin);
        if (string.IsNullOrEmpty(encoded)) throw Unavailable();
        var parts = encoded.Split('$');
        if (parts.Length != 3 || parts[0] != "pin-v1") throw Unavailable();
        byte[] salt, expected;
        try { salt = Convert.FromBase64String(parts[1]); expected = Convert.FromBase64String(parts[2]); }
        catch (FormatException) { throw Unavailable(); }
        if (salt.Length != 16 || expected.Length != 32) throw Unavailable();
        var actual = await DeriveAsync(pin, pepper, salt, ct);
        try { return CryptographicOperations.FixedTimeEquals(expected, actual); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }

    private static async Task<byte[]> DeriveAsync(string pin, string pepper, byte[] salt, CancellationToken ct)
    {
        EnsureConfigured(pepper);
        byte[] key;
        try { key = Convert.FromBase64String(pepper); }
        catch (FormatException) { throw Unavailable(); }
        var entered = false;
        try
        {
            if (key.Length < 32) throw Unavailable();
            // No unbounded KDF queue on the small Pilot runtime.
            if (!await Work.WaitAsync(TimeSpan.FromSeconds(1), ct)) throw Unavailable();
            entered = true;
            var pinBytes = Encoding.ASCII.GetBytes(pin);
            try
            {
                var input = HMACSHA256.HashData(key, pinBytes);
                try { return Rfc2898DeriveBytes.Pbkdf2(input, salt, Iterations, HashAlgorithmName.SHA256, 32); }
                finally { CryptographicOperations.ZeroMemory(input); }
            }
            finally { CryptographicOperations.ZeroMemory(pinBytes); }
        }
        finally { CryptographicOperations.ZeroMemory(key); if (entered) Work.Release(); }
    }

    private static AuthChallengeUnavailableException Unavailable() => new("Device unlock is temporarily unavailable.");
}
