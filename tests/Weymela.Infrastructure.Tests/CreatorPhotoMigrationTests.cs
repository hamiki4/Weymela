using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class CreatorPhotoMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migration_preserves_profiles_enforces_creator_only_key_and_refuses_lossy_rollback()
    {
        var database = await postgres.CreateAsync();
        await using var db = database.Open();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260929203557_AddUgcPlatformCapacities");
        var creator = Guid.NewGuid(); var business = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO v3."PublicWorkspaceProfiles" ("SubjectId","Role","DisplayName","PublicId","Region","Category","VerifiedFollowers","VerifiedViews","SocialVerified")
            VALUES ({creator},'Creator','Creator','CR-PHOTO-LEGACY','','',0,0,false),
                   ({business},'Business','Business','BU-PHOTO-LEGACY','','',0,0,false)
            """);
        await migrator.MigrateAsync();
        var profiles = await db.PublicWorkspaceProfiles.AsNoTracking().ToListAsync();
        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, profile => Assert.Null(profile.CreatorPhotoKey));
        var key = "p_" + new string('a', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE v3.\"PublicWorkspaceProfiles\" SET \"CreatorPhotoKey\"={key} WHERE \"SubjectId\"={creator}");
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE v3.\"PublicWorkspaceProfiles\" SET \"CreatorPhotoKey\"={key} WHERE \"SubjectId\"={business}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE v3.\"PublicWorkspaceProfiles\" SET \"CreatorPhotoKey\"={"../../unsafe"} WHERE \"SubjectId\"={creator}"));
        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync("20260929203557_AddUgcPlatformCapacities"));
        Assert.Equal(key, (await db.PublicWorkspaceProfiles.AsNoTracking().SingleAsync(x => x.SubjectId == creator)).CreatorPhotoKey);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE v3.\"PublicWorkspaceProfiles\" SET \"CreatorPhotoKey\"=NULL WHERE \"SubjectId\"={creator}");
        await migrator.MigrateAsync("20260929203557_AddUgcPlatformCapacities");
        await migrator.MigrateAsync();
        Assert.Equal(2, await db.PublicWorkspaceProfiles.CountAsync());
    }
}
