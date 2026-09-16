using Microsoft.AspNetCore.Http;
using Weymela.Api.Auth;
using Weymela.Infrastructure.Identity;
using Xunit;

namespace Weymela.Api.IntegrationTests;

public sealed class DeviceSessionCookiePolicyTests
{
    private static readonly DateTime Start = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Prepared_cookie_is_host_only_http_only_strict_and_fixed_to_session_expiry()
    {
        var session = Snapshot(Start.AddHours(1));
        var production = DeviceSessionCredentialCookie.Options(development: false, session);
        var development = DeviceSessionCredentialCookie.Options(development: true, session);

        Assert.Equal("__Host-WeymelaV3.DeviceSession", DeviceSessionCredentialCookie.Name(development: false));
        Assert.Equal("WeymelaV3.DeviceSession", DeviceSessionCredentialCookie.Name(development: true));
        Assert.True(production.HttpOnly);
        Assert.True(production.Secure);
        Assert.False(development.Secure);
        Assert.Equal(SameSiteMode.Strict, production.SameSite);
        Assert.Equal("/", production.Path);
        Assert.Null(production.Domain);
        Assert.Null(production.MaxAge);
        Assert.Equal(new DateTimeOffset(session.ExpiresAtUtc), production.Expires);

        var deletion = DeviceSessionCredentialCookie.DeleteOptions(development: false);
        Assert.True(deletion.HttpOnly);
        Assert.True(deletion.Secure);
        Assert.Equal(SameSiteMode.Strict, deletion.SameSite);
        Assert.Equal("/", deletion.Path);
        Assert.Null(deletion.Domain);
    }

    [Fact]
    public void Prepared_cookie_rejects_a_non_fixed_or_non_utc_expiry()
    {
        Assert.Throws<ArgumentException>(() => DeviceSessionCredentialCookie.Options(false,
            Snapshot(Start.AddHours(1).AddTicks(1))));
        Assert.Throws<ArgumentException>(() => DeviceSessionCredentialCookie.Options(false,
            Snapshot(DateTime.SpecifyKind(Start.AddHours(1), DateTimeKind.Unspecified))));
    }

    [Fact]
    public void Password_recovery_cookie_is_host_only_http_only_secure_strict_and_short_lived()
    {
        var expires = Start.AddMinutes(10);
        var production = PasswordRecoveryTransactionCookie.Options(false, expires);
        var development = PasswordRecoveryTransactionCookie.Options(true, expires);

        Assert.Equal("__Host-WeymelaV3.PasswordRecovery", PasswordRecoveryTransactionCookie.Name(false));
        Assert.Equal("WeymelaV3.PasswordRecovery", PasswordRecoveryTransactionCookie.Name(true));
        Assert.True(production.HttpOnly);
        Assert.True(production.Secure);
        Assert.False(development.Secure);
        Assert.Equal(SameSiteMode.Strict, production.SameSite);
        Assert.Equal("/", production.Path);
        Assert.Null(production.Domain);
        Assert.Equal(new DateTimeOffset(expires), production.Expires);

        var deletion = PasswordRecoveryTransactionCookie.DeleteOptions(false);
        Assert.True(deletion.HttpOnly);
        Assert.True(deletion.Secure);
        Assert.Equal(SameSiteMode.Strict, deletion.SameSite);
        Assert.Equal("/", deletion.Path);
        Assert.Null(deletion.Domain);
    }

    private static DeviceSessionSnapshot Snapshot(DateTime expiresAtUtc) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start, Start, null, expiresAtUtc, 1, 0);
}
