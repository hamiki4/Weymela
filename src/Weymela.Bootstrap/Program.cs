using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Weymela.Application.Web;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;

static string Required(IReadOnlyDictionary<string, string> values, string name) =>
    values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required option --{name}.");

static int RequiredInt(IReadOnlyDictionary<string, string> values, string name) =>
    int.Parse(Required(values, name), NumberStyles.Integer, CultureInfo.InvariantCulture);

static decimal RequiredDecimal(IReadOnlyDictionary<string, string> values, string name) =>
    decimal.Parse(Required(values, name), NumberStyles.Number, CultureInfo.InvariantCulture);

static decimal? OptionalDecimal(IReadOnlyDictionary<string, string> values, string name) =>
    values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture)
        : null;

var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
        throw new ArgumentException("Arguments must be --name value pairs.");
    values[args[i][2..]] = args[++i];
}

var connection = Environment.GetEnvironmentVariable("V3_BOOTSTRAP_CONNECTION")
    ?? throw new InvalidOperationException("V3_BOOTSTRAP_CONNECTION must be supplied outside source control.");
var options = new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(connection).Options;
await using var db = new WeymelaDbContext(options);
await PlatformAdminBootstrapTarget.VerifyAsync(db, CancellationToken.None);

var operation = values.GetValueOrDefault("operation") ?? "platform-admin";
if (string.Equals(operation, "financial-configuration-v1", StringComparison.Ordinal))
{
    var effective = DateTime.Parse(Required(values, "effective-from"), null,
        DateTimeStyles.RoundtripKind);
    var settings = new FinancialSettingsInput(
        new(RequiredInt(values, "view-only-views-per-reward"),
            RequiredDecimal(values, "view-only-business-pays"),
            RequiredDecimal(values, "view-only-creator-earns"),
            RequiredDecimal(values, "view-only-platform-keeps"),
            OptionalDecimal(values, "view-only-minimum-promotion-budget")),
        new(RequiredInt(values, "view-plus-commission-views-per-reward"),
            RequiredDecimal(values, "view-plus-commission-business-pays"),
            RequiredDecimal(values, "view-plus-commission-creator-earns"),
            RequiredDecimal(values, "view-plus-commission-platform-keeps"),
            OptionalDecimal(values, "view-plus-commission-minimum-promotion-budget")),
        RequiredDecimal(values, "creator-commission-percent"),
        RequiredDecimal(values, "customer-cashback-percent"),
        RequiredDecimal(values, "platform-percent"),
        RequiredDecimal(values, "creator-payout-threshold"),
        RequiredDecimal(values, "customer-payout-threshold"), effective);
    var financialRequest = new FinancialConfigurationBootstrapRequest(
        Guid.Parse(Required(values, "platform-admin-user-id")), settings,
        Required(values, "operator-reference"),
        Guid.Parse(Required(values, "correlation-id")),
        Required(values, "idempotency-key"));
    var financialResult = await new FinancialConfigurationBootstrapper(db).ProvisionAsync(financialRequest);
    Console.WriteLine($"Financial configuration Version 1 bootstrap {(financialResult.Replayed ? "replayed" : "provisioned")}: configuration={financialResult.ConfigurationId:D}, version={financialResult.VersionId:D}, effective={financialResult.EffectiveFromUtc:O}");
    return;
}
if (!string.Equals(operation, "platform-admin", StringComparison.Ordinal))
    throw new ArgumentException("Unsupported bootstrap operation.");

var request = new PlatformAdminBootstrapRequest(
    Required(values, "firebase-project"),
    Required(values, "firebase-uid"),
    Guid.Parse(Required(values, "user-id")),
    DateTime.Parse(Required(values, "valid-after"), null, System.Globalization.DateTimeStyles.RoundtripKind),
    Guid.Parse(Required(values, "operator-user-id")),
    Required(values, "operator-reference"),
    Guid.Parse(Required(values, "correlation-id")),
    Required(values, "idempotency-key"));
var result = await new PlatformAdminBootstrapper(db).ProvisionAsync(request);
Console.WriteLine($"Platform Admin bootstrap {(result.Replayed ? "replayed" : "provisioned")}: user={result.UserId:D}, binding={result.BindingId:D}");
