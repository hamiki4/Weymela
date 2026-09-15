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
    string IdempotencyKey);

public sealed record PlatformAdminBootstrapResult(Guid UserId, Guid BindingId, bool Replayed);

public static class PlatformAdminBootstrapTarget
{
    public const string DatabaseName = "weymela_v3_pilot";
    public const string BootstrapRole = "weymela_v3_bootstrap";

    public static async Task VerifyAsync(WeymelaDbContext db, CancellationToken cancellationToken)
        => await VerifyAsync(db, DatabaseName, BootstrapRole, cancellationToken);

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
            throw new InvalidOperationException("Bootstrap target guard failed: expected the isolated V3 Pilot database and bootstrap role.");
    }
}

public sealed class PlatformAdminBootstrapper(WeymelaDbContext db)
{
    private const string Operation = "PlatformAdminBootstrap";

    public async Task<PlatformAdminBootstrapResult> ProvisionAsync(PlatformAdminBootstrapRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
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
            $"operator={request.OperatorReference};targetUserId={request.UserId:D};bindingId={bindingId:D}"));
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
            request.CorrelationId.ToString("D"), request.IdempotencyKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void Validate(PlatformAdminBootstrapRequest request)
    {
        if (!string.Equals(request.FirebaseProjectId, "weymela-pilot", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the owner-approved Firebase project may be provisioned.");
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
