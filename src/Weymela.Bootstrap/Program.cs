using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;

static string Required(IReadOnlyDictionary<string, string> values, string name) =>
    values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required option --{name}.");

var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
        throw new ArgumentException("Arguments must be --name value pairs.");
    values[args[i][2..]] = args[++i];
}

var connection = Environment.GetEnvironmentVariable("V3_BOOTSTRAP_CONNECTION")
    ?? throw new InvalidOperationException("V3_BOOTSTRAP_CONNECTION must be supplied outside source control.");
var request = new PlatformAdminBootstrapRequest(
    Required(values, "firebase-project"),
    Required(values, "firebase-uid"),
    Guid.Parse(Required(values, "user-id")),
    DateTime.Parse(Required(values, "valid-after"), null, System.Globalization.DateTimeStyles.RoundtripKind),
    Guid.Parse(Required(values, "operator-user-id")),
    Required(values, "operator-reference"),
    Guid.Parse(Required(values, "correlation-id")),
    Required(values, "idempotency-key"));

var options = new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(connection).Options;
await using var db = new WeymelaDbContext(options);
await PlatformAdminBootstrapTarget.VerifyAsync(db, CancellationToken.None);
var result = await new PlatformAdminBootstrapper(db).ProvisionAsync(request);
Console.WriteLine($"Platform Admin bootstrap {(result.Replayed ? "replayed" : "provisioned")}: user={result.UserId:D}, binding={result.BindingId:D}");
