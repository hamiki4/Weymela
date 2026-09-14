using System.Security.Cryptography;
using Weymela.Application;
using Weymela.Infrastructure.Identity;
using Xunit;

namespace Weymela.Infrastructure.Tests;

// No external services or live credentials. The device/session integration is
// still blocked by review and is deliberately not claimed by these KDF tests.
public sealed class DevicePinVerifierTests
{
    private static string Pepper() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("123456")]
    [InlineData("12a45")]
    [InlineData("１２３４５")]
    [InlineData(" 1234")]
    [InlineData("1234 ")]
    [InlineData(null)]
    public void Only_exactly_five_ascii_digits_are_accepted(string? pin)
        => Assert.Throws<ApplicationFailure>(() => DevicePinVerifier.Validate(pin!));

    [Fact]
    public async Task Leading_zero_pin_round_trips_with_random_per_device_salts()
    {
        var pepper = Pepper();
        var first = await DevicePinVerifier.HashAsync("01234", pepper, default);
        var second = await DevicePinVerifier.HashAsync("01234", pepper, default);
        Assert.NotEqual(first, second);
        Assert.Equal("pin-v1", first.Split('$')[0]);
        Assert.Equal(16, Convert.FromBase64String(first.Split('$')[1]).Length);
        Assert.Equal(32, Convert.FromBase64String(first.Split('$')[2]).Length);
        Assert.NotEqual("01234", first.Split('$')[2]);
        Assert.True(await DevicePinVerifier.VerifyAsync("01234", first, pepper, default));
        Assert.True(await DevicePinVerifier.VerifyAsync("01234", second, pepper, default));
        Assert.False(await DevicePinVerifier.VerifyAsync("01235", first, pepper, default));
        Assert.False(await DevicePinVerifier.VerifyAsync("01234", first, Pepper(), default));
    }

    [Fact]
    public async Task Missing_pepper_and_corrupt_or_email_code_verifiers_fail_closed()
    {
        await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => DevicePinVerifier.HashAsync("12345", "", default));
        await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => DevicePinVerifier.HashAsync("12345", null!, default));
        await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => DevicePinVerifier.VerifyAsync("12345", null!, Pepper(), default));
        foreach (var malformed in new[] { "", "pin-v1$bad$bad", "v1$code$hash", "pin-v1$$" })
            await Assert.ThrowsAsync<AuthChallengeUnavailableException>(() => DevicePinVerifier.VerifyAsync("12345", malformed, Pepper(), default));
    }

    [Fact]
    public void Enrollment_confirmation_is_exact_and_crypto_configuration_fails_closed()
    {
        Assert.True(DevicePinVerifier.ConfirmationMatches("01234", "01234"));
        Assert.False(DevicePinVerifier.ConfirmationMatches("01234", "01235"));
        Assert.Throws<ApplicationFailure>(() => DevicePinVerifier.ConfirmationMatches("1234", "1234"));
        Assert.Throws<ApplicationFailure>(() => DevicePinVerifier.ConfirmationMatches("１２３４５", "１２３４５"));

        DevicePinVerifier.EnsureConfigured(Pepper());
        Assert.Throws<AuthChallengeUnavailableException>(() => DevicePinVerifier.EnsureConfigured(null));
        Assert.Throws<AuthChallengeUnavailableException>(() => DevicePinVerifier.EnsureConfigured("not-base64"));
        Assert.Throws<AuthChallengeUnavailableException>(() =>
            DevicePinVerifier.EnsureConfigured(Convert.ToBase64String(RandomNumberGenerator.GetBytes(31))));
    }
}
