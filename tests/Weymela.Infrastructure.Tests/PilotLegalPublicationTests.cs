using Npgsql;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class PilotLegalPublicationTests(PostgresFixture fixture)
{
    private const string Version = "pilot-draft-2026-09-16.1";

    [Fact]
    public async Task Publication_is_pilot_only_idempotent_and_does_not_create_acceptances()
    {
        var script = await File.ReadAllTextAsync(RepositoryFile("src", "Weymela.Api", "PilotLegal", "publish.sql"));
        script = string.Join('\n', script.Split('\n').Where(line => !line.StartsWith("\\set ", StringComparison.Ordinal)));

        var wrongTarget = await fixture.CreateAsync();
        await using (var wrong = new NpgsqlConnection(wrongTarget.ConnectionString))
        {
            await wrong.OpenAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(
                () => new NpgsqlCommand(script, wrong).ExecuteNonQueryAsync());
            Assert.Contains("target rejected", rejected.MessageText, StringComparison.Ordinal);
        }

        var pilot = await fixture.CreatePilotLegalPublicationAsync();
        await using var connection = new NpgsqlConnection(pilot.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(script, connection).ExecuteNonQueryAsync();
        await new NpgsqlCommand(script, connection).ExecuteNonQueryAsync();

        await using var rows = await new NpgsqlCommand("""
            SELECT "Type", "Version", "ContentHash"
            FROM v3."LegalDocumentVersions"
            ORDER BY "Type";
            """, connection).ExecuteReaderAsync();
        var documents = new List<(string Type, string Version, string Hash)>();
        while (await rows.ReadAsync())
            documents.Add((rows.GetString(0), rows.GetString(1), rows.GetString(2)));
        await rows.CloseAsync();

        Assert.Equal(2, documents.Count);
        Assert.Equal(new[] { "PrivacyPolicy", "TermsOfService" }, documents.Select(x => x.Type));
        Assert.All(documents, document =>
        {
            Assert.Equal(Version, document.Version);
            Assert.Matches("^sha256:[a-f0-9]{64}$", document.Hash);
        });
        Assert.Equal(0L, (long)(await new NpgsqlCommand(
            "SELECT count(*) FROM v3.\"LegalAcceptances\";", connection).ExecuteScalarAsync())!);
    }

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Weymela.slnx"))) continue;
            return Path.Combine([directory.FullName, .. segments]);
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
