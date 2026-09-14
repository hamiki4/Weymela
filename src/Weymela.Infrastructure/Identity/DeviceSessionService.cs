using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public enum DeviceSessionAccessState
{
    Active,
    Locked,
    RecoveryRequired,
    FullAuthenticationRequired
}

public sealed record DeviceSessionIdentity(Guid UserId, Guid IdentityBindingId, long IdentityVersion);

public sealed record DeviceSessionSnapshot(
    Guid Id,
    Guid UserId,
    Guid AuthorizedDeviceId,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime? LockedAtUtc,
    DateTime ExpiresAtUtc,
    long Generation,
    long Version);

public sealed record DeviceSessionResolution(DeviceSessionAccessState State, DeviceSessionSnapshot? Session);
public sealed record CreatedDeviceSession(DeviceSessionSnapshot Session, OpaqueDeviceCredential Credential);
public sealed record RotatedDeviceSession(DeviceSessionSnapshot Session, OpaqueDeviceCredential Credential);

/// <summary>
/// Durable device-session operations only. This service does not authenticate HTTP
/// requests, issue cookies, select profiles, verify PINs, or alter authorization.
/// </summary>
public sealed class DeviceSessionService(WeymelaDbContext db, TimeProvider clock)
{
    public async Task<CreatedDeviceSession> CreateAsync(
        DeviceSessionIdentity identity,
        Guid authorizedDeviceId,
        CancellationToken ct = default)
    {
        ValidateIdentity(identity);
        if (authorizedDeviceId == Guid.Empty) throw Forbidden();

        var now = UtcNow();
        var bindingIsCurrent = await db.IdentityBindings.AsNoTracking().AnyAsync(x =>
            x.Id == identity.IdentityBindingId && x.UserId == identity.UserId
            && x.Version == identity.IdentityVersion && x.IsActive, ct);
        var device = await db.AuthorizedDevices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == authorizedDeviceId && x.UserId == identity.UserId, ct);
        if (!bindingIsCurrent || !UsableDevice(device, now)) throw Forbidden();

        var credential = OpaqueDeviceCredential.Create();
        var session = DeviceAccessPolicy.NewSession(identity.UserId, identity.IdentityBindingId,
            identity.IdentityVersion, authorizedDeviceId, credential.Digest, now);
        db.DeviceSessions.Add(session);
        await SaveAsync(ct);
        return new(ToSnapshot(session), credential);
    }

    public async Task<DeviceSessionResolution> ResolveAsync(
        DeviceSessionIdentity identity,
        string? rawCredential,
        CancellationToken ct = default)
    {
        if (!ValidIdentity(identity) || !OpaqueDeviceCredential.TryDigest(rawCredential, out var digest))
            return FullAuthentication();

        var session = await db.DeviceSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SessionIdentifierHash == digest, ct);
        if (session is null) return FullAuthentication();
        var device = await db.AuthorizedDevices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == session.AuthorizedDeviceId, ct);
        var bindingIsCurrent = await db.IdentityBindings.AsNoTracking().AnyAsync(x =>
            x.Id == session.IdentityBindingId && x.UserId == identity.UserId
            && x.Version == identity.IdentityVersion && x.IsActive, ct);
        return Evaluate(identity, session, device, bindingIsCurrent, UtcNow());
    }

    public async Task<DeviceSessionSnapshot> UpdateMeaningfulActivityAsync(
        DeviceSessionIdentity identity,
        Guid sessionId,
        string rawCredential,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var loaded = await LoadForMutationAsync(identity, sessionId, rawCredential, expectedVersion, ct);
        RequireState(loaded.State, DeviceSessionAccessState.Active);
        loaded.Session.LastActivityAtUtc = UtcNow();
        loaded.Session.Version++;
        await SaveAsync(ct);
        return ToSnapshot(loaded.Session);
    }

    public async Task<DeviceSessionSnapshot> SetLockedAsync(
        DeviceSessionIdentity identity,
        Guid sessionId,
        string rawCredential,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var loaded = await LoadForMutationAsync(identity, sessionId, rawCredential, expectedVersion, ct);
        RequireState(loaded.State, DeviceSessionAccessState.Locked);
        if (loaded.Session.LockedAtUtc is null)
        {
            loaded.Session.LockedAtUtc = UtcNow();
            loaded.Session.Version++;
            await SaveAsync(ct);
        }
        return ToSnapshot(loaded.Session);
    }

    public async Task<DeviceSessionSnapshot> ClearLockedStateAsync(
        DeviceSessionIdentity identity,
        Guid sessionId,
        string rawCredential,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var loaded = await LoadForMutationAsync(identity, sessionId, rawCredential, expectedVersion, ct);
        RequireState(loaded.State, DeviceSessionAccessState.Locked);
        loaded.Session.LockedAtUtc = null;
        loaded.Session.LastActivityAtUtc = UtcNow();
        loaded.Session.Version++;
        await SaveAsync(ct);
        return ToSnapshot(loaded.Session);
    }

    public async Task<RotatedDeviceSession> RotateCredentialAsync(
        DeviceSessionIdentity identity,
        Guid sessionId,
        string rawCredential,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var loaded = await LoadForMutationAsync(identity, sessionId, rawCredential, expectedVersion, ct);
        if (loaded.State is not (DeviceSessionAccessState.Active or DeviceSessionAccessState.Locked))
            throw Forbidden();
        var credential = OpaqueDeviceCredential.Create();
        loaded.Session.SessionIdentifierHash = credential.Digest;
        loaded.Session.Generation++;
        loaded.Session.Version++;
        await SaveAsync(ct);
        return new(ToSnapshot(loaded.Session), credential);
    }

    public async Task<DeviceSessionSnapshot> RevokeAsync(
        DeviceSessionIdentity identity,
        Guid sessionId,
        string rawCredential,
        long expectedVersion,
        CancellationToken ct = default)
    {
        var loaded = await LoadForMutationAsync(identity, sessionId, rawCredential, expectedVersion, ct);
        if (loaded.Session.RevokedAtUtc is null)
        {
            loaded.Session.RevokedAtUtc = UtcNow();
            loaded.Session.Version++;
            await SaveAsync(ct);
        }
        return ToSnapshot(loaded.Session);
    }

    private async Task<LoadedSession> LoadForMutationAsync(
        DeviceSessionIdentity identity,
        Guid sessionId,
        string rawCredential,
        long expectedVersion,
        CancellationToken ct)
    {
        ValidateIdentity(identity);
        if (sessionId == Guid.Empty || expectedVersion < 0
            || !OpaqueDeviceCredential.TryDigest(rawCredential, out _)) throw Forbidden();

        var session = await db.DeviceSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null) throw Forbidden();
        if (!OpaqueDeviceCredential.Matches(rawCredential, session.SessionIdentifierHash)) throw Forbidden();
        if (session.Version != expectedVersion) throw Concurrency();
        var device = await db.AuthorizedDevices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == session.AuthorizedDeviceId, ct);
        var bindingIsCurrent = await db.IdentityBindings.AsNoTracking().AnyAsync(x =>
            x.Id == session.IdentityBindingId && x.UserId == identity.UserId
            && x.Version == identity.IdentityVersion && x.IsActive, ct);
        if (session.UserId != identity.UserId || session.IdentityBindingId != identity.IdentityBindingId
            || session.IdentityVersion != identity.IdentityVersion || !bindingIsCurrent
            || device is null || device.UserId != identity.UserId || device.Id != session.AuthorizedDeviceId)
            throw Forbidden();
        var resolution = Evaluate(identity, session, device, bindingIsCurrent, UtcNow());
        return new(session, resolution.State);
    }

    private static DeviceSessionResolution Evaluate(
        DeviceSessionIdentity identity,
        DeviceSessionRecord session,
        AuthorizedDeviceRecord? device,
        bool bindingIsCurrent,
        DateTime now)
    {
        var snapshot = ToSnapshot(session);
        if (session.UserId != identity.UserId || session.IdentityBindingId != identity.IdentityBindingId
            || session.IdentityVersion != identity.IdentityVersion || !bindingIsCurrent
            || session.RevokedAtUtc is not null || DeviceAccessPolicy.IsSessionExpired(session.ExpiresAtUtc, now)
            || device is null || device.UserId != identity.UserId || device.Id != session.AuthorizedDeviceId
            || device.RevokedAtUtc is not null || device.ExpiresAtUtc is null
            || DeviceAccessPolicy.IsDeviceExpired(device.ExpiresAtUtc.Value, now))
            return FullAuthentication();
        if (device.RequiresRecovery) return new(DeviceSessionAccessState.RecoveryRequired, snapshot);
        if (session.LockedAtUtc is not null || DeviceAccessPolicy.IsIdleLocked(session.LastActivityAtUtc, now))
            return new(DeviceSessionAccessState.Locked, snapshot);
        return new(DeviceSessionAccessState.Active, snapshot);
    }

    private static bool UsableDevice(AuthorizedDeviceRecord? device, DateTime now) =>
        device is not null && device.RevokedAtUtc is null && !device.RequiresRecovery
        && !string.IsNullOrWhiteSpace(device.PinVerifier) && device.ExpiresAtUtc is not null
        && !DeviceAccessPolicy.IsDeviceExpired(device.ExpiresAtUtc.Value, now);

    private static DeviceSessionSnapshot ToSnapshot(DeviceSessionRecord session) => new(
        session.Id, session.UserId, session.AuthorizedDeviceId, session.CreatedAtUtc,
        session.LastActivityAtUtc, session.LockedAtUtc, session.ExpiresAtUtc,
        session.Generation, session.Version);

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            throw new ApplicationFailure(FailureKind.ConcurrencyConflict,
                "The device session changed. Reload its current state before retrying.", exception);
        }
    }

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;
    private static DeviceSessionResolution FullAuthentication() =>
        new(DeviceSessionAccessState.FullAuthenticationRequired, null);
    private static bool ValidIdentity(DeviceSessionIdentity? identity) =>
        identity is not null && identity.UserId != Guid.Empty && identity.IdentityBindingId != Guid.Empty
        && identity.IdentityVersion >= 0;
    private static void ValidateIdentity(DeviceSessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!ValidIdentity(identity)) throw Forbidden();
    }
    private static void RequireState(DeviceSessionAccessState actual, DeviceSessionAccessState required)
    {
        if (actual != required) throw Forbidden();
    }
    private static ApplicationFailure Forbidden() =>
        new(FailureKind.Forbidden, "The device session is not available. Complete account authentication again.");
    private static ApplicationFailure Concurrency() =>
        new(FailureKind.ConcurrencyConflict, "The device session changed. Reload its current state before retrying.");

    private sealed record LoadedSession(DeviceSessionRecord Session, DeviceSessionAccessState State);
}
