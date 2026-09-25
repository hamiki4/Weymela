using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Identity;

/// <summary>
/// Stage A account authority. This service deliberately does not issue tokens,
/// write passwords/PINs, or create Cashiers. Administrative provisioning only
/// stores a one-time activation hash and delivers the invitation to the target.
/// </summary>
public sealed class PlatformAdminAccountService(WeymelaDbContext db, TimeProvider clock, IEmailCodeDelivery delivery)
{
    public const int MaxDirectoryResults = 500;
    private static readonly ActorRole[] PreauthorizedRoles =
        [ActorRole.Customer, ActorRole.Creator, ActorRole.Business, ActorRole.OperationsAdmin, ActorRole.PlatformAdmin];
    private static readonly ActorRole[] AllRoles = Enum.GetValues<ActorRole>();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<AdminAccountSummary>> ListAsync(Actor actor, AdminAccountFilterInput filter, CancellationToken ct)
    {
        DemandPlatform(actor);
        var adminRoles = string.Equals(filter.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        ActorRole? requestedRole = null;
        if (!adminRoles && !string.IsNullOrWhiteSpace(filter.Role))
        {
            if (!TryRole(filter.Role, out var parsedRole, allowEmpty: false))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a supported account role.");
            requestedRole = parsedRole;
        }
        if (filter.Role is not null && requestedRole is null && !adminRoles)
            throw new ApplicationFailure(FailureKind.Validation, "Choose a supported account role.");
        if (!string.IsNullOrWhiteSpace(filter.Status) && !Enum.TryParse<AccountLifecycleStatus>(filter.Status, true, out _)
            && !string.Equals(filter.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            throw new ApplicationFailure(FailureKind.Validation, "Choose a supported account status.");

        var permissions = await db.CommercePermissions.AsNoTracking().ToListAsync(ct);
        var preauthorizations = await db.AccountPreauthorizations.AsNoTracking()
            .Where(x => x.Status == AccountPreauthorizationStatus.Pending)
            .ToListAsync(ct);
        var lifecycles = await db.AccountLifecycles.AsNoTracking().ToDictionaryAsync(x => x.UserId, ct);
        var userIds = permissions.Select(x => x.UserId).Concat(preauthorizations.Select(x => x.UserId)).Distinct().ToArray();
        var identifiers = await db.AuthIdentifiers.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);
        var profiles = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => userIds.Contains(x.SubjectId)).ToListAsync(ct);
        var customers = await db.CustomerProfiles.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);
        var grants = await db.AdminGrants.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);
        var enrollments = await db.RoleEnrollments.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);
        var activity = await db.AuditEvents.AsNoTracking().Where(x => userIds.Contains(x.ActorId) || (x.TargetUserId != null && userIds.Contains(x.TargetUserId.Value)))
            .GroupBy(x => x.TargetUserId ?? x.ActorId).Select(x => new { UserId = x.Key, Last = x.Max(y => y.OccurredAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last, ct);

        var result = new List<AdminAccountSummary>();
        foreach (var group in permissions.GroupBy(x => new { x.UserId, x.Role }))
        {
            if (requestedRole is not null && group.Key.Role != requestedRole) continue;
            if (adminRoles && group.Key.Role is not (ActorRole.PlatformAdmin or ActorRole.OperationsAdmin)) continue;
            var groupProfile = profiles.FirstOrDefault(p => p.Role == group.Key.Role && group.Select(x => x.SubjectId).Contains(p.SubjectId));
            var groupGrant = grants.Where(x => x.UserId == group.Key.UserId && x.Role == group.Key.Role).OrderByDescending(x => x.GrantedAtUtc).FirstOrDefault();
            var groupCustomer = customers.FirstOrDefault(x => x.UserId == group.Key.UserId);
            var groupEmail = identifiers.FirstOrDefault(x => x.UserId == group.Key.UserId && x.Kind == "Email")?.DeliveryAddress;
            if (!Matches(filter.Search, groupProfile?.DisplayName, groupGrant?.DisplayName, groupCustomer?.PreferredName, groupEmail,
                group.FirstOrDefault()?.BusinessId?.ToString("D"))) continue;
            var lifecycle = lifecycles.GetValueOrDefault(group.Key.UserId);
            var preauth = preauthorizations.LastOrDefault(x => x.UserId == group.Key.UserId && x.TargetRole == group.Key.Role);
            var enrollment = enrollments.Where(x => x.UserId == group.Key.UserId && x.RequestedRole == group.Key.Role)
                .OrderByDescending(x => x.SubmittedAtUtc).FirstOrDefault();
            var allUserPermissions = permissions.Where(x => x.UserId == group.Key.UserId).ToList();
            result.Add(Summary(group.Key.UserId, group.Key.Role, lifecycle, group.ToList(), allUserPermissions, identifiers, profiles, customers, grants,
                activity.GetValueOrDefault(group.Key.UserId), preauth, enrollment));
        }
        foreach (var preauth in preauthorizations.Where(x => (requestedRole is null || x.TargetRole == requestedRole)
            && (!adminRoles || x.TargetRole is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin)))
        {
            if (permissions.Any(x => x.UserId == preauth.UserId && x.Role == preauth.TargetRole)) continue;
            var preauthEmail = identifiers.FirstOrDefault(x => x.UserId == preauth.UserId && x.Kind == "Email")?.DeliveryAddress;
            if (!Matches(filter.Search, preauth.DisplayName, preauthEmail, preauth.PublicId)) continue;
            var lifecycle = lifecycles.GetValueOrDefault(preauth.UserId);
            result.Add(Summary(preauth.UserId, preauth.TargetRole, lifecycle, [], [], identifiers, profiles, customers, grants,
                activity.GetValueOrDefault(preauth.UserId), preauth, null) with { Id = preauth.Id });
        }
        // Cashiers remain Business-owned. Their pending and active records are
        // visible here, but no PlatformAdmin write action is attached to them.
        if (!adminRoles && requestedRole is (null or ActorRole.Cashier))
        {
            var cashiers = await db.CashierPreauthorizations.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct);
            var businessNames = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business)
                .ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
            foreach (var cashier in cashiers)
            {
                var status = cashier.Status switch
                {
                    CashierPreauthorizationStatus.Active => "Active",
                    CashierPreauthorizationStatus.Disabled => "Disabled",
                    CashierPreauthorizationStatus.Revoked => "Revoked",
                    _ => "Pending"
                };
                if (!StatusMatches(status, filter.Status)) continue;
                var identifier = MaskPhone(cashier.CanonicalPhone);
                if (!Matches(filter.Search, cashier.DisplayName, identifier, businessNames.GetValueOrDefault(cashier.BusinessId))) continue;
                result.Add(new(cashier.Id, cashier.UserId, cashier.DisplayName, ActorRole.Cashier.ToString(), status,
                    status == "Active" ? "Active" : status, identifier, businessNames.GetValueOrDefault(cashier.BusinessId),
                    cashier.ActivatedAtUtc, false));
            }
        }
        return result.Where(x => StatusMatches(x.Status, filter.Status))
            .Where(x => string.IsNullOrWhiteSpace(filter.Approval) || string.Equals(x.ApprovalState, filter.Approval, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name).ThenBy(x => x.Role).Take(MaxDirectoryResults).ToArray();
    }

    public Task<AdminAccountDetail> DetailAsync(Actor actor, Guid id, CancellationToken ct)
        => DetailAsync(actor, id, null, ct);

    public async Task<AdminAccountDetail> DetailAsync(Actor actor, Guid id, string? requestedRole, CancellationToken ct)
    {
        DemandPlatform(actor);
        var permissionRows = await db.CommercePermissions.AsNoTracking().Where(x => x.UserId == id).ToListAsync(ct);
        var preauth = await db.AccountPreauthorizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (permissionRows.Count == 0 && preauth is null)
        {
            var cashier = await db.CashierPreauthorizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Account not found.");
            return await CashierDetailAsync(cashier, ct);
        }
        var userId = permissionRows.Count == 0 ? preauth!.UserId : id;
        var lifecycles = await db.AccountLifecycles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var identifiers = await db.AuthIdentifiers.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var profiles = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => permissionRows.Select(p => p.SubjectId).Contains(x.SubjectId)).ToListAsync(ct);
        var customers = await db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var grants = await db.AdminGrants.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var detailRole = permissionRows.FirstOrDefault(x => string.Equals(x.Role.ToString(), requestedRole, StringComparison.OrdinalIgnoreCase))?.Role
            ?? (preauth is not null && string.Equals(preauth.TargetRole.ToString(), requestedRole, StringComparison.OrdinalIgnoreCase) ? (ActorRole?)preauth.TargetRole : null)
            ?? permissionRows.FirstOrDefault()?.Role ?? preauth!.TargetRole;
        var enrollment = await db.RoleEnrollments.AsNoTracking().Where(x => x.UserId == userId && x.RequestedRole == detailRole)
            .OrderByDescending(x => x.SubmittedAtUtc).FirstOrDefaultAsync(ct);
        var summary = Summary(userId, detailRole, lifecycles,
            permissionRows, permissionRows, identifiers, profiles, customers, grants, null, preauth, enrollment);
        if (preauth is not null && permissionRows.Count == 0) summary = summary with { Id = preauth.Id };
        var safeProfiles = profiles.Select(x => new AdminAccountProfile(x.SubjectId, x.Role.ToString(), x.DisplayName, x.PublicId,
            x.Region, x.Category, permissionRows.Any(p => p.Role == x.Role && p.SubjectId == x.SubjectId && p.IsActive),
            permissionRows.FirstOrDefault(p => p.Role == x.Role && p.SubjectId == x.SubjectId)?.BusinessId)).ToArray();
        var audit = await SafeAuditAsync(userId, safeProfiles.Select(x => x.SubjectId).ToArray(), permissionRows.Select(x => x.BusinessId).OfType<Guid>().Distinct().ToArray(), ct);
        var transactions = await TransactionsAsync(userId, permissionRows, ct);
        object? roleData = null;
        if (permissionRows.Count == 0 && preauth is not null)
            roleData = null;
        else if (detailRole == ActorRole.Business)
            roleData = await BusinessDataAsync(userId, permissionRows, ct);
        else if (detailRole == ActorRole.Creator)
            roleData = await CreatorDataAsync(userId, permissionRows, ct);
        else if (detailRole == ActorRole.Customer)
            roleData = await CustomerDataAsync(userId, permissionRows, ct);
        else if (detailRole is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin)
        {
            var role = permissionRows.FirstOrDefault(x => x.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin)?.Role
                ?? preauth!.TargetRole;
            roleData = new AdminAdminData(role.ToString(), permissionRows.Any(x => x.Role == role && x.IsActive),
                grants.Where(x => x.Role == role).OrderByDescending(x => x.GrantedAtUtc).Select(x => (DateTime?)x.GrantedAtUtc).FirstOrDefault(),
                await db.AccountRoleHistory.AsNoTracking().CountAsync(x => x.TargetUserId == userId && x.TargetRole == role, ct));
        }
        return new(summary, permissionRows.Select(x => x.Role.ToString()).Distinct().OrderBy(x => x).ToArray(), safeProfiles,
            audit, transactions, audit.Cast<object>().ToArray(), roleData);
    }

    public async Task<IReadOnlyList<AdminAccountSummary>> BusinessCashiersAsync(Actor actor, Guid businessId, CancellationToken ct)
    {
        DemandPlatform(actor);
        var businessName = await db.PublicWorkspaceProfiles.AsNoTracking()
            .Where(x => x.SubjectId == businessId && x.Role == ActorRole.Business)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Business not found.");
        var rows = await db.CashierPreauthorizations.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.DisplayName).Take(MaxDirectoryResults).ToListAsync(ct);
        return rows.Select(x => new AdminAccountSummary(x.Id, x.UserId, x.DisplayName, ActorRole.Cashier.ToString(),
            x.Status.ToString(), x.Status.ToString(), MaskPhone(x.CanonicalPhone), businessName, x.ActivatedAtUtc, false)).ToArray();
    }

    public Task<AccountPreauthorizationResult> PreauthorizeAsync(Actor actor, AccountPreauthorizationInput input, string key, CancellationToken ct)
        => PreauthorizeAsync(AuthorityContext.ForAuthenticatedActor(actor), input, key, ct);

    public async Task<AccountPreauthorizationResult> PreauthorizeAsync(AuthorityContext authority, AccountPreauthorizationInput input, string key, CancellationToken ct)
    {
        await DemandPlatformAsync(authority, ct);
        var actor = authority.CommandActor;
        if (!TryRole(input.Role, out var role, allowEmpty: false) || !PreauthorizedRoles.Contains(role))
            throw new ApplicationFailure(FailureKind.Validation, "Only Customer, Creator, Business, Operations Admin, or Platform Admin accounts can be preauthorized here.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200) throw new ApplicationFailure(FailureKind.Validation, "A request reference is required.");
        var email = string.IsNullOrWhiteSpace(input.Email) ? null : EmailAuthService.NormalizeEmailAddress(input.Email);
        var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : PhoneNumberNormalizer.Normalize(input.Phone);
        if (email is null && phone is null) throw new ApplicationFailure(FailureKind.Validation, "Enter a canonical email or phone identifier.");
        var name = CleanRequired(input.DisplayName, 120, "Enter a display name.");
        var publicId = CleanOptional(input.PublicId, 80);
        if (role is ActorRole.Creator or ActorRole.Business && string.IsNullOrWhiteSpace(publicId))
            throw new ApplicationFailure(FailureKind.Validation, "A public profile identifier is required for this account type.");
        if (publicId is not null && await db.PublicWorkspaceProfiles.AsNoTracking().AnyAsync(x => x.Role == role && x.PublicId == publicId, ct))
            throw new ApplicationFailure(FailureKind.Validation, "That public profile identifier is already in use.");
        var region = CleanOptional(input.Region, 80);
        var category = CleanOptional(input.Category, 80);
        var submission = CleanOptional(input.Submission, 3000);
        var reason = CleanOptional(input.Reason, 500);
        if (role == ActorRole.PlatformAdmin && reason is null)
            throw new ApplicationFailure(FailureKind.Validation, "Enter an explicit reason for Platform Admin provisioning.");
        var fingerprint = RequestFingerprint.Create(role.ToString(), email ?? "", phone ?? "", name, publicId ?? "", region ?? "", category ?? "", submission ?? "", reason ?? "");
        if (!delivery.Enabled) throw new AuthChallengeUnavailableException("Account activation delivery is temporarily unavailable.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.UserId && x.OperationType == "AccountPreauthorize" && x.Key == key, ct);
        if (prior is not null)
        {
            if (prior.RequestFingerprint != fingerprint) throw new ApplicationFailure(FailureKind.IdempotencyConflict, "This request reference was already used.");
            var priorRow = await db.AccountPreauthorizations.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(prior.ResultReference), ct);
            return Result(priorRow);
        }
        var now = Now;
        var emailHash = email is null ? null : EmailAuthService.HashIdentifier(email);
        var emailIdentity = emailHash is null ? null : await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Email" && x.IdentifierHash == emailHash, ct);
        var phoneHash = phone is null ? null : EmailAuthService.HashIdentifier(phone);
        var phoneIdentity = phoneHash is null ? null : await db.AuthIdentifiers.AsTracking().SingleOrDefaultAsync(x => x.Kind == "Phone" && x.IdentifierHash == phoneHash, ct);
        if (emailIdentity is not null && phoneIdentity is not null && emailIdentity.UserId != phoneIdentity.UserId)
            throw new ApplicationFailure(FailureKind.Validation, "The identity references belong to different Weymela accounts.");
        var userId = emailIdentity?.UserId ?? phoneIdentity?.UserId ?? Guid.NewGuid();
        if (emailIdentity is not null && emailIdentity.UserId != userId || phoneIdentity is not null && phoneIdentity.UserId != userId)
            throw new ApplicationFailure(FailureKind.Validation, "The identity references cannot be safely combined.");
        if (role == ActorRole.PlatformAdmin && userId == actor.UserId)
            throw new ApplicationFailure(FailureKind.Forbidden, "A Platform Admin cannot provision a Platform Admin role for itself.", code: "PlatformAdminSelfProvisioningDenied");
        if (await db.CommercePermissions.AnyAsync(x => x.UserId == userId && x.Role == role, ct))
            throw new ApplicationFailure(FailureKind.Validation, "This account already has that role.");
        if (role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin
            && await db.CommercePermissions.AnyAsync(x => x.UserId == userId
                && x.IsActive && (x.Role == ActorRole.PlatformAdmin || x.Role == ActorRole.OperationsAdmin), ct))
            throw new ApplicationFailure(FailureKind.Validation, "An active administrative role must be changed through its existing protected lifecycle.", code: "AdminRoleCollision");
        if (await db.AccountPreauthorizations.AnyAsync(x => x.UserId == userId && x.TargetRole == role && x.Status == AccountPreauthorizationStatus.Pending, ct))
            throw new ApplicationFailure(FailureKind.Validation, "This account already has a pending preauthorization for that role.");
        if (emailIdentity is null && emailHash is not null)
            db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Email", IdentifierHash = emailHash, DeliveryAddress = email, IsVerified = false, CreatedAtUtc = now });
        if (phone is not null && phoneIdentity is null)
            db.AuthIdentifiers.Add(new AuthIdentifierRecord { UserId = userId, Kind = "Phone", IdentifierHash = phoneHash!, DeliveryAddress = phone, IsVerified = false, CreatedAtUtc = now });
        if (emailHash is null)
        {
            var existingVerifiedEmail = await db.AuthIdentifiers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Kind == "Email" && x.IsVerified, ct);
            if (existingVerifiedEmail is null)
                throw new ApplicationFailure(FailureKind.Validation, "A new account must have an email so the existing verification and password security flow can be used.");
            emailHash = existingVerifiedEmail.IdentifierHash;
            email = existingVerifiedEmail.DeliveryAddress is null ? null : EmailAuthService.NormalizeEmailAddress(existingVerifiedEmail.DeliveryAddress);
        }
        var deliveryAddress = email ?? throw new ApplicationFailure(FailureKind.Validation, "A verified email delivery address is required for account activation.");
        var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled)
            throw new ApplicationFailure(FailureKind.Validation, "Reactivate the account before adding a profile.");
        var hasActivePermission = await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsActive, ct);
        var lifetime = Math.Clamp(input.ExpiryDays ?? 7, 1, 30);
        var preauthorizationId = Guid.NewGuid();
        var secret = $"{preauthorizationId:N}-{NewSecret()}";
        var preauth = new AccountPreauthorizationRecord
        {
            Id = preauthorizationId,
            UserId = userId, TargetRole = role, EmailIdentifierHash = emailHash, PhoneIdentifierHash = phoneHash,
            DisplayName = name, PublicId = publicId, Region = region, Category = category, SubmissionJson = submission,
            CreatedByUserId = actor.UserId, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(lifetime),
            ActivationSecretExpiresAtUtc = now.AddDays(lifetime), ActivationSecretHash = HashSecret(secret), Version = 1
        };
        db.AccountPreauthorizations.Add(preauth);
        if (lifecycle is null)
            db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = userId, Status = hasActivePermission ? AccountLifecycleStatus.Active : AccountLifecycleStatus.Pending, ChangedByUserId = actor.UserId, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 });
        var correlation = Guid.NewGuid();
        var eventType = role == ActorRole.PlatformAdmin ? "PlatformAdminAccountPreauthorized" : "AccountPreauthorized";
        var auditReason = reason ?? "PlatformAdmin preauthorization";
        db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, userId, role, null, null, "Preauthorized", auditReason, now, correlation, preauth.Id));
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), eventType, actor.UserId, null, null, null, correlation, now,
            $"preauthorization={preauth.Id:D}", TargetUserId: userId, TargetRole: role, Operation: "preauthorize", Reason: auditReason));
        db.OutboxMessages.Add(new OutboxMessage { EventType = eventType, Payload = JsonSerializer.Serialize(new { preauth.Id, TargetUserId = userId, Role = role.ToString() }), OccurredAtUtc = now });
        db.IdempotencyRecords.Add(new(actor.UserId, "AccountPreauthorize", key, fingerprint, preauth.Id.ToString(), now));
        // The raw invitation is sent only to the target mailbox. It is never
        // returned to the administrator, persisted, logged, or audited.
        await delivery.SendAsync(deliveryAddress, secret, EmailCodePurpose.AdminAccountActivation, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result(preauth);
    }

    public async Task<AdminAccountDetail> ActivateAsync(Actor target, Guid preauthorizationId, string activationSecret, CancellationToken ct,
        AccountLegalConfirmation? accountLegal = null, string? ipReference = null, string? userAgentReference = null)
    {
        if (target.UserId == Guid.Empty || string.IsNullOrWhiteSpace(activationSecret)) throw new ApplicationFailure(FailureKind.Validation, "Activation could not be completed.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.AccountPreauthorizations.AsTracking().SingleOrDefaultAsync(x => x.Id == preauthorizationId, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "Activation request not found.");
        var now = Now;
        if (row.UserId != target.UserId || row.Status != AccountPreauthorizationStatus.Pending
            || row.ActivationSecretExpiresAtUtc <= now
            || row.ActivationSecretExpiresAtUtc != row.ExpiresAtUtc
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(row.ActivationSecretHash), SHA256.HashData(Encoding.UTF8.GetBytes(activationSecret))))
        {
            if (row.Status == AccountPreauthorizationStatus.Pending && row.ActivationSecretExpiresAtUtc <= now)
            {
                row.Status = AccountPreauthorizationStatus.Expired;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            throw new ApplicationFailure(FailureKind.Forbidden, "This activation request is invalid, expired, or already used.");
        }
        if (!await db.AuthIdentifiers.AsNoTracking().AnyAsync(x => x.UserId == target.UserId && x.Kind == "Email" && x.IsVerified, ct)
            || !await db.PasswordCredentials.AsNoTracking().AnyAsync(x => x.UserId == target.UserId, ct)
            || !await db.AuthorizedDevices.AsNoTracking().AnyAsync(x => x.UserId == target.UserId && x.RevokedAtUtc == null && x.PinVerifier != null, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "Verify the account and establish the password and device PIN before activation.");
        var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == target.UserId, ct);
        if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled)
            throw new ApplicationFailure(FailureKind.Forbidden, "This account is not eligible for activation.");
        if (await db.CommercePermissions.AnyAsync(x => x.UserId == target.UserId && x.Role == row.TargetRole, ct))
            throw new ApplicationFailure(FailureKind.Validation, "This account already has a profile for that role.");
        now = Now; var correlation = Guid.NewGuid();
        Guid subject; Guid? businessId = null;
        switch (row.TargetRole)
        {
            case ActorRole.Customer:
                var legalService = new AccountLegalOnboardingService(db, clock);
                var legal = await legalService.StatusAsync(target.UserId, ct);
                if (!legal.Current && accountLegal is not null)
                {
                    await legalService.AcceptCurrentAsync(target.UserId, accountLegal, ipReference, userAgentReference, ct);
                    legal = legal with { Current = true };
                }
                if (!legal.Current) throw new ApplicationFailure(FailureKind.Validation, "Accept the current Terms of Service and Privacy Policy before activating the Customer profile.");
                subject = await NewCustomerIdAsync(ct);
                db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = row.TargetRole, DisplayName = row.DisplayName, PublicId = $"CU-{Guid.NewGuid():N}"[..15] });
                db.CustomerProfiles.Add(new CustomerProfileRecord { CustomerId = subject, UserId = target.UserId, PreferredName = row.DisplayName, CreatedAtUtc = now, UpdatedAtUtc = now });
                db.CustomerCashbackAccounts.Add(new CustomerCashbackAccount(subject));
                db.CommercePermissions.Add(new CommercePermission(target.UserId, row.TargetRole, subject, null, true, false));
                break;
            case ActorRole.Creator:
                subject = Guid.NewGuid();
                db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = row.TargetRole, DisplayName = row.DisplayName, PublicId = row.PublicId!, Region = row.Region ?? "", Category = row.Category ?? "" });
                db.CommercePermissions.Add(new CommercePermission(target.UserId, row.TargetRole, subject, null, true, false));
                break;
            case ActorRole.Business:
                businessId = Guid.NewGuid(); subject = businessId.Value;
                db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = subject, Role = row.TargetRole, DisplayName = row.DisplayName, PublicId = row.PublicId!, Region = row.Region ?? "", Category = row.Category ?? "" });
                db.BusinessWallets.Add(new BusinessWallet(businessId.Value));
                db.CommercePermissions.Add(new CommercePermission(target.UserId, row.TargetRole, subject, businessId, true, true));
                break;
            case ActorRole.OperationsAdmin:
                subject = target.UserId;
                db.CommercePermissions.Add(new CommercePermission(target.UserId, row.TargetRole, subject, null, true, false));
                db.AdminGrants.Add(new AdminGrantRecord { UserId = target.UserId, DisplayName = row.DisplayName, Role = row.TargetRole, GrantedByUserId = row.CreatedByUserId, GrantedAtUtc = now });
                break;
            case ActorRole.PlatformAdmin:
                subject = target.UserId;
                db.CommercePermissions.Add(new CommercePermission(target.UserId, row.TargetRole, subject, null, true, false));
                db.AdminGrants.Add(new AdminGrantRecord { UserId = target.UserId, DisplayName = row.DisplayName, Role = row.TargetRole, GrantedByUserId = row.CreatedByUserId, GrantedAtUtc = now });
                break;
            default: throw new ApplicationFailure(FailureKind.Validation, "This account type cannot be activated here.");
        }
        db.RoleEnrollments.Add(new RoleEnrollmentRecord
        {
            UserId = target.UserId, RequestedRole = row.TargetRole, SubmissionJson = row.SubmissionJson ?? "{}",
            Status = RoleEnrollmentStatus.Approved, SubmittedAtUtc = row.CreatedAtUtc, ReviewedAtUtc = now,
            ReviewedBy = row.CreatedByUserId, DecisionReason = "PlatformAdmin preauthorization activated.", IdempotencyKey = $"admin-preauth:{row.Id:D}"
        });
        if (lifecycle is null) db.AccountLifecycles.Add(new AccountLifecycleRecord { UserId = target.UserId, Status = AccountLifecycleStatus.Active, ChangedByUserId = row.CreatedByUserId, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 });
        else if (lifecycle.Status == AccountLifecycleStatus.Pending)
        {
            if (!AccountLifecyclePolicy.TryTransition(lifecycle.Status, "activate", out var desired))
                throw new ApplicationFailure(FailureKind.Validation, "This account cannot be activated from its current state.", code: "InvalidLifecycleTransition");
            lifecycle.Status = desired; lifecycle.ChangedByUserId = row.CreatedByUserId; lifecycle.UpdatedAtUtc = now;
        }
        else if (lifecycle.Status is not AccountLifecycleStatus.Active)
            throw new ApplicationFailure(FailureKind.Validation, "This account cannot be activated from its current state.", code: "InvalidLifecycleTransition");
        row.Status = AccountPreauthorizationStatus.Activated; row.ActivatedAtUtc = now; row.ActivationSecretHash = "";
        db.AccountRoleHistory.Add(new(Guid.NewGuid(), row.CreatedByUserId, target.UserId, row.TargetRole, subject, businessId, "Granted", "PlatformAdmin granted the preauthorized role/profile.", now, correlation, row.Id));
        db.AccountRoleHistory.Add(new(Guid.NewGuid(), row.CreatedByUserId, target.UserId, row.TargetRole, subject, businessId, "Activated", "Target established credentials and activated preauthorization.", now, correlation, row.Id));
        var activationEvent = row.TargetRole == ActorRole.PlatformAdmin ? "PlatformAdminAccountActivated" : "AccountPreauthorizationActivated";
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), activationEvent, row.CreatedByUserId, businessId, null,
            row.TargetRole == ActorRole.Creator ? subject : null, correlation, now, $"preauthorization={row.Id:D}", TargetUserId: target.UserId,
            TargetRole: row.TargetRole, TargetSubjectId: subject, Operation: "activate"));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await DetailAsync(new Actor(row.CreatedByUserId, ActorRole.PlatformAdmin), target.UserId, ct);
    }

    public Task<Guid> CancelPreauthorizationAsync(Actor actor, Guid preauthorizationId, string reason, string key, CancellationToken ct)
    {
        DemandPlatform(actor);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ApplicationFailure(FailureKind.Validation, "Enter a reason for cancelling the preauthorization.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            throw new ApplicationFailure(FailureKind.Validation, "A request reference is required.");
        var fingerprint = RequestFingerprint.Create(preauthorizationId.ToString("D"), reason.Trim());
        return new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var prior = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.ActorId == actor.UserId
                && x.OperationType == "AccountCancelPreauthorization" && x.Key == key, token);
            if (prior is not null)
            {
                if (prior.RequestFingerprint != fingerprint)
                    throw new ApplicationFailure(FailureKind.IdempotencyConflict, "This request reference was already used.");
                return Guid.Parse(prior.ResultReference);
            }
            var row = await db.AccountPreauthorizations.AsTracking().SingleOrDefaultAsync(x => x.Id == preauthorizationId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Preauthorization not found.");
            if (row.Status != AccountPreauthorizationStatus.Pending)
                throw new ApplicationFailure(FailureKind.Validation, "This preauthorization cannot be cancelled from its current state.", code: "InvalidLifecycleTransition");
            var now = Now; var correlation = Guid.NewGuid();
            row.Status = AccountPreauthorizationStatus.Cancelled; row.CancelledAtUtc = now; row.ActivationSecretHash = "";
            var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == row.UserId, token);
            var hasActivePermission = await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == row.UserId && x.IsActive, token);
            if (!hasActivePermission && lifecycle?.Status == AccountLifecycleStatus.Pending)
            {
                if (!AccountLifecyclePolicy.TryTransition(lifecycle.Status, "cancel", out var desired))
                    throw new ApplicationFailure(FailureKind.Validation, "This account cannot be cancelled from its current state.", code: "InvalidLifecycleTransition");
                lifecycle.Status = desired; lifecycle.Reason = reason.Trim(); lifecycle.ChangedByUserId = actor.UserId; lifecycle.UpdatedAtUtc = now;
            }
            db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, row.UserId, row.TargetRole, null, null, "Cancelled", reason.Trim(), now, correlation, row.Id));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AccountPreauthorizationCancelled", actor.UserId, null, null, null, correlation, now,
                $"preauthorization={row.Id:D}", TargetUserId: row.UserId, TargetRole: row.TargetRole, Operation: "cancel-preauthorization", Reason: reason.Trim()));
            db.IdempotencyRecords.Add(new(actor.UserId, "AccountCancelPreauthorization", key, fingerprint, row.Id.ToString("D"), now));
            return row.Id;
        }, ct);
    }

    public Task<Guid> ChangeLifecycleAsync(Actor actor, Guid userId, AccountLifecycleInput input, string key, CancellationToken ct)
    {
        DemandPlatform(actor); var action = input.Action.Trim().ToLowerInvariant();
        if (action is not ("suspend" or "disable" or "reactivate") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 500)
            throw new ApplicationFailure(FailureKind.Validation, "Choose a lifecycle action and enter a reason.");
        return new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var permissions = await db.CommercePermissions.AsTracking().Where(x => x.UserId == userId).ToListAsync(token);
            if (permissions.Count == 0) throw new ApplicationFailure(FailureKind.NotFound, "Account not found.");
            if (permissions.Any(x => x.Role == ActorRole.PlatformAdmin))
                throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin accounts are read-only in the Stage A account-management API.", code: "PlatformAdminLifecycleDenied");
            var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, token);
            if (input.ExpectedVersion is not null && (lifecycle is null || lifecycle.Version != input.ExpectedVersion))
                throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "The account changed. Refresh before trying again.");
            var currentStatus = lifecycle?.Status ?? AccountLifecycleStatus.Active;
            var desired = action switch { "suspend" => AccountLifecycleStatus.Suspended, "disable" => AccountLifecycleStatus.Disabled, _ => AccountLifecycleStatus.Active };
            if (!AccountLifecyclePolicy.TryTransition(currentStatus, action, out var policyDesired) || policyDesired != desired)
                throw new ApplicationFailure(FailureKind.Validation, $"Cannot {action} an account in {currentStatus} state.", code: "InvalidLifecycleTransition");
            var now = Now;
            if (lifecycle is null) db.AccountLifecycles.Add(lifecycle = new AccountLifecycleRecord { UserId = userId, Status = desired, Reason = input.Reason.Trim(), ChangedByUserId = actor.UserId, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 });
            else { lifecycle.Status = desired; lifecycle.Reason = input.Reason.Trim(); lifecycle.ChangedByUserId = actor.UserId; lifecycle.UpdatedAtUtc = now; }
            var correlation = Guid.NewGuid();
            var historyAction = action switch { "suspend" => "Suspended", "disable" => "Disabled", _ => "Reactivated" };
            foreach (var permission in permissions)
            {
                db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, userId, permission.Role, permission.SubjectId, permission.BusinessId,
                    historyAction, input.Reason.Trim(), now, correlation));
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), $"Account{historyAction}", actor.UserId, permission.BusinessId, null,
                    permission.Role == ActorRole.Creator ? permission.SubjectId : null, correlation, now, "account-lifecycle", TargetUserId: userId,
                    TargetRole: permission.Role, TargetSubjectId: permission.SubjectId, Operation: action, Reason: input.Reason.Trim()));
            }
            return userId;
        }, ct);
    }

    public Task<Guid> RevokeProfileAsync(Actor actor, Guid userId, RevokeAccountProfileInput input, string key, CancellationToken ct)
    {
        DemandPlatform(actor);
        if (!TryRole(input.Role, out var role, allowEmpty: false) || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 500)
            throw new ApplicationFailure(FailureKind.Validation, "Choose a role and enter a reason.");
        if (role == ActorRole.PlatformAdmin)
            throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin profiles are read-only in the Stage A account-management API.", code: "PlatformAdminProfileRevokeDenied");
        return new EfUnitOfWork(db, IsolationLevel.Serializable).ExecuteAsync(async token =>
        {
            var permission = await db.CommercePermissions.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Role == role && (input.SubjectId == null || x.SubjectId == input.SubjectId) && x.IsActive, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "Active account profile not found.");
            var lifecycle = await db.AccountLifecycles.AsTracking().SingleOrDefaultAsync(x => x.UserId == userId, token);
            if (input.ExpectedVersion is not null && (lifecycle is null || lifecycle.Version != input.ExpectedVersion))
                throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "The account changed. Refresh before trying again.");
            permission.IsActive = false;
            foreach (var grant in await db.AdminGrants.AsTracking().Where(x => x.UserId == userId && x.Role == role && x.IsActive).ToListAsync(token))
            { grant.IsActive = false; grant.RevokedByUserId = actor.UserId; grant.RevokedAtUtc = Now; }
            var now = Now; var correlation = Guid.NewGuid();
            if (lifecycle is null)
            {
                lifecycle = new AccountLifecycleRecord { UserId = userId, Status = AccountLifecycleStatus.Active, ChangedByUserId = actor.UserId, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 };
                db.AccountLifecycles.Add(lifecycle);
            }
            else
            {
                lifecycle.Reason = input.Reason.Trim(); lifecycle.ChangedByUserId = actor.UserId; lifecycle.UpdatedAtUtc = now;
            }
            db.AccountRoleHistory.Add(new(Guid.NewGuid(), actor.UserId, userId, role, permission.SubjectId, permission.BusinessId, "Revoked", input.Reason.Trim(), now, correlation));
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), "AccountProfileRevoked", actor.UserId, permission.BusinessId, null,
                role == ActorRole.Creator ? permission.SubjectId : null, correlation, now, "profile-revoked", TargetUserId: userId,
                TargetRole: role, TargetSubjectId: permission.SubjectId, Operation: "revoke-profile", Reason: input.Reason.Trim()));
            return permission.SubjectId;
        }, ct);
    }

    private async Task<AdminAccountDetail> CashierDetailAsync(CashierPreauthorization cashier, CancellationToken ct)
    {
        var businessName = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.SubjectId == cashier.BusinessId && x.Role == ActorRole.Business).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        var count = cashier.UserId is null ? 0 : await db.VerifiedSales.AsNoTracking().CountAsync(x => x.CashierId == cashier.UserId, ct);
        var transactions = cashier.UserId is null ? [] : await db.VerifiedSales.AsNoTracking().Where(x => x.CashierId == cashier.UserId).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
        var summary = new AdminAccountSummary(cashier.Id, cashier.UserId, cashier.DisplayName, ActorRole.Cashier.ToString(), cashier.Status.ToString(), cashier.Status.ToString(), MaskPhone(cashier.CanonicalPhone), businessName, cashier.ActivatedAtUtc, false);
        var mapped = transactions.Select(Transaction).ToArray();
        var audit = cashier.UserId is null ? [] : await SafeAuditAsync(cashier.UserId.Value, [], [cashier.BusinessId], ct);
        return new(summary, [ActorRole.Cashier.ToString()], [], audit, mapped, audit.Cast<object>().ToArray(), new AdminCashierData(cashier.BusinessId, businessName, cashier.Status.ToString(), count));
    }

    private async Task<AdminBusinessData> BusinessDataAsync(Guid userId, IReadOnlyList<CommercePermission> permissions, CancellationToken ct)
    {
        var businessIds = permissions.Where(x => x.Role == ActorRole.Business).Select(x => x.SubjectId).ToArray();
        if (businessIds.Length == 0) return new(null, null, 0, 0, 0, 0, 0, 0);
        var businessId = businessIds[0]; var wallet = await db.BusinessWallets.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == businessId, ct);
        return new(wallet?.AvailableBalance.Amount, wallet?.ReservedBalance.Amount,
            await db.Promotions.CountAsync(x => x.BusinessId == businessId, ct),
            await db.UgcOpportunities.CountAsync(x => x.BusinessId == businessId, ct),
            await db.UgcCustomerOffers.CountAsync(x => x.BusinessId == businessId, ct),
            await db.CashierPreauthorizations.CountAsync(x => x.BusinessId == businessId, ct),
            await db.DepositRequests.CountAsync(x => x.BusinessId == businessId, ct),
            await db.VerifiedSales.CountAsync(x => x.BusinessId == businessId, ct) + await db.UgcCustomerOfferSales.CountAsync(x => x.BusinessId == businessId, ct));
    }

    private async Task<AdminCreatorData> CreatorDataAsync(Guid userId, IReadOnlyList<CommercePermission> permissions, CancellationToken ct)
    {
        var ids = permissions.Where(x => x.Role == ActorRole.Creator).Select(x => x.SubjectId).ToArray(); if (ids.Length == 0) return new(0, 0, 0, 0, null, 0, []);
        var creator = ids[0]; var allocationIds = await db.CreatorAllocations.AsNoTracking().Where(x => x.CreatorId == creator).Select(x => x.Id).ToArrayAsync(ct);
        var earnings = await db.CreatorEarningsAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CreatorId == creator, ct);
        var socialProfiles = (await db.CreatorSocialProfiles.AsNoTracking().Where(x => x.CreatorId == creator && x.IsActive)
            .OrderBy(x => x.Platform).ThenBy(x => x.ProfileUrl).ToListAsync(ct))
            .Select(x => new AdminCreatorSocialProfile(x.Id, x.Platform.ToString(), x.ProfileUrl, x.SelfReportedAudience,
                x.VerificationStatus, x.VerifiedAudience)).ToArray();
        return new(await db.CreatorApplications.CountAsync(x => x.CreatorId == creator, ct), allocationIds.Length,
            await db.CreatorPromotionContentSubmissions.CountAsync(x => allocationIds.Contains(x.CreatorAllocationId), ct),
            await db.CreatorPromotionParticipations.CountAsync(x => x.CreatorId == creator, ct), earnings?.AvailableEarnings.Amount,
            await db.PayoutRecords.CountAsync(x => x.CreatorId == creator, ct), socialProfiles);
    }

    private async Task<AdminCustomerData> CustomerDataAsync(Guid userId, IReadOnlyList<CommercePermission> permissions, CancellationToken ct)
    {
        var ids = permissions.Where(x => x.Role == ActorRole.Customer).Select(x => x.SubjectId).ToArray(); if (ids.Length == 0) return new(null, 0, 0, 0);
        var customer = ids[0]; var cashback = await db.CustomerCashbackAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customer, ct);
        return new(cashback?.AvailableCashback.Amount, await db.VerifiedSales.CountAsync(x => x.CustomerId == customer, ct) + await db.UgcCustomerOfferSales.CountAsync(x => x.CustomerId == customer, ct),
            await db.OfferQrSessions.CountAsync(x => x.CustomerId == customer, ct), await db.PayoutRecords.CountAsync(x => x.CustomerId == customer, ct));
    }

    private async Task<IReadOnlyList<AdminCommerceTransaction>> TransactionsAsync(Guid userId, IReadOnlyList<CommercePermission> permissions, CancellationToken ct)
    {
        var subjects = permissions.Select(x => x.SubjectId).ToHashSet(); var businesses = permissions.Where(x => x.BusinessId != null).Select(x => x.BusinessId!.Value).ToHashSet();
        var sales = await db.VerifiedSales.AsNoTracking().Where(x => x.CustomerId == userId || x.CreatorId == userId || x.CashierId == userId || businesses.Contains(x.BusinessId) || subjects.Contains(x.BusinessId)).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
        return sales.Select(Transaction).ToArray();
    }

    private async Task<IReadOnlyList<AdminAuditItem>> SafeAuditAsync(Guid userId, IReadOnlyCollection<Guid> subjects, IReadOnlyCollection<Guid> businesses, CancellationToken ct)
        => await db.AuditEvents.AsNoTracking().Where(x => x.ActorId == userId || x.TargetUserId == userId || (x.TargetSubjectId != null && subjects.Contains(x.TargetSubjectId.Value)) || (x.BusinessId != null && businesses.Contains(x.BusinessId.Value)))
            .OrderByDescending(x => x.OccurredAtUtc).Take(100).Select(x => new AdminAuditItem(x.Id, x.Operation ?? x.EventType, x.EventType, x.OccurredAtUtc, x.ActorId, x.TargetUserId, x.TargetRole == null ? null : x.TargetRole.ToString(), x.TargetSubjectId, x.CorrelationId)).ToListAsync(ct);

    private static AdminAccountSummary Summary(Guid userId, ActorRole role, AccountLifecycleRecord? lifecycle, IReadOnlyList<CommercePermission> permissions,
        IReadOnlyList<CommercePermission> allUserPermissions,
        IReadOnlyList<AuthIdentifierRecord> identifiers, IReadOnlyList<PublicWorkspaceProfile> profiles, IReadOnlyList<CustomerProfileRecord> customers,
        IReadOnlyList<AdminGrantRecord> grants, DateTime? activity, AccountPreauthorizationRecord? preauth, RoleEnrollmentRecord? enrollment)
    {
        var profile = permissions.Where(x => x.Role == role).Select(x => profiles.FirstOrDefault(p => p.SubjectId == x.SubjectId && p.Role == role)).FirstOrDefault(x => x is not null);
        var grant = grants.Where(x => x.UserId == userId && x.Role == role).OrderByDescending(x => x.GrantedAtUtc).FirstOrDefault();
        var customer = customers.FirstOrDefault(x => x.UserId == userId);
        var email = identifiers.FirstOrDefault(x => x.UserId == userId && x.Kind == "Email" && x.DeliveryAddress != null)?.DeliveryAddress;
        var status = lifecycle?.Status.ToString() ?? (permissions.Any(x => x.IsActive) ? "Active" : "Inactive");
        if (preauth?.Status == AccountPreauthorizationStatus.Pending && permissions.All(x => !x.IsActive)) status = "Pending";
        if (preauth is not null && preauth.Status != AccountPreauthorizationStatus.Pending && permissions.All(x => !x.IsActive)) status = preauth.Status.ToString();
        var name = profile?.DisplayName ?? grant?.DisplayName ?? customer?.PreferredName ?? preauth?.DisplayName ?? NameFromEmail(email);
        var identifier = email is null ? "Verified Weymela account" : MaskEmail(email);
        var approval = preauth?.Status == AccountPreauthorizationStatus.Pending ? "Pending"
            : role is ActorRole.Business or ActorRole.Creator ? enrollment?.Status.ToString() ?? "Not recorded" : "Active";
        return new(userId, userId, name, role.ToString(), status, approval, identifier,
            permissions.FirstOrDefault(x => x.Role == role)?.BusinessId?.ToString("D"), activity ?? preauth?.CreatedAtUtc,
            role is not (ActorRole.Cashier or ActorRole.PlatformAdmin)
                && (permissions.Any(x => x.IsActive) || preauth?.Status == AccountPreauthorizationStatus.Pending));
    }

    private static AdminCommerceTransaction Transaction(VerifiedSale x)
        => new(x.Id, x.CreatedAtUtc, x.PurchaseAmount.Amount, x.PurchaseAmount.Currency, x.Status.ToString(), x.BusinessId, x.CreatorId, x.CustomerId, x.CashierId);

    private static AccountPreauthorizationResult Result(AccountPreauthorizationRecord row)
        => new(row.Id, row.UserId, row.TargetRole.ToString(), row.Status.ToString(), row.ExpiresAtUtc, true);

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string HashSecret(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    private async Task<Guid> NewCustomerIdAsync(CancellationToken ct)
    {
        for (var i = 0; i < 5; i++) { var candidate = Guid.NewGuid(); if (!await db.CustomerProfiles.AnyAsync(x => x.CustomerId == candidate, ct)) return candidate; }
        throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "The Customer profile could not be created. Try again.");
    }
    private static string CleanRequired(string? value, int max, string message) { var clean = value?.Trim(); if (string.IsNullOrWhiteSpace(clean) || clean.Length > max) throw new ApplicationFailure(FailureKind.Validation, message); return clean; }
    private static string? CleanOptional(string? value, int max) { var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (clean?.Length > max) throw new ApplicationFailure(FailureKind.Validation, "Profile information is too long."); return clean; }
    private static bool TryRole(string? value, out ActorRole role, bool allowEmpty)
    { if (string.IsNullOrWhiteSpace(value) && allowEmpty) { role = default; return true; } var normalized = value?.Replace(" ", "", StringComparison.Ordinal) ?? ""; return Enum.TryParse(normalized, true, out role) && AllRoles.Contains(role); }
    private static bool StatusMatches(string status, string? filter) => string.IsNullOrWhiteSpace(filter) || string.Equals(status, filter, StringComparison.OrdinalIgnoreCase);
    private static bool Matches(string? search, params string?[] values) => string.IsNullOrWhiteSpace(search) || values.Any(x => x?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true);
    private static string NameFromEmail(string? email) => string.IsNullOrWhiteSpace(email) ? "Weymela account" : email.Split('@')[0];
    private static string MaskEmail(string email) { var parts = email.Split('@'); var local = parts[0]; return (local.Length <= 2 ? local[0] + "*" : local[0] + new string('*', Math.Min(6, local.Length - 2)) + local[^1]) + "@" + parts[1]; }
    private static string MaskPhone(string phone) => phone.Length <= 4 ? "****" : new string('*', Math.Max(0, phone.Length - 4)) + phone[^4..];
    private static void DemandPlatform(Actor actor) { if (actor.Role != ActorRole.PlatformAdmin) throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin access is required."); }

    private async Task DemandPlatformAsync(AuthorityContext authority, CancellationToken ct)
    {
        if (authority.RealActor.Role != ActorRole.PlatformAdmin
            || !authority.Authority.IsPlatformAdmin
            || authority.CommandActor.UserId == Guid.Empty)
            throw new ApplicationFailure(FailureKind.Forbidden, "An active real Platform Admin authority is required.", code: "PlatformAdminAuthorityRequired");
        var lifecycle = await db.AccountLifecycles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == authority.CommandActor.UserId, ct);
        if (lifecycle?.Status is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Disabled or AccountLifecycleStatus.Cancelled or AccountLifecycleStatus.Revoked)
            throw new ApplicationFailure(FailureKind.Forbidden, "The Platform Admin account is not active.", code: "PlatformAdminInactive");
        if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.UserId == authority.CommandActor.UserId
            && x.Role == ActorRole.PlatformAdmin && x.SubjectId == authority.CommandActor.UserId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "The Platform Admin account is not active.", code: "PlatformAdminInactive");
    }
}
