using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Finance;

public sealed record FinancialConfigurationBootstrapRequest(
    Guid PlatformAdminUserId,
    FinancialSettingsInput Settings,
    string OperatorReference,
    Guid CorrelationId,
    string IdempotencyKey);

public sealed record FinancialConfigurationBootstrapResult(
    Guid ConfigurationId,
    Guid VersionId,
    int Version,
    DateTime EffectiveFromUtc,
    bool Replayed);

/// <summary>
/// Trusted, operator-run provisioning for the first effective Platform pricing version.
/// There is deliberately no HTTP endpoint for this one-time operation.
/// </summary>
public sealed class FinancialConfigurationBootstrapper(WeymelaDbContext db, TimeProvider? clock = null)
{
    private const string Operation = "FinancialConfigurationBootstrap";
    private const string ConfigurationName = "PlatformPricing";
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<FinancialConfigurationBootstrapResult> ProvisionAsync(
        FinancialConfigurationBootstrapRequest request, CancellationToken cancellationToken = default)
    {
        var now = Now;
        Validate(request, now);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var fingerprint = Fingerprint(request);
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(x =>
            x.ActorId == request.PlatformAdminUserId && x.OperationType == Operation
            && x.Key == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(existing.RequestFingerprint),
                    Encoding.UTF8.GetBytes(fingerprint)))
                throw new InvalidOperationException(
                    "The financial bootstrap idempotency key was already used for a different request.");
            var versionId = Guid.Parse(existing.ResultReference);
            var version = await db.FinancialConfigurationVersions.SingleAsync(x => x.Id == versionId,
                cancellationToken);
            var root = await db.FinancialConfigurations.SingleAsync(x =>
                x.Id == version.ConfigurationId && x.Name == ConfigurationName, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(root.Id, version.Id, version.Version, version.EffectiveFromUtc, true);
        }

        await RequirePlatformAdminAsync(request.PlatformAdminUserId, now, cancellationToken);
        if (await db.FinancialConfigurations.AnyAsync(cancellationToken)
            || await db.FinancialConfigurationVersions.AnyAsync(cancellationToken))
            throw new InvalidOperationException(
                "Financial configuration already exists; first-configuration bootstrap is closed.");

        var configurationId = Guid.NewGuid();
        var versionIdToCreate = Guid.NewGuid();
        var effective = request.Settings.EffectiveFromUtc!.Value;
        var versionToCreate = FinancialConfigurationVersionFactory.Create(versionIdToCreate,
            configurationId, 1, request.PlatformAdminUserId, effective, request.Settings);

        db.FinancialConfigurations.Add(new FinancialConfiguration(configurationId, ConfigurationName));
        db.FinancialConfigurationVersions.Add(versionToCreate);
        db.IdempotencyRecords.Add(new StoredIdempotencyRecord(request.PlatformAdminUserId, Operation,
            request.IdempotencyKey, fingerprint, versionIdToCreate.ToString("D"), now));
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "FinancialConfigurationBootstrapProvisioned",
            request.PlatformAdminUserId, null, null, null, request.CorrelationId, now,
            $"operator={request.OperatorReference};configurationId={configurationId:D};versionId={versionIdToCreate:D};version=1;effectiveFromUtc={effective:O}"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(configurationId, versionIdToCreate, 1, effective, false);
    }

    public static string Fingerprint(FinancialConfigurationBootstrapRequest request)
    {
        var canonical = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task RequirePlatformAdminAsync(Guid userId, DateTime now,
        CancellationToken cancellationToken)
    {
        var permissions = await db.CommercePermissions.Where(x =>
            x.Role == ActorRole.PlatformAdmin && x.IsActive).ToListAsync(cancellationToken);
        if (permissions.Count != 1 || permissions[0].UserId != userId
            || permissions[0].SubjectId != userId || permissions[0].BusinessId is not null)
            throw new InvalidOperationException(
                "The existing trusted Platform Admin must control financial bootstrap.");

        var bindings = await db.IdentityBindings.Where(x => x.UserId == userId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (bindings.Count != 1 || bindings[0].Provider != "Firebase"
            || bindings[0].ProjectId != "weymela-pilot"
            || string.IsNullOrWhiteSpace(bindings[0].ExternalSubject)
            || bindings[0].Version <= 0 || bindings[0].ValidAfterUtc > now)
            throw new InvalidOperationException(
                "The trusted Platform Admin identity binding is not valid for Pilot.");
    }

    private static void Validate(FinancialConfigurationBootstrapRequest request, DateTime now)
    {
        if (request.PlatformAdminUserId == Guid.Empty || request.CorrelationId == Guid.Empty)
            throw new ArgumentException("Platform Admin and correlation identifiers are required.",
                nameof(request));
        if (string.IsNullOrWhiteSpace(request.OperatorReference)
            || request.OperatorReference.Length > 160)
            throw new ArgumentException("A bounded operator reference is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 160)
            throw new ArgumentException("A bounded idempotency key is required.", nameof(request));
        if (request.Settings.EffectiveFromUtc is not { Kind: DateTimeKind.Utc } effective
            || effective > now)
            throw new ArgumentException(
                "The first financial configuration must be explicitly effective in UTC now.",
                nameof(request));
    }
}
