using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Weymela.Infrastructure.Persistence;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[CollectionDefinition("V3 PostgreSQL")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("v3_test_bootstrap")
        .WithUsername("v3_test").WithPassword(Guid.NewGuid().ToString("N"))
        .WithCreateParameterModifier(p =>
        {
            foreach (var binding in p.HostConfig.PortBindings.Values.SelectMany(x => x))
                binding.HostIP = "127.0.0.1";
        }).Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    public async Task<TestDatabase> CreateAsync()
    {
        // Every test uses a fresh database inside this disposable container.
        var name = "v3_test_" + Guid.NewGuid().ToString("N");
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
        await db.Database.MigrateAsync();
        return result;
    }
}

public sealed record TestDatabase(string ConnectionString)
{
    public WeymelaDbContext Open() => new(new DbContextOptionsBuilder<WeymelaDbContext>().UseNpgsql(ConnectionString).Options);
}
