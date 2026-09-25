using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Api.Auth;

public static class LiveAuthentication
{
    public static void Add(IServiceCollection services, RuntimeOptions options)
    {
        services.AddHttpClient("FirebasePublicKeys", c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 2 });
        services.TryAddSingleton<IIdentitySigningKeys>(sp => new FirebaseSigningKeys(sp.GetRequiredService<IHttpClientFactory>().CreateClient("FirebasePublicKeys"), sp.GetRequiredService<TimeProvider>()));
        services.TryAddScoped<IIdentityTokenVerifier, FirebaseTokenVerifier>(); services.AddScoped<TrustedIdentityService>();
        if (options.Development) services.AddDataProtection().UseEphemeralDataProtectionProvider();
        else
        {
            X509Certificate2 certificate;
            try { certificate = X509CertificateLoader.LoadPkcs12FromFile(options.CookieCertificatePath, options.CookieCertificatePassword); }
            catch { throw new InvalidOperationException("The externally protected cookie-key certificate is unavailable."); }
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                throw new InvalidOperationException("A current private cookie-key protection certificate is required.");
            services.AddDataProtection().SetApplicationName("WeymelaV3-" + options.EnvironmentName + "-" + options.FirebaseProjectId)
                .PersistKeysToFileSystem(new DirectoryInfo(options.CookieKeyDirectory)).ProtectKeysWithCertificate(certificate);
        }
        services.AddAuthentication(WorkspaceAuthentication.Scheme).AddCookie(WorkspaceAuthentication.Scheme, o =>
        {
            o.Cookie.Name = options.Development ? "WeymelaV3.Session" : "__Host-WeymelaV3.Session";
            o.Cookie.Path = "/"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = options.Development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(1); o.SlidingExpiration = false;
            o.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; },
                OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; },
                OnValidatePrincipal = async c =>
                {
                    if (!Guid.TryParse(c.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
                    { c.RejectPrincipal(); return; }
                    var db = c.HttpContext.RequestServices.GetRequiredService<WeymelaDbContext>();
                    if (await db.AccountLifecycles.AsNoTracking().AnyAsync(x => x.UserId == userId
                        && (x.Status == AccountLifecycleStatus.Suspended || x.Status == AccountLifecycleStatus.Disabled
                            || x.Status == AccountLifecycleStatus.Revoked || x.Status == AccountLifecycleStatus.Closed), c.HttpContext.RequestAborted))
                    { c.RejectPrincipal(); return; }
                    if (options.DevelopmentIdentity && c.Principal?.FindFirst("identity-binding") is null) return;
                    if (!Guid.TryParse(c.Principal?.FindFirst("identity-binding")?.Value, out var id)
                        || !long.TryParse(c.Principal.FindFirst("identity-version")?.Value, out var version)
                        || !DateTime.TryParseExact(c.Principal.FindFirst("authenticated-at")?.Value,"O",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var authAt)) { c.RejectPrincipal(); return; }
                    if (!await db.IdentityBindings.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId && x.Provider == "Firebase" && x.ProjectId == options.FirebaseProjectId && x.Version == version && x.ValidAfterUtc<=authAt && x.IsActive, c.HttpContext.RequestAborted)) c.RejectPrincipal();
                }
            };
        });
    }
}
