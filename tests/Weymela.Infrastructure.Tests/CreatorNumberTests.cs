using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Weymela.Application;
using Weymela.Infrastructure.Persistence.Records;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class CreatorNumberTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migration_backfills_existing_creators_in_public_id_order_and_preserves_long_ids()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928213157_AlignDepositReviewAuthority");
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var customer = Guid.NewGuid();
        await InsertLegacy(a, "CR-Z");
        await InsertLegacy(b, "CR-A");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."PublicWorkspaceProfiles" ("SubjectId","Role","DisplayName","PublicId","Region","Category","VerifiedFollowers","VerifiedViews","SocialVerified")
            VALUES ({customer},'Customer','Customer','CU-1','','',0,0,false)
            """);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var creators = await db.PublicWorkspaceProfiles.Where(x => x.Role == ActorRole.Creator)
            .OrderBy(x => x.PublicId).ToListAsync();
        Assert.Equal(new[] { "CR-A", "CR-Z" }, creators.Select(x => x.PublicId).ToArray());
        Assert.Equal(new long[] { 1000, 1001 }, creators.Select(x => x.CreatorNumber!.Value).ToArray());
        Assert.All(creators, x => Assert.Equal(x.PublicId == "CR-A" ? b : a, x.SubjectId));
        Assert.Null((await db.PublicWorkspaceProfiles.SingleAsync(x => x.SubjectId == customer)).CreatorNumber);
        Assert.Equal(2, await db.PublicWorkspaceProfiles.CountAsync(x => x.CreatorNumber != null));

        async Task InsertLegacy(Guid id, string publicId) => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."PublicWorkspaceProfiles" ("SubjectId","Role","DisplayName","PublicId","Region","Category","VerifiedFollowers","VerifiedViews","SocialVerified")
            VALUES ({id},'Creator','Creator',{publicId},'','',0,0,false)
            """);
    }

    [Fact]
    public async Task Database_assigns_distinct_numbers_for_concurrent_creators_and_blocks_edits()
    {
        var database = await fixture.CreateAsync();
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        await using var first = database.Open(); await using var second = database.Open();
        first.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = firstId, Role = ActorRole.Creator, DisplayName = "First", PublicId = "CR-FIRST" });
        second.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile { SubjectId = secondId, Role = ActorRole.Creator, DisplayName = "Second", PublicId = "CR-SECOND" });
        await Task.WhenAll(first.SaveChangesAsync(), second.SaveChangesAsync());
        await using var verify = database.Open();
        var rows = await verify.PublicWorkspaceProfiles.Where(x => x.Role == ActorRole.Creator).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.True(x.CreatorNumber >= 1000));
        Assert.Equal(2, rows.Select(x => x.CreatorNumber).Distinct().Count());
        Assert.Contains(rows, x => x.SubjectId == firstId && x.PublicId == "CR-FIRST");
        Assert.Contains(rows, x => x.SubjectId == secondId && x.PublicId == "CR-SECOND");
        await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE v3.\"PublicWorkspaceProfiles\" SET \"CreatorNumber\"=12345 WHERE \"SubjectId\"={firstId} AND \"Role\"='Creator'"));
        await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."PublicWorkspaceProfiles" ("SubjectId","Role","DisplayName","PublicId","Region","Category","VerifiedFollowers","VerifiedViews","SocialVerified","CreatorNumber")
            VALUES ({Guid.NewGuid()},'Creator','Spoof','CR-SPOOF','','',0,0,false,12345)
            """));
    }

    [Fact]
    public async Task Creator_number_can_grow_beyond_four_digits_and_is_not_reused()
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        await db.Database.ExecuteSqlRawAsync("SELECT setval('v3.creator_number_seq',9999,true)");
        var first = new PublicWorkspaceProfile { SubjectId = Guid.NewGuid(), Role = ActorRole.Creator,
            DisplayName = "Five digits", PublicId = "CR-FIVE-DIGITS" };
        db.PublicWorkspaceProfiles.Add(first);
        await db.SaveChangesAsync();
        Assert.Equal(10000L, first.CreatorNumber);
        db.PublicWorkspaceProfiles.Remove(first);
        await db.SaveChangesAsync();
        var next = new PublicWorkspaceProfile { SubjectId = Guid.NewGuid(), Role = ActorRole.Creator,
            DisplayName = "Next", PublicId = "CR-NEXT" };
        db.PublicWorkspaceProfiles.Add(next);
        await db.SaveChangesAsync();
        Assert.Equal(10001L, next.CreatorNumber);
    }
}
