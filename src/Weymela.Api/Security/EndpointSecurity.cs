using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Weymela.Application;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api.Security;

public static class EndpointSecurity
{
    public static string Operation(HttpContext c) => (c.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
    public static string Category(HttpContext c)
    {
        var route = Operation(c);
        // The access-key-protected development persona endpoint is a deterministic
        // BrowserHost fixture. Keep it bounded, but do not let fixture setup consume
        // the production-shaped Firebase/session authentication partition.
        if (route == "/api/development/session") return "development-fixture";
        // Device enrollment mutates credential state and stays on the strict auth
        // limiter. Its authenticated, privacy-safe status projection is an ordinary
        // account read so normal page loads do not consume sign-in attempt capacity.
        if (route.Contains("/session") || route.Contains("/auth/")
            || route.Contains("/device/") && !HttpMethods.IsGet(c.Request.Method)) return "auth";
        if (route.Contains("/notifications")) return "notifications";
        if (route.Contains("/manual")) return "lookup";
        if (route.Contains("/checkout")) return "checkout";
        if (route.EndsWith("/qr")) return "qr";
        if (route.EndsWith("/refresh") || route.EndsWith("/content")) return "views";
        if (route.EndsWith("/join")) return "join";
        if (route.Contains("deposit")) return "deposit";
        if (route.Contains("/payout") || route.Contains("/settlement")) return "payout";
        if (route.Contains("financial-settings")) return "settings";
        return HttpMethods.IsGet(c.Request.Method) ? "reads" : "writes";
    }
    public static bool Financial(HttpContext c)
    {
        if (HttpMethods.IsGet(c.Request.Method) || HttpMethods.IsOptions(c.Request.Method)) return false;
        var route = Operation(c); var category = Category(c);
        return category is "checkout" or "qr" or "views" or "deposit" or "payout" or "settings"
            || route.EndsWith("/fund") || route.EndsWith("/approve") || route.EndsWith("/increase");
    }
    public static void AddLimits(IServiceCollection services, RuntimeOptions config) => services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
        options.OnRejected = async (context, ct) =>
        {
            OperationalTelemetry.RateLimited.Add(1);
            var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(new { code = "RateLimited", message = "Too many requests. Please wait a moment and try again.", retryAfterSeconds = seconds }, ct);
        };
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("api-capacity", _ => new() { PermitLimit = 16, QueueLimit = 0 })),
            PartitionedRateLimiter.Create<HttpContext, string>(c =>
            {
                var category = Category(c);
                var identity = category is "auth" or "development-fixture" ? c.Connection.RemoteIpAddress?.ToString() ?? "local" : c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? c.Connection.RemoteIpAddress?.ToString() ?? "local";
                var limit = category switch { "auth" or "development-fixture" => 20, "qr" => 20, "checkout" => 60, "lookup" => 10, "views" => 12, "join" => 10, "deposit" => 10, "payout" => 10, "settings" => 12, "notifications" => 90, "reads" => 240, _ => 60 };
                return RateLimitPartition.GetFixedWindowLimiter(category + ":" + identity, _ => new() { PermitLimit = limit * config.RateLimitMultiplier, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            }));
    });
}

public sealed class ValidatedInputFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var value in context.Arguments)
        {
            if (value is Guid id) InputRules.Id(id);
            else if (value?.GetType().Namespace is {} ns && (ns.StartsWith("Weymela.Application") || ns.StartsWith("Weymela.Api.Endpoints"))) Validate(value, 0);
        }
        return next(context);
    }
    private static void Validate(object? value, int depth)
    {
        if (value is null || depth > 6) return;
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0) continue;
            var input = property.GetValue(value); var name = property.Name;
            if(input is null&&new NullabilityInfoContext().Create(property).ReadState==NullabilityState.NotNull)
                throw new ApplicationFailure(FailureKind.Validation,"A required field is missing.");
            if (input is string text)
            {
                var max = name switch { "Title" => 120, "Description" => 3000, "Requirements" or "ContentConcept" => 2000, "Message" => 1000,
                    "Category" or "Region" => 80, "Token" => 43, "IdToken" => 8192, "AccessKey" => 256, "ExternalContentId" => 100, _ => 200 };
                InputRules.Text(text, max);
            }
            else if (input is decimal number)
            {
                var percent = name.Contains("Percent");
                if (number < 0 || number > (percent ? 100 : 9999999999999999.99m) || decimal.Round(number, percent ? 4 : 2) != number)
                    throw new ApplicationFailure(FailureKind.Validation, "Check the amount, rate and decimal precision.");
            }
            else if (input is Guid id) InputRules.Id(id);
            else if (input is long count && count < 0 || input is int n && n < 0) throw new ApplicationFailure(FailureKind.Validation, "Counts and versions cannot be negative.");
            else if (input is DateTime date && (date.Kind != DateTimeKind.Utc || date.Year < 2000 || date.Year > 2100)) throw new ApplicationFailure(FailureKind.Validation, "Use valid UTC dates.");
            else if (input?.GetType().Namespace?.StartsWith("Weymela.Application") == true) Validate(input, depth + 1);
        }
    }
}
