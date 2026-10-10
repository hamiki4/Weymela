using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

/// <summary>
/// Trusted, operator-run provisioning for the first Platform Admin. There is
/// deliberately no HTTP endpoint for this operation.
/// </summary>
public sealed record PlatformAdminBootstrapRequest(
    string FirebaseProjectId,
    string FirebaseUid,
    Guid UserId,
    DateTime ValidAfterUtc,
    Guid OperatorUserId,
    string OperatorReference,
    Guid CorrelationId,
    string IdempotencyKey,
    string? ProductionAuthorizationReference = null);

public sealed record PlatformAdminBootstrapResult(Guid UserId, Guid BindingId, bool Replayed);

public sealed record V3BootstrapTarget(string Name, string DatabaseName, string BootstrapRole,
    string FirebaseProjectId, bool RequiresProductionAuthorization = false);

public sealed record V3BootstrapTargetConfiguration(
    string PilotDatabaseName,
    string PilotBootstrapRole,
    string ProductionTestDatabaseName,
    string ProductionTestBootstrapRole,
    string ProductionDatabaseName,
    string ProductionBootstrapRole)
{
    public static V3BootstrapTargetConfiguration FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string Required(string name) =>
            read(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Protected bootstrap target setting {name} is required.");

        return new(
            Required("V3_BOOTSTRAP_PILOT_DATABASE"),
            Required("V3_BOOTSTRAP_PILOT_ROLE"),
            Required("V3_BOOTSTRAP_PRODUCTION_TEST_DATABASE"),
            Required("V3_BOOTSTRAP_PRODUCTION_TEST_ROLE"),
            Required("V3_BOOTSTRAP_PRODUCTION_DATABASE"),
            Required("V3_BOOTSTRAP_PRODUCTION_ROLE"));
    }

    internal void Validate()
    {
        var entries = new[]
        {
            (PilotDatabaseName, PilotBootstrapRole),
            (ProductionTestDatabaseName, ProductionTestBootstrapRole),
            (ProductionDatabaseName, ProductionBootstrapRole)
        };
        if (entries.Any(entry => !PlatformAdminBootstrapTarget.IsV3DatabaseName(entry.Item1)
                || !PlatformAdminBootstrapTarget.IsV3BootstrapRole(entry.Item2))
            || entries.Select(entry => entry.Item1).Distinct(StringComparer.Ordinal).Count() != entries.Length
            || entries.Select(entry => entry.Item2).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new InvalidOperationException(
                "Protected bootstrap target settings must define three distinct V3 database and bootstrap-role pairs.");
    }
}

public interface IProductionFirebaseUidVerifier
{
    Task VerifyAsync(string projectId, string uid, CancellationToken cancellationToken);
}

public static class PlatformAdminBootstrapTarget
{
    public static V3BootstrapTarget Select(string? target, V3BootstrapTargetConfiguration configuration)
    {
        configuration.Validate();
        var selected = target switch
        {
            "pilot" => new V3BootstrapTarget("pilot", configuration.PilotDatabaseName,
                configuration.PilotBootstrapRole, "weymela-pilot"),
            "production-test" => new V3BootstrapTarget("production-test", configuration.ProductionTestDatabaseName,
                configuration.ProductionTestBootstrapRole, "weymela-production"),
            "production" => new V3BootstrapTarget("production", configuration.ProductionDatabaseName,
                configuration.ProductionBootstrapRole, "weymela-production", true),
            _ => throw new InvalidOperationException("An explicit bootstrap target is required: pilot, production-test, or production.")
        };
        return ValidateSelected(selected);
    }

    public static V3BootstrapTarget ValidateSelected(V3BootstrapTarget selected)
    {
        var expected = selected.Name switch
        {
            "pilot" => (ProjectId: "weymela-pilot", RequiresProductionAuthorization: false),
            "production-test" => (ProjectId: "weymela-production", RequiresProductionAuthorization: false),
            "production" => (ProjectId: "weymela-production", RequiresProductionAuthorization: true),
            _ => throw new InvalidOperationException("An explicit bootstrap target is required: pilot, production-test, or production.")
        };
        if (selected.FirebaseProjectId != expected.ProjectId
            || selected.RequiresProductionAuthorization != expected.RequiresProductionAuthorization
            || !IsV3DatabaseName(selected.DatabaseName)
            || !IsV3BootstrapRole(selected.BootstrapRole))
            throw new InvalidOperationException("Bootstrap target configuration is invalid for the selected V3 environment.");
        return selected;
    }

    public static bool IsV3DatabaseName(string value) => IsPostgresIdentifier(value)
        && value.StartsWith("weymela_v3_", StringComparison.Ordinal);

    public static bool IsV3BootstrapRole(string value) => IsPostgresIdentifier(value)
        && value.StartsWith("weymela_v3_", StringComparison.Ordinal)
        && value.EndsWith("_bootstrap", StringComparison.Ordinal);

    private static bool IsPostgresIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 63
        && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static bool IsValidProductionAuthorizationReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 160
        && value.All(character => char.IsAsciiLetterOrDigit(character) || "-_.:/#".Contains(character));

    public static V3BootstrapTarget Resolve(string? target, string connectionString,
        V3BootstrapTargetConfiguration configuration)
        => ValidateConnection(Select(target, configuration), connectionString);

    public static V3BootstrapTarget ValidateConnection(V3BootstrapTarget selected, string connectionString)
    {
        ValidateSelected(selected);
        NpgsqlConnectionStringBuilder connection;
        try { connection = new NpgsqlConnectionStringBuilder(connectionString); }
        catch { throw new InvalidOperationException("Invalid V3 bootstrap connection configuration."); }
        if (!string.Equals(connection.Database, selected.DatabaseName, StringComparison.Ordinal)
            || !string.Equals(connection.Username, selected.BootstrapRole, StringComparison.Ordinal))
            throw new InvalidOperationException("Bootstrap connection does not match the explicitly selected isolated target.");
        return selected;
    }

    public static Task VerifyAsync(WeymelaDbContext db, V3BootstrapTarget target,
        CancellationToken cancellationToken = default)
        => VerifyAsync(db, target.DatabaseName, target.BootstrapRole, cancellationToken);

    public static async Task VerifyAsync(WeymelaDbContext db, string expectedDatabase, string expectedRole, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database(), current_user";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(0), expectedDatabase, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), expectedRole, StringComparison.Ordinal))
            throw new InvalidOperationException("Bootstrap target guard failed: database and role do not match the selected isolated target.");
    }
}

public sealed class PlatformAdminBootstrapper(WeymelaDbContext db, V3BootstrapTarget target,
    IProductionFirebaseUidVerifier? productionFirebaseUidVerifier = null)
{
    private const string Operation = "PlatformAdminBootstrap";
    private readonly V3BootstrapTarget _target = PlatformAdminBootstrapTarget.ValidateSelected(target);

    public async Task<PlatformAdminBootstrapResult> ProvisionAsync(PlatformAdminBootstrapRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, _target);
        if (_target.RequiresProductionAuthorization)
        {
            if (productionFirebaseUidVerifier is null)
                throw new InvalidOperationException("Production bootstrap requires Firebase Admin verification of the selected Production UID.");
            await productionFirebaseUidVerifier.VerifyAsync(_target.FirebaseProjectId, request.FirebaseUid, cancellationToken);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var fingerprint = Fingerprint(request);
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == request.OperatorUserId
            && x.OperationType == Operation && x.Key == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(existing.RequestFingerprint), Encoding.UTF8.GetBytes(fingerprint)))
                throw new InvalidOperationException("The bootstrap idempotency key was already used for a different request.");
            var replayId = Guid.Parse(existing.ResultReference);
            var replayedBinding = await db.IdentityBindings.SingleAsync(x => x.Id == replayId, cancellationToken);
            var replayedBindings = await db.IdentityBindings.Where(x => x.UserId == request.UserId
                || (x.Provider == "Firebase" && x.ProjectId == request.FirebaseProjectId
                    && x.ExternalSubject == request.FirebaseUid)).ToListAsync(cancellationToken);
            var replayedPermission = await db.CommercePermissions.Where(x =>
                x.Role == ActorRole.PlatformAdmin && x.IsActive).ToListAsync(cancellationToken);
            if (replayedPermission.Count != 1 || replayedPermission[0].UserId != request.UserId
                || replayedPermission[0].SubjectId != request.UserId || replayedPermission[0].BusinessId is not null)
                throw new InvalidOperationException("The bootstrapped Platform Admin is no longer active; bootstrap replay is closed.");
            var replayedExternalBindings = replayedBindings.Where(x => x.Provider == "Firebase"
                && x.ProjectId == request.FirebaseProjectId && x.ExternalSubject == request.FirebaseUid).ToList();
            var replayedUserBindings = replayedBindings.Where(x => x.UserId == request.UserId).ToList();
            var validatedReplayBinding = RequireExistingBinding(replayedExternalBindings, replayedUserBindings, request);
            if (replayedBinding.Id != validatedReplayBinding.Id)
                throw new InvalidOperationException("The bootstrap replay identity binding does not match the original request.");
            await transaction.CommitAsync(cancellationToken);
            return new(replayedBinding.UserId, replayedBinding.Id, true);
        }

        if (await db.CommercePermissions.AnyAsync(x => x.Role == ActorRole.PlatformAdmin && x.IsActive, cancellationToken))
            throw new InvalidOperationException("A Trusted Platform Admin already exists; first-admin bootstrap is closed.");

        var externalBindings = await db.IdentityBindings.Where(x => x.Provider == "Firebase"
            && x.ProjectId == request.FirebaseProjectId && x.ExternalSubject == request.FirebaseUid)
            .ToListAsync(cancellationToken);
        var userBindings = await db.IdentityBindings.Where(x => x.UserId == request.UserId)
            .ToListAsync(cancellationToken);
        var binding = RequireExistingBinding(externalBindings, userBindings, request);

        var activePermissions = await db.CommercePermissions.Where(x => x.UserId == request.UserId && x.IsActive).ToListAsync(cancellationToken);
        if (activePermissions.Any(x => x.Role != ActorRole.PlatformAdmin || x.SubjectId != request.UserId || x.BusinessId is not null))
            throw new InvalidOperationException("The V3 user has a conflicting active workspace permission.");
        if (activePermissions.Any(x => x.Role == ActorRole.PlatformAdmin && x.SubjectId == request.UserId && x.BusinessId is null))
            throw new InvalidOperationException("A Platform Admin permission exists without its bootstrap idempotency record; operator review is required.");

        var bindingId = binding.Id;
        db.CommercePermissions.Add(new CommercePermission(request.UserId, ActorRole.PlatformAdmin, request.UserId, null, true, false));
        db.IdempotencyRecords.Add(new StoredIdempotencyRecord(request.OperatorUserId, Operation, request.IdempotencyKey,
            fingerprint, bindingId.ToString(), DateTime.UtcNow));
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "PlatformAdminBootstrapProvisioned", request.OperatorUserId, null, null, null,
            request.CorrelationId, DateTime.UtcNow,
            $"operator={request.OperatorReference};target={_target.Name};authorization={request.ProductionAuthorizationReference ?? "none"};targetUserId={request.UserId:D};bindingId={bindingId:D}"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(request.UserId, bindingId, false);
    }

    internal static IdentityBinding RequireExistingBinding(
        IReadOnlyList<IdentityBinding> externalBindings,
        IReadOnlyList<IdentityBinding> userBindings,
        PlatformAdminBootstrapRequest request)
    {
        if (externalBindings.Count != 1 || userBindings.Count != 1)
            throw new InvalidOperationException("An unambiguous existing Firebase identity binding is required for first-admin bootstrap.");

        var binding = externalBindings[0];
        var requestedValidAfter = request.ValidAfterUtc.ToUniversalTime();
        if (userBindings[0].Id != binding.Id
            || binding.Provider != "Firebase"
            || binding.ProjectId != request.FirebaseProjectId
            || binding.ExternalSubject != request.FirebaseUid
            || binding.UserId != request.UserId
            || !binding.IsActive
            || binding.Version <= 0
            || binding.ValidAfterUtc > DateTime.UtcNow
            || binding.ValidAfterUtc != requestedValidAfter)
            throw new InvalidOperationException("The supplied Firebase identity binding is inactive, invalid, or mapped to a different V3 user.");

        return binding;
    }

    public static string Fingerprint(PlatformAdminBootstrapRequest request)
    {
        var canonical = string.Join("|", request.FirebaseProjectId, request.FirebaseUid, request.UserId.ToString("D"),
            request.ValidAfterUtc.ToUniversalTime().ToString("O"), request.OperatorUserId.ToString("D"), request.OperatorReference,
            request.CorrelationId.ToString("D"), request.IdempotencyKey, request.ProductionAuthorizationReference ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void Validate(PlatformAdminBootstrapRequest request, V3BootstrapTarget target)
    {
        if (!string.Equals(request.FirebaseProjectId, target.FirebaseProjectId, StringComparison.Ordinal))
            throw new InvalidOperationException("Firebase project does not match the explicitly selected bootstrap target.");
        if (target.RequiresProductionAuthorization
            && !PlatformAdminBootstrapTarget.IsValidProductionAuthorizationReference(
                request.ProductionAuthorizationReference))
            throw new InvalidOperationException("Production bootstrap requires a bounded owner authorization reference.");
        if (!target.RequiresProductionAuthorization && request.ProductionAuthorizationReference is not null)
            throw new InvalidOperationException("A Production authorization reference is valid only for the Production target.");
        if (string.IsNullOrWhiteSpace(request.FirebaseUid) || request.FirebaseUid.Length > 128)
            throw new ArgumentException("A valid Firebase UID is required.", nameof(request));
        if (request.UserId == Guid.Empty || request.OperatorUserId == Guid.Empty || request.CorrelationId == Guid.Empty)
            throw new ArgumentException("User, operator and correlation identifiers are required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OperatorReference) || request.OperatorReference.Length > 160)
            throw new ArgumentException("A bounded operator reference is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 160)
            throw new ArgumentException("A bounded idempotency key is required.", nameof(request));
        if (request.ValidAfterUtc.Kind == DateTimeKind.Unspecified)
            throw new ArgumentException("ValidAfterUtc must include an explicit UTC kind.", nameof(request));
    }
}
