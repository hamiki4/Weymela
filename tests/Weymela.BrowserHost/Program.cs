using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Weymela.Api;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Operations;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.BrowserHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Claims;
using Weymela.Api.Auth;
using Weymela.Api.Endpoints;

// This executable owns its one disposable database/container. It never accepts an external connection string.
await using var postgres=new PostgreSqlBuilder().WithImage("postgres:17-alpine")
    .WithDatabase("v3_test_browser_"+Guid.NewGuid().ToString("N")).WithUsername("v3_test").WithPassword(Guid.NewGuid().ToString("N"))
    .WithCreateParameterModifier(p=>{foreach(var binding in p.HostConfig.PortBindings.Values.SelectMany(x=>x))binding.HostIP="127.0.0.1";}).Build();
await postgres.StartAsync();
var root=Path.GetFullPath(Environment.GetEnvironmentVariable("V3_SOURCE_ROOT")??Directory.GetCurrentDirectory());
var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var testBuildRevision=Environment.GetEnvironmentVariable("V3_TEST_BUILD_REVISION")??"local";
await using var app=ApiHost.Build(["--environment","Development"],builder=>
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
    {
        ["ConnectionStrings:WeymelaV3"]=postgres.GetConnectionString(),["V3:EnableDevelopmentIdentity"]="true",
        ["V3:DevelopmentAccessKey"]=key,["V3:Auth:CodeHashKey"]=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),["V3:Auth:PinPepper"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),["V3:Auth:FirebaseProjectId"]="isolated-v3-test",["V3:WebRoot"]=Path.Combine(root,"src/Weymela.Web/dist"),["V3:RateLimitMultiplier"]="20",["V3:TestBuildRevision"]=testBuildRevision
    });
    builder.Services.AddSingleton<IEmailCodeDelivery, BrowserEmailCodeDelivery>();
    builder.Services.AddScoped<IFirebaseCustomTokenIssuer, BrowserFirebaseCustomTokenIssuer>();
    builder.Services.AddScoped<IIdentityTokenVerifier, BrowserIdentityTokenVerifier>();
    builder.Services.AddScoped<PersistentWorkspaceDirectory>();
    builder.Services.AddScoped<IWorkspaceDirectory, BrowserWorkspaceDirectory>();
});
await using(var scope=app.Services.CreateAsyncScope())
{
    var db=scope.ServiceProvider.GetRequiredService<WeymelaDbContext>();await db.Database.MigrateAsync();
    await DevelopmentWorkspaceSeed.SeedAsync(db,scope.ServiceProvider.GetRequiredService<DevelopmentDirectory>(),scope.ServiceProvider.GetRequiredService<DevelopmentViewProvider>(),TimeProvider.System);
}
app.MapGet("/__test/email-code", (string identifier) =>
{
    var code = BrowserEmailCodeDelivery.Read(identifier);
    return code is null ? Results.NotFound() : Results.Ok(new { code });
}).AllowAnonymous();
app.MapGet("/__test/build-info", () => Results.Ok(new { revision = testBuildRevision })).AllowAnonymous();
// Disposable BrowserHost-only clock control. It accepts no identity/session input,
// resolves only the authenticated test account's HttpOnly credential, and is never
// mapped by ApiHost in Pilot or Production.
app.MapPost("/__test/device-session/idle", async (HttpContext context, WeymelaDbContext db, CancellationToken ct) =>
{
    if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        || !OpaqueDeviceCredential.TryDigest(
            context.Request.Cookies[DeviceSessionCredentialCookie.DevelopmentName], out var digest))
        return Results.Unauthorized();
    var session = await db.DeviceSessions.SingleOrDefaultAsync(x => x.UserId == userId
        && x.SessionIdentifierHash == digest && x.RevokedAtUtc == null, ct);
    if (session is null) return Results.Unauthorized();
    session.LastActivityAtUtc = DateTime.UtcNow.Subtract(DeviceAccessPolicy.IdleLock).AddSeconds(-1);
    session.LockedAtUtc = null;
    session.Version++;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization("VerifiedAccount");
// Disposable BrowserHost-only state setup for the verified-email recovery flow.
// It resolves only the signed-in test account's HttpOnly device credential and
// is never mapped by ApiHost in Pilot or Production.
app.MapPost("/__test/device/recovery-required", async (HttpContext context, WeymelaDbContext db, CancellationToken ct) =>
{
    if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        || !OpaqueDeviceCredential.TryDigest(
            context.Request.Cookies[DeviceCredentialCookie.Name], out var digest))
        return Results.Unauthorized();
    var device = await db.AuthorizedDevices.SingleOrDefaultAsync(x => x.UserId == userId
        && x.CredentialIdHash == digest && x.RevokedAtUtc == null, ct);
    if (device is null) return Results.Unauthorized();
    device.FailedAttempts = DeviceAccessPolicy.RecoveryAttemptThreshold;
    device.LockedUntilUtc = null;
    device.RequiresRecovery = true;
    device.Version++;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization("VerifiedAccount");
await app.StartAsync();
var worker = Task.Run(async () => {
    var stopping = app.Lifetime.ApplicationStopping;
    try { while (!stopping.IsCancellationRequested) {
        await using var scope=app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkerPump>().RunOnceAsync(stopping);
        await Task.Delay(1000,stopping);
    } } catch(OperationCanceledException) when(stopping.IsCancellationRequested) { }
});
var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var control=Path.Combine(root,".artifacts/browser-host.json");Directory.CreateDirectory(Path.GetDirectoryName(control)!);
await File.WriteAllTextAsync(control,JsonSerializer.Serialize(new{url,accessKey=key}));
if(!OperatingSystem.IsWindows())File.SetUnixFileMode(control,UnixFileMode.UserRead|UnixFileMode.UserWrite);
Console.WriteLine("Isolated V3 browser host ready. Control file: .artifacts/browser-host.json");
await app.WaitForShutdownAsync();
await worker;
