using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Identity;

public static class ProductHandoffPurposes
{
    public const string ProfileOnboarding = "PROFILE_ONBOARDING";
    public const string ExistingWorkspace = "EXISTING_WORKSPACE";
}

public sealed record ProductHandoffIssueRequest(Guid UserId, Guid BindingId, long BindingVersion,
    DateTime AuthenticatedAtUtc, ActorRole Role, string Purpose, string CallbackId, Guid? ProfileSubjectId,
    Guid? BusinessId, Guid CorrelationId);
public sealed record ProductHandoffIssueResult(string Code, string State, string CallbackUrl, DateTime ExpiresAtUtc);
public sealed record ProductIdentityAssertion(string Issuer, string Environment, string Audience, Guid UserId,
    Guid IdentityBindingId, long IdentityBindingVersion, DateTime AuthenticatedAtUtc, string Purpose,
    string Role, Guid? ProfileSubjectId, Guid? BusinessId, string DisplayName, string AuthorityStatus, string DeviceAssurance,
    string AccountEmail, string? AccountPhone, DateTime IssuedAtUtc, DateTime ExpiresAtUtc);
public sealed record ProductAuthorityRequest(Guid UserId, Guid IdentityBindingId, long IdentityBindingVersion,
    string Role, Guid? ProfileSubjectId, Guid? BusinessId, string Purpose);
public sealed record ProductAuthorityResult(bool Active, string Status, long IdentityBindingVersion);
public sealed record ProductProfileSynchronizationRequest(Guid UserId, Guid IdentityBindingId,
    long IdentityBindingVersion, string Role, Guid ExternalSubjectId, Guid? BusinessId, string DisplayName,
    string Lifecycle, string IdempotencyKey, IReadOnlyList<ProductCreatorSocialProfileSynchronization>? SocialProfiles = null);
public sealed record ProductCreatorSocialProfileSynchronization(string Platform, string ProfileUrl, long AudienceCount,
    string VerificationStatus = "Unverified", long? VerifiedAudience = null);
public sealed record ProductProfileSynchronizationResult(Guid V3ProfileSubjectId, string Lifecycle, bool Created);

public sealed class ProductIntegrationService(WeymelaDbContext db, ProductIntegrationOptions options, TimeProvider clock)
{
    private static readonly ActorRole[] PublicRoles = [ActorRole.Customer, ActorRole.Creator, ActorRole.Business];

    public bool Enabled => options.Enabled;
    public object PublicConfiguration() => new
    {
        enabled = options.Enabled,
        beginUrl = options.Enabled ? options.BeginUrl : "",
        callbackId = options.Enabled ? options.CallbackId : ""
    };

    public bool AuthenticateClient(string? clientId, string? clientSecret)
    {
        if (!options.Enabled || clientId is null || clientSecret is null) return false;
        return Fixed(clientId, options.ClientId) & Fixed(clientSecret, options.ClientSecret);
    }

    public async Task<ProductHandoffIssueResult> IssueAsync(ProductHandoffIssueRequest request, string state,
        CancellationToken ct)
    {
        EnsureEnabled(true);
        var now = clock.GetUtcNow().UtcDateTime;
        if (state.Length is < 32 or > 160 || !state.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw Denied();
        if (request.CallbackId != options.CallbackId || request.UserId == Guid.Empty
            || request.BindingId == Guid.Empty || request.BindingVersion < 0)
            throw Denied();
        var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.BindingId
            && x.UserId == request.UserId && x.Version == request.BindingVersion && x.IsActive
            && x.ValidAfterUtc <= request.AuthenticatedAtUtc, ct);
        if (binding is null)
        {
            Audit("ProductHandoffAuthorityRejected", request.UserId, request.CorrelationId,
                $"role={request.Role};purpose={request.Purpose}", now);
            await db.SaveChangesAsync(ct);
            throw Denied();
        }
        _ = binding;
        if (!Enum.IsDefined(request.Role) || request.Role == ActorRole.Cashier) throw Denied();
        var purpose = NormalizePurpose(request.Purpose);
        Guid? subject = request.ProfileSubjectId;
        Guid? business = request.BusinessId;
        if (purpose == ProductHandoffPurposes.ExistingWorkspace)
        {
            var permission = await db.CommercePermissions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.UserId == request.UserId && x.Role == request.Role && x.SubjectId == subject && x.IsActive, ct);
            if (permission is null || request.Role == ActorRole.Business && permission.BusinessId != subject
                || (request.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin) && subject != request.UserId)
            {
                Audit("ProductHandoffProfileRejected", request.UserId, request.CorrelationId,
                    $"role={request.Role};purpose={purpose}", now);
                await db.SaveChangesAsync(ct);
                throw Denied();
            }
            business = permission.BusinessId;
        }
        else
        {
            if (!PublicRoles.Contains(request.Role) || subject is not null || business is not null)
            {
                Audit("ProductHandoffProfileRejected", request.UserId, request.CorrelationId,
                    $"role={request.Role};purpose={purpose}", now);
                await db.SaveChangesAsync(ct);
                throw Denied();
            }
            if (request.Role != ActorRole.Business
                && await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == request.UserId
                    && x.Role == request.Role && x.IsActive, ct))
            {
                Audit("ProductHandoffProfileRejected", request.UserId, request.CorrelationId,
                    $"role={request.Role};purpose={purpose}", now);
                await db.SaveChangesAsync(ct);
                throw Denied();
            }
            var legal = await new AccountLegalOnboardingService(db, clock).StatusAsync(request.UserId, ct);
            if (!legal.Available || !legal.Current)
                throw new ApplicationFailure(FailureKind.Forbidden, "Review and accept the current legal documents before continuing.");
        }

        var raw = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var row = new ProductHandoffTransaction
        {
            CodeHash = Hash(raw), UserId = request.UserId, IdentityBindingId = request.BindingId,
            IdentityBindingVersion = request.BindingVersion, Role = request.Role,
            ProfileSubjectId = subject, BusinessId = business, Purpose = purpose,
            Audience = options.Audience, Environment = options.Environment, CallbackId = request.CallbackId,
            AuthenticatedAtUtc = request.AuthenticatedAtUtc, IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(options.CodeLifetime), CorrelationId = request.CorrelationId
        };
        db.ProductHandoffTransactions.Add(row);
        Audit("ProductHandoffIssued", request.UserId, request.CorrelationId,
            $"role={request.Role};purpose={purpose};transaction={row.Id:D}", now);
        await db.SaveChangesAsync(ct);
        return new(raw, state, options.CallbackUrl, row.ExpiresAtUtc);
    }

    public async Task<ProductIdentityAssertion> RedeemAsync(string code, string callbackId, CancellationToken ct)
    {
        EnsureEnabled(true);
        if (code.Length is < 40 or > 100) throw InvalidCode();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var hash = Hash(code);
            var row = await db.ProductHandoffTransactions
                .FromSqlInterpolated($"SELECT *, xmin FROM v3.\"ProductHandoffTransactions\" WHERE \"CodeHash\" = {hash} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw InvalidCode();
            var now = clock.GetUtcNow().UtcDateTime;
            if (row.ConsumedAtUtc is not null)
            {
                Audit("ProductHandoffReplayRejected", row.UserId, row.CorrelationId,
                    $"transaction={row.Id:D}", now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                throw InvalidCode();
            }
            if (row.ExpiresAtUtc <= now || row.Audience != options.Audience
                || row.Environment != options.Environment || row.CallbackId != callbackId
                || callbackId != options.CallbackId)
            {
                var rejection = row.ExpiresAtUtc <= now ? "ProductHandoffExpired"
                    : row.Audience != options.Audience ? "ProductHandoffAudienceRejected"
                    : row.Environment != options.Environment ? "ProductHandoffEnvironmentRejected"
                    : "ProductHandoffCallbackRejected";
                Audit(rejection,
                    row.UserId, row.CorrelationId, $"transaction={row.Id:D}", now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                throw InvalidCode();
            }
            var authority = await AuthorityAsync(new(row.UserId, row.IdentityBindingId,
                row.IdentityBindingVersion, row.Role.ToString(), row.ProfileSubjectId, row.BusinessId,
                row.Purpose), ct);
            if (!authority.Active)
            {
                Audit("ProductHandoffAuthorityRejected", row.UserId, row.CorrelationId,
                    $"role={row.Role};purpose={row.Purpose};transaction={row.Id:D}", now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                throw Denied();
            }
            var displayName = row.ProfileSubjectId is Guid profileId
                ? await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.SubjectId == profileId
                    && x.Role == row.Role).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) ?? "Weymela profile"
                : row.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin
                    ? await db.AdminGrants.AsNoTracking().Where(x => x.UserId == row.UserId && x.IsActive)
                        .OrderByDescending(x => x.GrantedAtUtc).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? "Weymela Admin"
                    : "Account setup";
            var accountEmail = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == row.UserId
                    && x.Kind == "Email" && x.IsVerified && x.DeliveryAddress != null)
                .Select(x => x.DeliveryAddress!).SingleOrDefaultAsync(ct) ?? throw Denied();
            if (row.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin && displayName == "Weymela Admin")
                displayName = await db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == row.UserId)
                    .Select(x => x.PreferredName).SingleOrDefaultAsync(ct)
                    ?? accountEmail.Split('@')[0].Replace('.', ' ').Replace('_', ' ');
            var accountPhone = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == row.UserId
                    && x.Kind == "Phone" && x.DeliveryAddress != null)
                .Select(x => x.DeliveryAddress).SingleOrDefaultAsync(ct);
            row.ConsumedAtUtc = now;
            Audit("ProductHandoffRedeemed", row.UserId, row.CorrelationId,
                $"role={row.Role};purpose={row.Purpose};transaction={row.Id:D}", now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(options.Issuer, options.Environment, options.Audience, row.UserId,
                row.IdentityBindingId, row.IdentityBindingVersion, row.AuthenticatedAtUtc, row.Purpose,
                row.Role.ToString(), row.ProfileSubjectId, row.BusinessId, displayName, authority.Status, "V3_DEVICE_UNLOCKED",
                accountEmail, accountPhone, now, now.AddMinutes(5));
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var replayed = await db.ProductHandoffTransactions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CodeHash == Hash(code), ct);
            if (replayed?.ConsumedAtUtc is not null)
            {
                Audit("ProductHandoffReplayRejected", replayed.UserId, replayed.CorrelationId,
                    $"transaction={replayed.Id:D}", clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
            }
            throw InvalidCode();
        }
    }

    public async Task<ProductAuthorityResult> AuthorityAsync(ProductAuthorityRequest request, CancellationToken ct)
    {
        EnsureEnabled(true);
        if (!Enum.TryParse<ActorRole>(request.Role, true, out var role)) return new(false, "Invalid", request.IdentityBindingVersion);
        var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.IdentityBindingId
            && x.UserId == request.UserId && x.Version == request.IdentityBindingVersion && x.IsActive, ct);
        if (binding is null) return new(false, "Revoked", request.IdentityBindingVersion);
        var purpose = NormalizePurpose(request.Purpose);
        if (purpose == ProductHandoffPurposes.ExistingWorkspace)
        {
            var active = await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == request.UserId
                && x.Role == role && x.SubjectId == request.ProfileSubjectId && x.BusinessId == request.BusinessId
                && x.IsActive, ct);
            return new(active, active ? "Active" : "Revoked", binding.Version);
        }
        if (!PublicRoles.Contains(role)) return new(false, "Invalid", binding.Version);
        var enrollment = await db.RoleEnrollments.AsNoTracking().Where(x => x.UserId == request.UserId
            && x.RequestedRole == role).OrderByDescending(x => x.SubmittedAtUtc).FirstOrDefaultAsync(ct);
        if (enrollment?.Status == RoleEnrollmentStatus.Rejected) return new(false, "Rejected", binding.Version);
        var alreadyActive = await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == request.UserId
            && x.Role == role && x.IsActive, ct);
        return new(role == ActorRole.Business || !alreadyActive,
            enrollment is null ? "Onboarding" : "Pending", binding.Version);
    }

    public async Task<ProductProfileSynchronizationResult> SynchronizeProfileAsync(
        ProductProfileSynchronizationRequest request, CancellationToken ct)
    {
        EnsureEnabled(true);
        if (!Enum.TryParse<ActorRole>(request.Role, true, out var role) || !PublicRoles.Contains(role)
            || request.ExternalSubjectId == Guid.Empty || request.DisplayName.Trim().Length is < 1 or > 120
            || request.IdempotencyKey.Length is < 8 or > 200)
            throw new ApplicationFailure(FailureKind.Validation, "The product profile update is invalid.");
        var binding = await db.IdentityBindings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.IdentityBindingId
            && x.UserId == request.UserId && x.Version == request.IdentityBindingVersion && x.IsActive, ct)
            ?? throw Denied();
        _ = binding;
        var lifecycle = request.Lifecycle.Trim().ToUpperInvariant();
        if (lifecycle is not ("PENDING" or "ACTIVE" or "REJECTED" or "CORRECTION_REQUESTED"))
            throw new ApplicationFailure(FailureKind.Validation, "The product profile lifecycle is invalid.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await db.CommercePermissions.SingleOrDefaultAsync(x => x.UserId == request.UserId
            && x.Role == role && x.SubjectId == request.ExternalSubjectId && x.IsActive, ct);
        if (current is not null)
        {
            if (role == ActorRole.Creator && request.SocialProfiles is not null)
                await SynchronizeCreatorSocialProfilesAsync(request.ExternalSubjectId, request.SocialProfiles, clock.GetUtcNow().UtcDateTime, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(current.SubjectId, "ACTIVE", false);
        }
        if (role != ActorRole.Business)
        {
            var other = await db.CommercePermissions.SingleOrDefaultAsync(x => x.UserId == request.UserId
                && x.Role == role && x.IsActive, ct);
            if (other is not null)
            {
                if (role == ActorRole.Creator && request.SocialProfiles is not null)
                    await SynchronizeCreatorSocialProfilesAsync(other.SubjectId, request.SocialProfiles, clock.GetUtcNow().UtcDateTime, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new(other.SubjectId, "ACTIVE", false);
            }
        }
        var idempotency = $"product:{request.IdempotencyKey}";
        var enrollment = await db.RoleEnrollments.SingleOrDefaultAsync(x => x.UserId == request.UserId
            && x.IdempotencyKey == idempotency, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var created = false;
        if (enrollment is null)
        {
            var generatedPublicId = $"{(role == ActorRole.Customer ? "CU" : role == ActorRole.Creator ? "CR" : "BU")}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
            enrollment = new RoleEnrollmentRecord
            {
                UserId = request.UserId, RequestedRole = role, IdempotencyKey = idempotency,
                SubmissionJson = JsonSerializer.Serialize(new
                {
                    DisplayName = request.DisplayName.Trim(), PublicId = generatedPublicId,
                    Region = "", Category = "", Submission = "V2 product integration"
                }), SubmittedAtUtc = now
            };
            db.RoleEnrollments.Add(enrollment);
            created = true;
        }
        else if (enrollment.RequestedRole != role) throw new ApplicationFailure(FailureKind.IdempotencyConflict,
            "The product profile reference was already used.");

        var details = JsonSerializer.Deserialize<EnrollmentDetails>(enrollment.SubmissionJson)
            ?? throw new ApplicationFailure(FailureKind.Validation, "The product profile is incomplete.");
        if (lifecycle == "ACTIVE")
        {
            enrollment.Status = RoleEnrollmentStatus.Approved;
            enrollment.ReviewedAtUtc ??= now;
            enrollment.DecisionReason = "Approved by the integrated product lifecycle.";
            if (!await db.CommercePermissions.AnyAsync(x => x.UserId == request.UserId && x.Role == role
                && x.SubjectId == request.ExternalSubjectId, ct))
            {
                var businessId = role == ActorRole.Business ? request.ExternalSubjectId : request.BusinessId;
                db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
                {
                    SubjectId = request.ExternalSubjectId, Role = role, DisplayName = request.DisplayName.Trim(),
                    PublicId = details.PublicId, Region = details.Region ?? "", Category = details.Category ?? ""
                });
                if (role == ActorRole.Customer)
                {
                    var legal = await new AccountLegalOnboardingService(db, clock).StatusAsync(request.UserId, ct);
                    if (!legal.Current) throw new ApplicationFailure(FailureKind.Forbidden,
                        "Current legal acceptance is required.");
                    db.CustomerProfiles.Add(new CustomerProfileRecord
                    {
                        CustomerId = request.ExternalSubjectId, UserId = request.UserId,
                        PreferredName = request.DisplayName.Trim(), CreatedAtUtc = now, UpdatedAtUtc = now
                    });
                    db.CustomerCashbackAccounts.Add(new CustomerCashbackAccount(request.ExternalSubjectId));
                }
                if (role == ActorRole.Business) db.BusinessWallets.Add(new BusinessWallet(request.ExternalSubjectId));
                db.CommercePermissions.Add(new CommercePermission(request.UserId, role,
                    request.ExternalSubjectId, businessId, true, false));
            }
        }
        else if (lifecycle == "REJECTED")
        {
            enrollment.Status = RoleEnrollmentStatus.Rejected;
            enrollment.ReviewedAtUtc = now;
            enrollment.DecisionReason = "Rejected by the integrated product lifecycle.";
        }
        else if (lifecycle == "CORRECTION_REQUESTED")
        {
            enrollment.Status = RoleEnrollmentStatus.Pending;
            enrollment.ReviewedAtUtc = now;
            enrollment.DecisionReason = "Corrections requested by the integrated product lifecycle.";
        }
        if (role == ActorRole.Creator && request.SocialProfiles is not null)
            await SynchronizeCreatorSocialProfilesAsync(request.ExternalSubjectId, request.SocialProfiles, now, ct);
        if (lifecycle is "ACTIVE" or "REJECTED" or "CORRECTION_REQUESTED")
            db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = role == ActorRole.Creator
                    ? lifecycle == "ACTIVE" ? "CreatorProfileApproved" : lifecycle == "CORRECTION_REQUESTED" ? "CreatorProfileCorrectionRequested" : "CreatorProfileRejected"
                    : role == ActorRole.Business
                        ? lifecycle == "ACTIVE" ? "BusinessProfileApproved" : lifecycle == "CORRECTION_REQUESTED" ? "BusinessProfileCorrectionRequested" : "BusinessProfileRejected"
                        : "CustomerProfileActivated",
                Payload = JsonSerializer.Serialize(new { request.UserId, ProfileSubjectId = request.ExternalSubjectId, Role = role.ToString(), Lifecycle = lifecycle }),
                OccurredAtUtc = now
            });
        Audit("ProductProfileSynchronized", request.UserId, Guid.NewGuid(),
            $"role={role};lifecycle={lifecycle};externalSubject={request.ExternalSubjectId:D}", now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(request.ExternalSubjectId, lifecycle, created);
    }

    private async Task SynchronizeCreatorSocialProfilesAsync(Guid creatorId,
        IReadOnlyList<ProductCreatorSocialProfileSynchronization> input, DateTime now, CancellationToken ct)
    {
        if (input.Count == 0 || input.Count > 4)
            throw new ApplicationFailure(FailureKind.Validation, "At least one supported Creator social profile is required.");
        var normalized = new List<(CreatorPlatform Platform, string Url, long Audience, string Status, long? Verified)>();
        foreach (var row in input)
        {
            if (!Enum.TryParse<CreatorPlatform>(row.Platform, true, out var platform) || !Enum.IsDefined(platform)
                || row.AudienceCount < 0 || row.VerifiedAudience < 0)
                throw new ApplicationFailure(FailureKind.Validation, "Creator social profile data is invalid.");
            normalized.Add((platform, SocialUrl(platform, row.ProfileUrl), row.AudienceCount,
                string.IsNullOrWhiteSpace(row.VerificationStatus) ? "Unverified" : row.VerificationStatus.Trim(), row.VerifiedAudience));
        }
        if (normalized.GroupBy(x => x.Platform).Any(x => x.Count() > 1))
            throw new ApplicationFailure(FailureKind.Validation, "Each Creator social platform may be registered once.");
        var existing = await db.CreatorSocialProfiles.Where(x => x.CreatorId == creatorId).ToListAsync(ct);
        foreach (var row in existing) row.IsActive = false;
        foreach (var social in normalized)
        {
            var row = existing.SingleOrDefault(x => x.Platform == social.Platform);
            if (row is null)
            {
                db.CreatorSocialProfiles.Add(new CreatorSocialProfileRecord
                {
                    CreatorId = creatorId, Platform = social.Platform, ProfileUrl = social.Url,
                    SelfReportedAudience = social.Audience, VerificationStatus = social.Status,
                    VerifiedAudience = social.Verified, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
                });
            }
            else
            {
                row.ProfileUrl = social.Url; row.SelfReportedAudience = social.Audience;
                row.VerificationStatus = social.Status; row.VerifiedAudience = social.Verified;
                row.IsActive = true; row.UpdatedAtUtc = now;
            }
        }
    }

    private static string SocialUrl(CreatorPlatform platform, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ApplicationFailure(FailureKind.Validation, "Creator social profile URLs must use secure HTTPS links.");
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        var allowed = platform switch
        {
            CreatorPlatform.TikTok => host is "tiktok.com" or "m.tiktok.com",
            CreatorPlatform.Instagram => host == "instagram.com",
            CreatorPlatform.YouTube => host is "youtube.com" or "m.youtube.com" or "youtu.be",
            CreatorPlatform.Facebook => host is "facebook.com" or "m.facebook.com" or "fb.com",
            _ => false
        };
        if (!allowed) throw new ApplicationFailure(FailureKind.Validation, "The social profile URL does not match the selected platform.");
        return uri.AbsoluteUri;
    }

    private void Audit(string type, Guid actor, Guid correlation, string detail, DateTime now) =>
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), type, actor, null, null, null, correlation, now, detail));
    private static string NormalizePurpose(string purpose) => purpose.Trim().ToUpperInvariant() switch
    {
        ProductHandoffPurposes.ProfileOnboarding => ProductHandoffPurposes.ProfileOnboarding,
        ProductHandoffPurposes.ExistingWorkspace => ProductHandoffPurposes.ExistingWorkspace,
        _ => throw Denied()
    };
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.SerializationFailure) return true;
        return false;
    }
    private static bool Fixed(string supplied, string expected)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
    private T EnsureEnabled<T>(T value)
    {
        if (!options.Enabled) throw new ApplicationFailure(FailureKind.Forbidden,
            "The Weymela product workspace is not configured.");
        return value;
    }
    private static ApplicationFailure InvalidCode() => new(FailureKind.Forbidden,
        "The product handoff is invalid or expired.");
    private static ApplicationFailure Denied() => new(FailureKind.Forbidden,
        "The product handoff is not authorized.");
    private sealed record EnrollmentDetails(string DisplayName, string PublicId, string? Region,
        string? Category, string? Submission);
}
