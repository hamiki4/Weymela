using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class RestrictedRoleGrantTests(PostgresFixture fixture)
{
    private static readonly DateTime Now = new(2026, 9, 14, 20, 0, 0, DateTimeKind.Utc);
    private static readonly string CryptoKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    [Fact]
    public async Task Runtime_grants_are_exact_and_real_flows_succeed_under_restricted_roles()
    {
        var database = await fixture.CreateAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        var suffix = databaseName[^8..];
        var apiRole = $"api_test_{suffix}";
        var workerRole = $"worker_test_{suffix}";
        var migratorRole = $"migrator_test_{suffix}";
        var backupRole = $"backup_test_{suffix}";
        var apiPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var workerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var migratorPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var backupPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        await AssertMigrationOrderAsync(database.ConnectionString);
        await SeedLegalDocumentsAsync(database);
        var adminUserId = await SeedPlatformAdminAsync(database);
        await CreateRuntimeRoleAsync(database.ConnectionString, apiRole, apiPassword);
        await CreateRuntimeRoleAsync(database.ConnectionString, workerRole, workerPassword);
        await CreateRuntimeRoleAsync(database.ConnectionString, migratorRole, migratorPassword);
        await CreateRuntimeRoleAsync(database.ConnectionString, backupRole, backupPassword);
        await AssignMigrationOwnershipAsync(database.ConnectionString, migratorRole);
        await SeedUnsafeRuntimePrivilegesAsync(database.ConnectionString, apiRole, workerRole);

        var root = RepositoryRoot();
        var apiScript = new FileInfo(Path.Combine(root.FullName, "database/grants/v3-api.sql"));
        var workerScript = new FileInfo(Path.Combine(root.FullName, "database/grants/v3-worker.sql"));
        var verifyScript = new FileInfo(Path.Combine(root.FullName, "database/grants/v3-verify.sql"));
        var migratorScript = new FileInfo(Path.Combine(root.FullName, "database/grants/v3-migrator.sql"));
        var backupScript = new FileInfo(Path.Combine(root.FullName, "database/grants/v3-backup.sql"));
        var defaultsScript = new FileInfo(Path.Combine(root.FullName,
            "database/grants/v3-migrator-defaults.sql"));
        AssertGrantScriptContract(apiScript);
        AssertGrantScriptContract(workerScript);
        AssertOperationalScriptContract(migratorScript);
        AssertOperationalScriptContract(backupScript);
        AssertOperationalScriptContract(defaultsScript);
        await ExecuteScriptAsync(database.ConnectionString, apiScript,
            [("api_role", apiRole), ("database_name", databaseName)]);
        await ExecuteScriptAsync(database.ConnectionString, workerScript,
            [("worker_role", workerRole), ("database_name", databaseName)]);
        await ExecuteScriptAsync(database.ConnectionString, migratorScript,
            [("migrator_role", migratorRole), ("database_name", databaseName)]);
        await ExecuteScriptAsync(database.ConnectionString, backupScript,
            [("backup_role", backupRole), ("database_name", databaseName)]);
        var migratorConnection = RuntimeConnection(database.ConnectionString, migratorRole,
            migratorPassword);
        await ExecuteScriptAsync(migratorConnection, defaultsScript,
            [("backup_role", backupRole)]);
        await ExecuteScriptAsync(database.ConnectionString, verifyScript,
            VerifyVariables(apiRole, workerRole, migratorRole, backupRole, databaseName));

        var apiConnection = RuntimeConnection(database.ConnectionString, apiRole, apiPassword);
        var workerConnection = RuntimeConnection(database.ConnectionString, workerRole, workerPassword);
        var backupConnection = RuntimeConnection(database.ConnectionString, backupRole, backupPassword);
        var account = await ExerciseApiFlowsAsync(apiConnection, adminUserId);
        var workerSeed = await SeedWorkerFlowAsync(database, account.CreatorId, account.CreatorUserId);
        await ExerciseWorkerFlowsAsync(workerConnection, workerSeed);

        await AssertApiDenialsAsync(apiConnection, workerConnection, apiRole, workerRole,
            new NpgsqlConnectionStringBuilder(database.ConnectionString).Username!);
        await AssertWorkerDenialsAsync(workerConnection, apiRole, workerRole,
            new NpgsqlConnectionStringBuilder(database.ConnectionString).Username!);
        await AssertPersistedOutcomesAsync(database, account, workerSeed);
        await ExerciseOperationalRolesAsync(database, databaseName, suffix, migratorConnection,
            backupConnection, apiConnection, apiRole, workerRole, migratorRole, backupRole,
            verifyScript);

        // The verifier must reject excess access, not merely prove that required grants exist.
        await ExecuteOwnerAsync(database.ConnectionString,
            $"GRANT SELECT ON TABLE v3.\"AuthIdentifiers\" TO {QuoteIdentifier(workerRole)}");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteScriptAsync(database.ConnectionString,
            verifyScript, VerifyVariables(apiRole, workerRole, migratorRole, backupRole,
                databaseName)));
        await ExecuteOwnerAsync(database.ConnectionString,
            $"REVOKE SELECT ON TABLE v3.\"AuthIdentifiers\" FROM {QuoteIdentifier(workerRole)}");
        await ExecuteOwnerAsync(database.ConnectionString,
            $"GRANT UPDATE ON TABLE v3.\"AuthIdentifiers\" TO {QuoteIdentifier(backupRole)}");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteScriptAsync(database.ConnectionString,
            verifyScript, VerifyVariables(apiRole, workerRole, migratorRole, backupRole,
                databaseName)));
        await ExecuteOwnerAsync(database.ConnectionString,
            $"REVOKE UPDATE ON TABLE v3.\"AuthIdentifiers\" FROM {QuoteIdentifier(backupRole)}");
        await ExecuteOwnerAsync(database.ConnectionString,
            $"GRANT CONNECT ON DATABASE {QuoteIdentifier(databaseName)} TO PUBLIC");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteScriptAsync(database.ConnectionString,
            verifyScript, VerifyVariables(apiRole, workerRole, migratorRole, backupRole,
                databaseName)));
        await ExecuteOwnerAsync(database.ConnectionString,
            $"REVOKE CONNECT ON DATABASE {QuoteIdentifier(databaseName)} FROM PUBLIC");
        await ExecuteOwnerAsync(database.ConnectionString,
            "GRANT SELECT ON TABLE v3.\"InAppNotifications\" TO PUBLIC");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteScriptAsync(database.ConnectionString,
            verifyScript, VerifyVariables(apiRole, workerRole, migratorRole, backupRole,
                databaseName)));
        await ExecuteOwnerAsync(database.ConnectionString,
            "REVOKE SELECT ON TABLE v3.\"InAppNotifications\" FROM PUBLIC");
        await ExecuteOwnerAsync(database.ConnectionString,
            $"ALTER DEFAULT PRIVILEGES FOR ROLE {QuoteIdentifier(migratorRole)} IN SCHEMA v3 GRANT SELECT ON TABLES TO PUBLIC");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteScriptAsync(database.ConnectionString,
            verifyScript, VerifyVariables(apiRole, workerRole, migratorRole, backupRole,
                databaseName)));
        await ExecuteOwnerAsync(database.ConnectionString,
            $"ALTER DEFAULT PRIVILEGES FOR ROLE {QuoteIdentifier(migratorRole)} IN SCHEMA v3 REVOKE SELECT ON TABLES FROM PUBLIC");
        await ExecuteScriptAsync(database.ConnectionString, verifyScript,
            VerifyVariables(apiRole, workerRole, migratorRole, backupRole, databaseName));
    }

    private static async Task<ApiOutcome> ExerciseApiFlowsAsync(
        string connectionString, Guid adminUserId)
    {
        var clock = new ManualClock(Now);
        var options = new RuntimeOptions
        {
            FirebaseProjectId = "isolated-v3-test",
            AuthCodeHashKey = CryptoKey,
            PinPepper = CryptoKey
        };
        await using var db = Open(connectionString);
        var delivery = new CapturingDelivery();
        var signer = new CapturingSigner();
        var issuer = new FirebaseAdminCustomTokenIssuer(db, signer, options, clock);
        var email = new EmailAuthService(db, delivery, issuer, options, clock);

        await email.StartAsync("restricted@example.test", null,
            EmailCodePurpose.Signup, default);
        await email.VerifyAsync("restricted@example.test", EmailCodePurpose.Signup,
            delivery.LatestCode, default);
        var userId = Guid.ParseExact(Assert.Single(signer.Uids), "N");
        var binding = await db.IdentityBindings.SingleAsync(x => x.UserId == userId);

        var enrollments = new RoleEnrollmentService(db, clock);
        var terms = await db.LegalDocumentVersions.SingleAsync(x => x.Type == LegalDocumentType.TermsOfService);
        var privacy = await db.LegalDocumentVersions.SingleAsync(x => x.Type == LegalDocumentType.PrivacyPolicy);
        var accountLegal = new AccountLegalConfirmation(new(terms.Id, terms.ContentHash, true),
            new(privacy.Id, privacy.ContentHash, true));
        var customer = await enrollments.SubmitAsync(new Actor(userId, ActorRole.Customer),
            new RoleEnrollmentRequest(ActorRole.Customer, "Restricted Customer", "CU-RUNTIME",
                null, null, null, AccountLegal: accountLegal), "customer-enrollment", default);
        Assert.Equal(RoleEnrollmentStatus.Approved, customer.Status);
        var customerPermission = await db.CommercePermissions.SingleAsync(x =>
            x.UserId == userId && x.Role == ActorRole.Customer);
        var customerActor = TrustedIdentityService.ActorFrom(customerPermission);

        var creator = await enrollments.SubmitAsync(customerActor,
            new RoleEnrollmentRequest(ActorRole.Creator, "Restricted Creator", "CR-RUNTIME",
                "Addis", "Food", "Restricted-role rehearsal"), "creator-enrollment", default);
        var business = await enrollments.SubmitAsync(customerActor,
            new RoleEnrollmentRequest(ActorRole.Business, "Restricted Business", "BU-RUNTIME",
                "Addis", "Restaurant", "Restricted-role rehearsal"), "business-enrollment", default);
        var admin = new Actor(adminUserId, ActorRole.PlatformAdmin);
        await enrollments.ReviewAsync(admin, creator.Id, true, null, creator.Version,
            "creator-review", default);
        await enrollments.ReviewAsync(admin, business.Id, true, null, business.Version,
            "business-review", default);

        var permissions = await db.CommercePermissions.Where(x => x.UserId == userId)
            .OrderBy(x => x.Role).ToListAsync();
        Assert.Equal(3, permissions.Count);
        var creatorPermission = permissions.Single(x => x.Role == ActorRole.Creator);
        var businessPermission = permissions.Single(x => x.Role == ActorRole.Business);
        var identity = new TrustedIdentityService(db,
            new StaticIdentityVerifier(binding.ExternalSubject), new PersistentWorkspaceDirectory(db));
        var selectedCreator = await identity.SignInAsync("test-token",
            new ProfileSelection(ActorRole.Creator, creatorPermission.SubjectId, null), default);
        Assert.Equal(3, selectedCreator.Profiles.Count);
        var selectedBusiness = await identity.SelectAsync(userId, binding.Id, binding.Version,
            new ProfileSelection(ActorRole.Business, businessPermission.SubjectId,
                businessPermission.BusinessId), Now, Now.AddHours(1), default);
        Assert.Equal(ActorRole.Business, selectedBusiness.Actor.Role);

        var legal = new LegalWorkspaceService(db, clock);
        foreach (var actor in new[] { selectedCreator.Actor, selectedBusiness.Actor })
        {
            foreach (var document in await legal.CurrentAsync(actor, default))
                await legal.AcceptAsync(actor, document.Id, document.ContentHash, true, default);
        }

        var deviceEnrollment = await new DeviceEnrollmentService(db, options, clock).EnrollAsync(
            userId, "01234", "01234", null, "device-enrollment", default);
        Assert.Equal(DeviceEnrollmentStates.Enrolled, deviceEnrollment.Status.State);
        var deviceCredential = Assert.IsType<OpaqueDeviceCredential>(deviceEnrollment.Credential);
        var device = await db.AuthorizedDevices.SingleAsync(x => x.UserId == userId
            && x.RevokedAtUtc == null);
        var sessionIdentity = new DeviceSessionIdentity(userId, binding.Id, binding.Version);
        var established = await new DeviceSessionService(db, clock)
            .EstablishAsync(sessionIdentity, device.Id, default);

        clock.Set(Now.AddMinutes(20));
        var access = new DeviceAccessService(db, options, clock);
        var locked = await access.EnforceAsync(sessionIdentity, deviceCredential.Value,
            established.Credential.Value, true, default);
        Assert.Equal(DeviceAccessStates.Locked, locked.State);
        var unlocked = await access.UnlockAsync(sessionIdentity, deviceCredential.Value,
            established.Credential.Value, "01234", "pin-unlock", default);
        Assert.True(unlocked.Succeeded);

        clock.Set(Now.AddMinutes(21));
        await email.StartAsync("restricted@example.test", null, EmailCodePurpose.PinRecovery, default);
        var recovery = await new DevicePinRecoveryService(db, options, clock).CompleteAsync(
            new DevicePinRecoveryRequest(sessionIdentity, deviceCredential.Value,
                "restricted@example.test", delivery.LatestCode, "56789", "56789", "pin-recovery"));
        Assert.Equal(userId, recovery.Session.UserId);

        var notices = new NotificationService(db, clock);
        var page = await notices.GetAsync(customerActor, default);
        var notice = Assert.Single(page.Items);
        await notices.ReadAsync(customerActor, notice.Id, default);
        Assert.Equal(0, (await notices.GetAsync(customerActor, default)).UnreadCount);

        var offers = await new WorkspaceQueries(db, new PersistentWorkspaceDirectory(db), clock)
            .OffersAsync(customerActor, default);
        Assert.Empty(offers);

        Assert.Equal(8, await db.Database.SqlQueryRaw<string>(
            "SELECT \"MigrationId\" AS \"Value\" FROM public.\"__EFMigrationsHistory\"")
            .CountAsync());
        return new(userId, creatorPermission.SubjectId, recovery.AuthorizedDeviceId,
            recovery.Session.Id, permissions.Select(x => x.Role).ToArray());
    }

    private static async Task<WorkerSeed> SeedWorkerFlowAsync(
        TestDatabase database, Guid creatorId, Guid creatorUserId)
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var businessId = Guid.NewGuid();
        var configurationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var wallet = new BusinessWallet(businessId);
        var configuration = new FinancialConfiguration(configurationId, "PlatformPricing");
        var price = Scenario.Price(PromotionType.ViewPlusCommission, versionId, effective: now.AddDays(-3));
        var version = new FinancialConfigurationVersion(versionId, configurationId, 1,
            Guid.NewGuid(), now.AddDays(-3), Scenario.Price(PromotionType.ViewOnly, versionId,
                effective: now.AddDays(-3)), price, new Money(2500), new Money(500));
        var promotion = new Promotion(businessId, "Worker grant campaign", "Rehearsal",
            PromotionType.ViewPlusCommission, new Money(6000), new(null, null, "ET", "Content"),
            now.AddDays(-1), now.AddDays(5), price, now.AddDays(-2));
        db.AddRange(wallet, configuration, version, promotion);
        await db.SaveChangesAsync();

        var business = new Actor(Guid.NewGuid(), ActorRole.Business, businessId);
        var financial = new FinancialCommands(db);
        await financial.CreditDepositAsync(new(business, new Money(10000),
            "restricted-worker-deposit", now.AddDays(-2)), 0);
        await financial.FundPromotionAsync(new(business, promotion.Id, 0, 1,
            "restricted-worker-fund", now.AddDays(-2)));
        promotion = await db.Promotions.Include(x => x.Allocations)
            .SingleAsync(x => x.Id == promotion.Id);
        promotion.Publish(now.AddDays(-2), Guid.NewGuid());
        var application = new CreatorApplication(promotion.Id, creatorId, "Join", null,
            promotion, now.AddDays(-2), Guid.NewGuid());
        application.Approve(business.UserId, now.AddDays(-2));
        db.CreatorApplications.Add(application);
        await db.SaveChangesAsync();
        var allocationId = await financial.AssignCreatorAllocationAsync(
            new(business, promotion.Id, creatorId, new Money(1000), promotion.Version,
                now.AddDays(-2)), "restricted-worker-allocation");
        promotion = await db.Promotions.Include(x => x.Allocations)
            .SingleAsync(x => x.Id == promotion.Id);
        promotion.Activate(now.AddDays(-1), Guid.NewGuid());
        db.OfferQrSessions.Add(new OfferQrSession(Guid.NewGuid(), promotion.Id, creatorId,
            allocationId, businessId, new string('A', 64), now.AddHours(-1),
            "restricted-worker-qr"));
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "CreatorPayoutEligible",
            Payload = JsonSerializer.Serialize(new { BeneficiaryId = creatorId }),
            OccurredAtUtc = now.AddMinutes(-2)
        });
        db.InAppNotifications.Add(new InAppNotification
        {
            UserId = creatorUserId, Role = ActorRole.Creator,
            SourceKey = "restricted-worker-push", EventType = "TestPush",
            Title = "Worker push", Message = "Safe rehearsal", Route = "/creator",
            CreatedAtUtc = now.AddMinutes(-1), PushState = PushDeliveryState.Pending,
            NextPushAtUtc = now.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        return new(promotion.Id, allocationId, creatorId, creatorUserId);
    }

    private static async Task ExerciseWorkerFlowsAsync(string connectionString, WorkerSeed seed)
    {
        await using var db = Open(connectionString);
        var pump = new WorkerPump(db, new RuntimeOptions
        {
            WorkerBatchSize = 100,
            RecipientBatchSize = 100,
            RetryLimit = 5
        }, new SuccessfulPush(), TimeProvider.System);
        await pump.RunOnceAsync(default);
        await pump.RunOnceAsync(default);
        Assert.NotNull((await db.WorkerCheckpoints.SingleAsync(x =>
            x.Name == "operational-worker")).LastSuccessAtUtc);
        Assert.Equal(OfferQrStatus.Expired,
            (await db.OfferQrSessions.SingleAsync(x =>
                x.CreatorAllocationId == seed.AllocationId)).Status);
        var pending = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.ProcessedAtUtc == null && x.FailedAtUtc == null)
            .Select(x => new { x.EventType, x.AttemptCount, x.FailureCount, x.LastError })
            .ToListAsync();
        Assert.True(pending.Count == 0, JsonSerializer.Serialize(pending));
        Assert.Equal(PushDeliveryState.Delivered,
            (await db.InAppNotifications.SingleAsync(x =>
                x.SourceKey == "restricted-worker-push")).PushState);
    }

    private static async Task AssertApiDenialsAsync(
        string connectionString,
        string workerConnectionString,
        string apiRole,
        string workerRole,
        string ownerRole)
    {
        var commands = new[]
        {
            "CREATE TABLE v3.api_forbidden(id integer)",
            "CREATE SCHEMA api_forbidden",
            "ALTER TABLE v3.\"AuthIdentifiers\" ADD COLUMN \"Forbidden\" integer",
            "DROP TABLE v3.\"AuthIdentifiers\"",
            "TRUNCATE TABLE v3.\"AuditEvents\"",
            "DELETE FROM v3.\"AuditEvents\"",
            "ALTER TABLE v3.\"AuthIdentifiers\" DISABLE TRIGGER ALL",
            "CREATE EXTENSION hstore",
            $"SET ROLE {QuoteIdentifier(workerRole)}",
            $"SET ROLE {QuoteIdentifier(ownerRole)}",
            "UPDATE v3.\"WorkerCheckpoints\" SET \"LastErrorCode\"='forbidden'",
            "UPDATE v3.\"InAppNotifications\" SET \"PushAttempts\"=1",
            "UPDATE v3.\"OutboxMessages\" SET \"AttemptCount\"=1",
            "INSERT INTO public.\"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('forbidden', '0')"
        };
        foreach (var command in commands)
            await AssertInsufficientPrivilegeAsync(connectionString, command,
                $"API role {apiRole} unexpectedly executed: {command}");

        await AssertIneffectiveGrantAsync(
            connectionString,
            $"GRANT SELECT ON TABLE v3.\"AuthIdentifiers\" TO {QuoteIdentifier(workerRole)}",
            workerConnectionString,
            "SELECT * FROM v3.\"AuthIdentifiers\"",
            $"API role {apiRole} unexpectedly changed grants for {workerRole}");
    }

    private static async Task AssertWorkerDenialsAsync(
        string connectionString, string apiRole, string workerRole, string ownerRole)
    {
        foreach (var table in new[]
        {
            "AuthIdentifiers", "EmailAuthChallenges", "PasswordCredentials", "IdentityBindings",
            "AuthorizedDevices", "DeviceSessions", "RoleEnrollments", "LegalAcceptances"
        })
            await AssertInsufficientPrivilegeAsync(connectionString,
                $"SELECT * FROM v3.{QuoteIdentifier(table)}",
                $"Worker role {workerRole} unexpectedly read {table}");

        var commands = new[]
        {
            "SELECT \"PinVerifier\" FROM v3.\"AuthorizedDevices\"",
            "UPDATE v3.\"AuthIdentifiers\" SET \"IsVerified\"=false",
            "CREATE TABLE v3.worker_forbidden(id integer)",
            "CREATE SCHEMA worker_forbidden",
            "ALTER TABLE v3.\"OutboxMessages\" ADD COLUMN \"Forbidden\" integer",
            "DROP TABLE v3.\"OutboxMessages\"",
            "TRUNCATE TABLE v3.\"OutboxMessages\"",
            "DELETE FROM v3.\"OutboxMessages\"",
            "ALTER TABLE v3.\"OutboxMessages\" DISABLE TRIGGER ALL",
            $"SET ROLE {QuoteIdentifier(apiRole)}",
            $"SET ROLE {QuoteIdentifier(ownerRole)}",
            "UPDATE v3.\"InAppNotifications\" SET \"ReadAtUtc\"=clock_timestamp()",
            "UPDATE v3.\"OfferQrSessions\" SET \"TokenHash\"=repeat('B',64)"
        };
        foreach (var command in commands)
            await AssertInsufficientPrivilegeAsync(connectionString, command,
                $"Worker role {workerRole} unexpectedly executed: {command}");

        await AssertIneffectiveGrantAsync(
            connectionString,
            $"GRANT SELECT ON TABLE v3.\"DeviceSessions\" TO {QuoteIdentifier(workerRole)}",
            connectionString,
            "SELECT * FROM v3.\"DeviceSessions\"",
            $"Worker role {workerRole} unexpectedly changed its own grants");
    }

    private static async Task AssertPersistedOutcomesAsync(
        TestDatabase database, ApiOutcome account, WorkerSeed worker)
    {
        await using var db = database.Open();
        Assert.Equal(3, await db.CommercePermissions.CountAsync(x =>
            x.UserId == account.CreatorUserId && x.IsActive));
        Assert.Equal(account.Roles.OrderBy(x => x), (await db.CommercePermissions
            .Where(x => x.UserId == account.CreatorUserId).Select(x => x.Role).ToListAsync())
            .OrderBy(x => x));
        Assert.Single(await db.AuthorizedDevices.Where(x =>
            x.UserId == account.CreatorUserId && x.RevokedAtUtc == null).ToListAsync());
        Assert.Single(await db.DeviceSessions.Where(x =>
            x.UserId == account.CreatorUserId && x.RevokedAtUtc == null).ToListAsync());
        Assert.True(await db.IdempotencyRecords.AnyAsync(x =>
            x.ActorId == account.CreatorUserId));
        Assert.True(await db.AuditEvents.AnyAsync(x =>
            x.ActorId == account.CreatorUserId));
        Assert.True(await db.LegalAcceptances.AnyAsync(x =>
            x.UserId == account.CreatorUserId));
        Assert.Equal(2, await db.LegalAcceptances.CountAsync(x =>
            x.UserId == account.CreatorUserId && x.Role == LegalRole.Account));
        Assert.True(await db.InAppNotifications.AnyAsync(x =>
            x.UserId == worker.CreatorUserId));
    }

    private static async Task SeedLegalDocumentsAsync(TestDatabase database)
    {
        await using var db = database.Open();
        foreach (var type in new[]
        {
            LegalDocumentType.TermsOfService,
            LegalDocumentType.PrivacyPolicy,
            LegalDocumentType.CreatorAgreement,
            LegalDocumentType.BusinessAgreement,
            LegalDocumentType.AntiCircumventionAgreement
        })
            db.LegalDocumentVersions.Add(new LegalDocumentVersion(Guid.NewGuid(), type,
                "restricted-role-v1", $"hash-{type}", Now.AddDays(-1)));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedPlatformAdminAsync(TestDatabase database)
    {
        await using var db = database.Open();
        var adminUserId = Guid.NewGuid();
        db.IdentityBindings.Add(new IdentityBinding
        {
            Provider = "Firebase",
            ProjectId = "weymela-pilot",
            ExternalSubject = $"restricted-admin-{adminUserId:N}",
            UserId = adminUserId,
            IsActive = true,
            ValidAfterUtc = Now.AddMinutes(-1),
            Version = 1
        });
        await db.SaveChangesAsync();
        var result = await new PlatformAdminBootstrapper(db).ProvisionAsync(
            new PlatformAdminBootstrapRequest(
                "weymela-pilot",
                $"restricted-admin-{adminUserId:N}",
                adminUserId,
                Now.AddMinutes(-1),
                Guid.NewGuid(),
                "restricted-role-rehearsal",
                Guid.NewGuid(),
                "restricted-role-admin-bootstrap"));
        Assert.Equal(adminUserId, result.UserId);
        return adminUserId;
    }

    private static async Task AssertMigrationOrderAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var actual = new List<string>();
        while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
        Assert.Equal(new[]
        {
            "20260911225904_InitialV3Schema",
            "20260911233032_AddViewRewardsQrAndPayouts",
            "20260912011149_AddOperationalSecurityAndNotifications",
            "20260913045523_AddAuthenticationRecovery",
            "20260913054814_AddRoleEnrollments",
            "20260913062900_AddPhoneLoginAliases",
            "20260914022116_AddDevicePinSessionFoundation",
            "20260916042557_AddPasswordCredentials"
        }, actual);
        await reader.CloseAsync();
        command.CommandText = "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname IN ('public','v3') AND c.relkind='S'";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static void AssertGrantScriptContract(System.IO.FileInfo path)
    {
        var sql = File.ReadAllText(path.FullName);
        Assert.StartsWith("\\set ON_ERROR_STOP on", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALL TABLES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER DEFAULT PRIVILEGES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE ROLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(sql, @"\bPASSWORD\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        Assert.DoesNotContain("GRANT DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN;", sql, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", sql, StringComparison.Ordinal);
    }

    private static void AssertOperationalScriptContract(System.IO.FileInfo path)
    {
        var sql = File.ReadAllText(path.FullName);
        Assert.StartsWith("\\set ON_ERROR_STOP on", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALL TABLES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE ROLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(sql, @"\bPASSWORD\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        Assert.DoesNotContain("GRANT DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN;", sql, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", sql, StringComparison.Ordinal);
    }

    private static (string Name, string Value)[] VerifyVariables(
        string api, string worker, string migrator, string backup, string database) =>
    [
        ("api_role", api), ("worker_role", worker), ("migrator_role", migrator),
        ("backup_role", backup), ("database_name", database)
    ];

    private static async Task AssignMigrationOwnershipAsync(string connectionString, string migrator)
    {
        // The fixture migrates as its disposable owner. Model the Pilot ownership split
        // before granting privileges, including the EF history table in public.
        await ExecuteOwnerAsync(connectionString, $"GRANT USAGE, CREATE ON SCHEMA public, v3 TO {QuoteIdentifier(migrator)}");
        await ExecuteOwnerAsync(connectionString, $"""
            DO $ownership$
            DECLARE item record;
            BEGIN
                FOR item IN
                    SELECT n.nspname, c.relname, c.relkind
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE (n.nspname = 'v3' OR
                           (n.nspname = 'public' AND c.relname = '__EFMigrationsHistory'))
                      AND c.relkind IN ('r', 'p', 'S', 'v', 'm')
                LOOP
                    EXECUTE format('ALTER %s %I.%I OWNER TO %I',
                        CASE item.relkind WHEN 'S' THEN 'SEQUENCE'
                            WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW'
                            ELSE 'TABLE' END,
                        item.nspname, item.relname, {QuoteLiteral(migrator)});
                END LOOP;
                FOR item IN
                    SELECT p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = 'v3'
                LOOP
                    EXECUTE format('ALTER FUNCTION %s OWNER TO %I',
                        item.oid::regprocedure, {QuoteLiteral(migrator)});
                END LOOP;
                EXECUTE format('ALTER SCHEMA v3 OWNER TO %I', {QuoteLiteral(migrator)});
            END $ownership$;
            """);
    }

    private async Task ExerciseOperationalRolesAsync(
        TestDatabase database, string databaseName, string suffix,
        string migratorConnection, string backupConnection, string apiConnection,
        string apiRole, string workerRole, string migratorRole, string backupRole,
        FileInfo verifyScript)
    {
        var ownerConnection = database.ConnectionString;
        var probeTable = $"ops_future_{suffix}";
        var probeSequence = $"ops_future_seq_{suffix}";
        await using (var connection = new NpgsqlConnection(migratorConnection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_user";
            Assert.Equal(migratorRole, (string)(await command.ExecuteScalarAsync())!);
            command.CommandText = $"CREATE TABLE v3.{QuoteIdentifier(probeTable)} (\"Id\" integer NOT NULL)";
            await command.ExecuteNonQueryAsync();
            command.CommandText = $"ALTER TABLE v3.{QuoteIdentifier(probeTable)} ADD COLUMN \"Value\" text";
            await command.ExecuteNonQueryAsync();
            command.CommandText = $"CREATE SEQUENCE v3.{QuoteIdentifier(probeSequence)}";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT count(*) FROM public.\"__EFMigrationsHistory\"";
            Assert.Equal(8L, (long)(await command.ExecuteScalarAsync())!);
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO public.\"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('disposable_migration_probe', '10.0.0')";
                await command.ExecuteNonQueryAsync();
                command.CommandText = "DELETE FROM public.\"__EFMigrationsHistory\" WHERE \"MigrationId\" = 'disposable_migration_probe'";
                await command.ExecuteNonQueryAsync();
                await transaction.RollbackAsync();
            }
        }

        await using (var connection = new NpgsqlConnection(backupConnection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_user";
            Assert.Equal(backupRole, (string)(await command.ExecuteScalarAsync())!);
            command.CommandText = "SELECT count(*) FROM v3.\"AuthorizedDevices\"";
            Assert.True((long)(await command.ExecuteScalarAsync())! >= 0);
            command.CommandText = $"SELECT count(*) FROM v3.{QuoteIdentifier(probeTable)}";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        await using (var connection = new NpgsqlConnection(ownerConnection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT has_sequence_privilege({QuoteLiteral(backupRole)},
                           {QuoteLiteral($"v3.{QuoteIdentifier(probeSequence)}")}, 'SELECT'),
                       has_sequence_privilege({QuoteLiteral(backupRole)},
                           {QuoteLiteral($"v3.{QuoteIdentifier(probeSequence)}")}, 'USAGE'),
                       has_table_privilege({QuoteLiteral(apiRole)},
                           {QuoteLiteral($"v3.{QuoteIdentifier(probeTable)}")}, 'SELECT'),
                       has_table_privilege({QuoteLiteral(workerRole)},
                           {QuoteLiteral($"v3.{QuoteIdentifier(probeTable)}")}, 'SELECT')
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.False(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
        }

        await AssertInsufficientPrivilegeAsync(migratorConnection,
            "CREATE ROLE forbidden_ops_role", "Migrator must not create roles");
        await AssertInsufficientPrivilegeAsync(migratorConnection,
            "CREATE DATABASE forbidden_ops_database", "Migrator must not create databases");
        await AssertInsufficientPrivilegeAsync(migratorConnection,
            $"SET ROLE {QuoteIdentifier(apiRole)}", "Migrator must not assume API role");
        await AssertInsufficientPrivilegeAsync(migratorConnection,
            $"SET ROLE {QuoteIdentifier(workerRole)}", "Migrator must not assume Worker role");
        await AssertInsufficientPrivilegeAsync(migratorConnection,
            $"SET ROLE {QuoteIdentifier(new NpgsqlConnectionStringBuilder(ownerConnection).Username!)}",
            "Migrator must not assume bootstrap role");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            "INSERT INTO v3.\"AuthorizedDevices\" (\"Id\") VALUES (gen_random_uuid())",
            "Backup must not insert auth state");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            "UPDATE v3.\"AuthorizedDevices\" SET \"Version\" = \"Version\"",
            "Backup must not update auth state");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            "DELETE FROM v3.\"AuthorizedDevices\" WHERE false",
            "Backup must not delete auth state");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            "TRUNCATE v3.\"AuthorizedDevices\"",
            "Backup must not truncate auth state");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            $"ALTER TABLE v3.{QuoteIdentifier(probeTable)} ADD COLUMN \"Forbidden\" integer",
            "Backup must not alter schema");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            "CREATE TABLE v3.forbidden_backup_table (id integer)",
            "Backup must not create schema objects");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            $"DROP TABLE v3.{QuoteIdentifier(probeTable)}",
            "Backup must not drop schema objects");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            $"SET ROLE {QuoteIdentifier(migratorRole)}", "Backup must not assume migrator role");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            $"SET ROLE {QuoteIdentifier(apiRole)}", "Backup must not assume API role");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            $"SET ROLE {QuoteIdentifier(workerRole)}", "Backup must not assume Worker role");
        await AssertInsufficientPrivilegeAsync(backupConnection,
            $"SET ROLE {QuoteIdentifier(new NpgsqlConnectionStringBuilder(ownerConnection).Username!)}",
            "Backup must not assume bootstrap role");
        await AssertIneffectiveGrantAsync(backupConnection,
            $"GRANT SELECT ON v3.{QuoteIdentifier(probeTable)} TO {QuoteIdentifier(apiRole)}",
            apiConnection,
            $"SELECT count(*) FROM v3.{QuoteIdentifier(probeTable)}",
            "Backup must not create effective grants");

        await ExecuteScriptAsync(ownerConnection, verifyScript,
            VerifyVariables(apiRole, workerRole, migratorRole, backupRole, databaseName));
        var archive = $"/tmp/v3-ops-{suffix}.dump";
        var dump = await fixture.ExecuteInContainerAsync(
            "pg_dump", "-U", backupRole, "-d", databaseName, "-Fc", "--no-owner",
            "--no-acl", "-f", archive);
        Assert.True(dump.ExitCode == 0, "Backup-role pg_dump failed without disclosing archive data.");
        var listing = await fixture.ExecuteInContainerAsync("pg_restore", "--list", archive);
        Assert.Equal(0, listing.ExitCode);
        foreach (var table in new[] { "AuthIdentifiers", "EmailAuthChallenges", "PasswordCredentials", "AuthorizedDevices",
                     "DeviceSessions", "LegalAcceptances", "FinancialJournals" })
            Assert.Contains($"TABLE DATA v3 {table}", listing.Stdout, StringComparison.Ordinal);
        var decoded = await fixture.ExecuteInContainerAsync("pg_restore", "--file=/dev/null", archive);
        Assert.Equal(0, decoded.ExitCode);
    }

    private static async Task CreateRuntimeRoleAsync(
        string connectionString, string role, string password)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            CREATE ROLE {QuoteIdentifier(role)} LOGIN PASSWORD {QuoteLiteral(password)}
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedUnsafeRuntimePrivilegesAsync(
        string connectionString, string apiRole, string workerRole)
    {
        await ExecuteOwnerAsync(connectionString, $"""
            GRANT UPDATE ("PushAttempts") ON TABLE v3."InAppNotifications" TO {QuoteIdentifier(apiRole)};
            GRANT SELECT ("PinVerifier"), REFERENCES ("Id")
                ON TABLE v3."AuthorizedDevices" TO {QuoteIdentifier(workerRole)};
            GRANT INSERT ("IdentifierHash")
                ON TABLE v3."AuthIdentifiers" TO {QuoteIdentifier(workerRole)};
            """);
    }

    private static async Task ExecuteScriptAsync(
        string connectionString, System.IO.FileInfo path,
        params (string Name, string Value)[] variables)
    {
        var sql = await File.ReadAllTextAsync(path.FullName);
        sql = string.Join('\n', sql.Split('\n').Where(line =>
            !line.TrimStart().StartsWith("\\set ", StringComparison.Ordinal)));
        foreach (var variable in variables)
        {
            sql = sql.Replace($":\"{variable.Name}\"", QuoteIdentifier(variable.Value),
                StringComparison.Ordinal);
            sql = sql.Replace($":'{variable.Name}'", QuoteLiteral(variable.Value),
                StringComparison.Ordinal);
            Assert.DoesNotContain($":\"{variable.Name}\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain($":'{variable.Name}'", sql, StringComparison.Ordinal);
        }
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteOwnerAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertInsufficientPrivilegeAsync(
        string connectionString, string sql, string because)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var exception = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync());
        Assert.True(exception is not null, because);
        var failure = Assert.IsType<PostgresException>(exception);
        Assert.True(failure.SqlState == PostgresErrorCodes.InsufficientPrivilege,
            because + $" ({failure.SqlState})");
    }

    private static async Task AssertIneffectiveGrantAsync(
        string granterConnectionString,
        string grantSql,
        string targetConnectionString,
        string privilegeProbeSql,
        string because)
    {
        await using (var connection = new NpgsqlConnection(granterConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(grantSql, connection);
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (PostgresException failure) when (
                failure.SqlState == PostgresErrorCodes.InsufficientPrivilege)
            {
                return;
            }
        }

        await AssertInsufficientPrivilegeAsync(targetConnectionString, privilegeProbeSql, because);
    }

    private static string RuntimeConnection(string ownerConnection, string role, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(ownerConnection)
        {
            Username = role,
            Password = password,
            Pooling = false,
            IncludeErrorDetail = false
        };
        return builder.ConnectionString;
    }

    private static WeymelaDbContext Open(string connectionString) => new(
        new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(connectionString).Options);

    private static System.IO.DirectoryInfo RepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Weymela.slnx")))
            directory = directory.Parent;
        return directory ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    private static string QuoteLiteral(string value) =>
        '\'' + value.Replace("'", "''", StringComparison.Ordinal) + '\'';

    private sealed class CapturingDelivery : IEmailCodeDelivery
    {
        public bool Enabled => true;
        public string LatestCode { get; private set; } = "";
        public Task SendAsync(string destination, string code, EmailCodePurpose purpose,
            CancellationToken ct)
        {
            Assert.Equal("restricted@example.test", destination);
            LatestCode = code;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSigner : IFirebaseAdminTokenSigner
    {
        public List<string> Uids { get; } = [];
        public Task<string> CreateCustomTokenAsync(string uid, CancellationToken ct)
        {
            Uids.Add(uid);
            return Task.FromResult("restricted-role-custom-token");
        }
    }

    private sealed class StaticIdentityVerifier(string uid) : IIdentityTokenVerifier
    {
        public Task<VerifiedIdentity> VerifyAsync(string sensitiveIdToken, CancellationToken ct) =>
            Task.FromResult(new VerifiedIdentity("Firebase", "isolated-v3-test", uid,
                Now, Now.AddHours(1)));
    }

    private sealed class SuccessfulPush : INotificationPushProvider
    {
        public bool Enabled => true;
        public Task<PushDeliveryResult> SendAsync(PushNotification notification,
            CancellationToken ct) => Task.FromResult(new PushDeliveryResult(true, false, ""));
    }

    private sealed class ManualClock(DateTime initial) : TimeProvider
    {
        private DateTime current = initial;
        public void Set(DateTime value) => current = value;
        public override DateTimeOffset GetUtcNow() => new(current);
    }

    private sealed record ApiOutcome(
        Guid CreatorUserId,
        Guid CreatorId,
        Guid ReplacementDeviceId,
        Guid ReplacementSessionId,
        ActorRole[] Roles);
    private sealed record WorkerSeed(
        Guid PromotionId,
        Guid AllocationId,
        Guid CreatorId,
        Guid CreatorUserId);
}
