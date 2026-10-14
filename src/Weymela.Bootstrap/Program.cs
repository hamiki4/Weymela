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

static bool RequiredBoolean(IReadOnlyDictionary<string, string> values, string name) =>
    bool.TryParse(Required(values, name), out var value)
        ? value
        : throw new ArgumentException($"Option --{name} must be true or false.");

var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
        throw new ArgumentException("Arguments must be --name value pairs.");
    if (!values.TryAdd(args[i][2..], args[++i]))
        throw new ArgumentException("An option may be supplied only once.");
}

var targetName = values.GetValueOrDefault("target");
if (targetName is not ("pilot" or "production-test" or "production"))
    throw new InvalidOperationException("An explicit bootstrap target is required: pilot, production-test, or production.");
// Financial corrections are a separate maintenance operation. They use the existing
// migrator identity, never the identity-provisioning bootstrap role or a public API.
if (values.GetValueOrDefault("operation") == "ugc-payment-correction")
{
    var names = new Dictionary<string, string?> {
        ["pilot"] = Environment.GetEnvironmentVariable("V3_BOOTSTRAP_PILOT_DATABASE"),
        ["production-test"] = Environment.GetEnvironmentVariable("V3_BOOTSTRAP_PRODUCTION_TEST_DATABASE"),
        ["production"] = Environment.GetEnvironmentVariable("V3_BOOTSTRAP_PRODUCTION_DATABASE") };
    if (names.Values.Any(name => name is null || !PlatformAdminBootstrapTarget.IsV3DatabaseName(name))
        || names.Values.Distinct(StringComparer.Ordinal).Count() != 3)
        throw new InvalidOperationException("Protected, distinct correction target databases are required.");
    var reference = targetName == "production"
        ? Required(values, "production-authorization-reference") : Required(values, "operator-reference");
    var correctionConnection = new Npgsql.NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("V3_BOOTSTRAP_CONNECTION")
            ?? throw new InvalidOperationException("The correction connection must be supplied outside source control."));
    if (correctionConnection.Database != names[targetName] || correctionConnection.Username != "weymela_v3_migrator"
        || !string.IsNullOrEmpty(correctionConnection.Password) || !Path.IsPathFullyQualified(correctionConnection.Passfile ?? ""))
        throw new InvalidOperationException("Correction requires the exact selected V3 database, existing migrator role and protected passfile.");
    await using var correctionDb = new WeymelaDbContext(new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(correctionConnection.ConnectionString).Options);
    await PlatformAdminBootstrapTarget.VerifyAsync(correctionDb, names[targetName]!, "weymela_v3_migrator");
    var correctionJournal = await new UgcPaymentCorrectionService(correctionDb, TimeProvider.System).CorrectAsync(
        new Weymela.Application.Actor(Guid.Parse(Required(values, "platform-admin-user-id")), Weymela.Application.ActorRole.PlatformAdmin),
        Guid.Parse(Required(values, "assignment-id")), Guid.Parse(Required(values, "original-journal-id")),
        reference, Required(values, "idempotency-key"), CancellationToken.None);
    Console.WriteLine($"Auditable UGC correction journal: {correctionJournal:D}");
    return;
}

var targetConfiguration = V3BootstrapTargetConfiguration.FromEnvironment();
var selectedTarget = PlatformAdminBootstrapTarget.Select(targetName, targetConfiguration);
var operation = values.GetValueOrDefault("operation") ?? "platform-admin";
if (operation is not ("platform-admin" or "financial-configuration-v1" or "ugc-payment-correction"))
    throw new ArgumentException("Unsupported bootstrap operation.");
var productionAuthorizationReference = values.GetValueOrDefault("production-authorization-reference");
if (selectedTarget.RequiresProductionAuthorization && string.IsNullOrWhiteSpace(productionAuthorizationReference))
    throw new InvalidOperationException("The Production target requires --production-authorization-reference with the owner approval record.");
if (!selectedTarget.RequiresProductionAuthorization && productionAuthorizationReference is not null)
    throw new InvalidOperationException("--production-authorization-reference is valid only with --target production.");

var connection = Environment.GetEnvironmentVariable("V3_BOOTSTRAP_CONNECTION")
    ?? throw new InvalidOperationException("V3_BOOTSTRAP_CONNECTION must be supplied outside source control.");
var target = PlatformAdminBootstrapTarget.ValidateConnection(selectedTarget, connection);
if (operation == "platform-admin"
    && !string.Equals(Required(values, "firebase-project"), target.FirebaseProjectId, StringComparison.Ordinal))
    throw new InvalidOperationException("Firebase project does not match the explicitly selected bootstrap target.");

var options = new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(connection).Options;
await using var db = new WeymelaDbContext(options);
await PlatformAdminBootstrapTarget.VerifyAsync(db, target, CancellationToken.None);

if (string.Equals(operation, "financial-configuration-v1", StringComparison.Ordinal))
{
    var effective = DateTime.Parse(Required(values, "effective-from"), null,
        DateTimeStyles.RoundtripKind);
    if (values.ContainsKey("inactive") && RequiredBoolean(values, "inactive"))
    {
        // Unknown prices stay absent. This operation cannot create an effective product price.
        var inactive = new InactiveFinancialBaselineRequest(
            Guid.Parse(Required(values, "platform-admin-user-id")),
            RequiredDecimal(values, "customer-cashback-percent"),
            RequiredDecimal(values, "creator-commission-percent"), RequiredDecimal(values, "platform-percent"),
            effective, Required(values, "operator-reference"), Guid.Parse(Required(values, "correlation-id")),
            Required(values, "idempotency-key"), productionAuthorizationReference);
        var baseline = await new FinancialConfigurationBootstrapper(db, target).ProvisionInactiveAsync(inactive);
        Console.WriteLine($"Inactive financial baseline approved: version={baseline.Version}, id={baseline.Id:D}; all products remain disabled.");
        return;
    }
    var ugc = target.RequiresProductionAuthorization
        ? new UgcSettingsInput(RequiredDecimal(values, "ugc-minimum-creator-payment"),
            RequiredDecimal(values, "ugc-platform-fee-percent"), OptionalDecimal(values, "ugc-minimum-budget"),
            OptionalDecimal(values, "ugc-customer-offer-platform-sale-percent"))
        : null;
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
        RequiredDecimal(values, "customer-payout-threshold"), effective,
        ugc,
        target.RequiresProductionAuthorization ? RequiredInt(values, "promotion-live-duration-days") : 30,
        target.RequiresProductionAuthorization && RequiredBoolean(values, "enforce-audience-requirements"));
    var financialRequest = new FinancialConfigurationBootstrapRequest(
        Guid.Parse(Required(values, "platform-admin-user-id")), settings,
        Required(values, "operator-reference"),
        Guid.Parse(Required(values, "correlation-id")),
        Required(values, "idempotency-key"), productionAuthorizationReference);
    var financialResult = await new FinancialConfigurationBootstrapper(db, target).ProvisionAsync(financialRequest);
    Console.WriteLine($"Financial configuration Version 1 bootstrap {(financialResult.Replayed ? "replayed" : "provisioned")}: configuration={financialResult.ConfigurationId:D}, version={financialResult.VersionId:D}, effective={financialResult.EffectiveFromUtc:O}");
    return;
}
var request = new PlatformAdminBootstrapRequest(
    Required(values, "firebase-project"),
    Required(values, "firebase-uid"),
    Guid.Parse(Required(values, "user-id")),
    DateTime.Parse(Required(values, "valid-after"), null, System.Globalization.DateTimeStyles.RoundtripKind),
    Guid.Parse(Required(values, "operator-user-id")),
    Required(values, "operator-reference"),
    Guid.Parse(Required(values, "correlation-id")),
    Required(values, "idempotency-key"), productionAuthorizationReference);
using var productionUidVerifier = target.RequiresProductionAuthorization
    ? new FirebaseAdminUidVerifier(
        Environment.GetEnvironmentVariable("V3_BOOTSTRAP_FIREBASE_ADMIN_CREDENTIALS")
            ?? throw new InvalidOperationException("V3_BOOTSTRAP_FIREBASE_ADMIN_CREDENTIALS must point to the protected Production Firebase Admin credential file."),
        target.FirebaseProjectId)
    : null;
var result = await new PlatformAdminBootstrapper(db, target, productionUidVerifier).ProvisionAsync(request);
Console.WriteLine($"Platform Admin bootstrap {(result.Replayed ? "replayed" : "provisioned")}: user={result.UserId:D}, binding={result.BindingId:D}");
