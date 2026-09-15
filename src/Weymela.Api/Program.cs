using Microsoft.Extensions.Configuration;
using Weymela.Api;
using Weymela.Api.Auth;
using Weymela.Infrastructure.Operations;

if (args is ["--probe-data-protection-restart"])
{
    try
    {
        if (!OperatingSystem.IsLinux()
            || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Pilot"
            || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Pilot")
            throw new InvalidOperationException("The Data Protection probe requires the approved Pilot environment.");
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var options = RuntimeOptions.Load(configuration, "Pilot");
        DataProtectionRestartProbe.Verify(
            options.CookieCertificatePath,
            options.CookieCertificatePassword,
            options.CookieKeyDirectory,
            "WeymelaV3-" + options.EnvironmentName + "-" + options.FirebaseProjectId,
            DateTime.UtcNow);
        Console.WriteLine("{\"dataProtectionRestart\":\"passed\"}");
        return;
    }
    catch
    {
        Console.Error.WriteLine("Data Protection restart probe failed; review protected paths privately.");
        Environment.ExitCode = 1;
        return;
    }
}

var app=ApiHost.Build(args);
app.Run();
public partial class Program;
