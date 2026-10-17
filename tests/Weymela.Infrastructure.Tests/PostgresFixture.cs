using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DotNet.Testcontainers.Images;
using Testcontainers.PostgreSql;
using Weymela.Infrastructure.Persistence;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[CollectionDefinition("V3 PostgreSQL")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("mirror.gcr.io/library/postgres@sha256:aa90e97ee862e558111d34cfb8b2c4bec768c2b039fb791341686928560263b3")
        .WithImagePullPolicy(PullPolicy.Missing)
        .WithDatabase("v3_test_bootstrap")
        .WithUsername("v3_test").WithPassword(Guid.NewGuid().ToString("N"))
        .WithCreateParameterModifier(p =>
        {
            foreach (var binding in p.HostConfig.PortBindings.Values.SelectMany(x => x))
                binding.HostIP = "127.0.0.1";
        }).Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    public async Task<(long ExitCode, string Stdout, string Stderr)> ExecuteInContainerAsync(
        params string[] command)
    {
        var result = await postgres.ExecAsync(command);
        return (result.ExitCode, result.Stdout, result.Stderr);
    }

    public async Task<TestDatabase> CreateAsync(string? targetMigration = null)
    {
        // Every test uses a fresh database inside this disposable container.
        var name = "v3_test_" + Guid.NewGuid().ToString("N");
        return await CreateDatabaseAsync(name, targetMigration);
    }

    public Task<TestDatabase> CreatePilotLegalPublicationAsync()
        => CreateDatabaseAsync("weymela_v3_pilot");

    private async Task<TestDatabase> CreateDatabaseAsync(string name, string? targetMigration = null)
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await cmd.ExecuteNonQueryAsync();
        // A unique database means a unique connection pool. Disable pooling here so a large
        // suite does not retain one idle server connection per completed test database.
        // Runtime pooling is unaffected; concurrent contexts still use independent real connections.
        var cs = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
        var result = new TestDatabase(cs);
        await using var db = result.Open();
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(targetMigration);
        return result;
    }
}

public sealed record TestDatabase(string ConnectionString)
{
    public WeymelaDbContext Open(bool legacyUgc = false)
    {
        var options = new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(ConnectionString);
        if (legacyUgc) options.ReplaceService<IModelCustomizer, BeforeCreatorDeliveryModel>();
        return new(options.Options);
    }
}

// Historical migration fixtures explicitly omit columns that their old schema lacks.
public sealed class BeforeCreatorDeliveryModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder model, DbContext context)
    {
        base.Customize(model, context);
        model.Entity<Weymela.Infrastructure.Persistence.Records.FinancialConfigurationVersion>()
            .Ignore(x => x.LowPromotionBalanceAlert).Ignore(x => x.MonthlyPayoutDay).Ignore(x => x.MonthlyPayoutEnabled)
            .Ignore(x => x.MonthlyPayoutEffectiveFromUtc).Ignore(x => x.UgcPayoutMinimumExempt);
        model.Entity<Weymela.Domain.UgcOpportunity>().Ignore(x => x.DeliverableType).Ignore(x => x.PromotionLiveDurationDays);
        model.Entity<Weymela.Domain.CreatorPublicationVerification>().Ignore(x => x.ExpiresAtUtc).Ignore(x => x.LiveDurationDays).Ignore(x => x.GoLiveRequestedAtUtc);
    }
}
