using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Weymela.Application;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class CreatorPhotoHttpTests(PostgresFixture postgres)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
    internal static readonly byte[] Jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/4gHYSUNDX1BST0ZJTEUAAQEAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADb/2wBDAAoHBwgHBgoICAgLCgoLDhgQDg0NDh0VFhEYIx8lJCIfIiEmKzcvJik0KSEiMEExNDk7Pj4+JS5ESUM8SDc9Pjv/2wBDAQoLCw4NDhwQEBw7KCIoOzs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozv/wAARCAACAAIDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAwX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCaApmf/9k=");

    private async Task WithHost(Func<ApiFixture, string, Task> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "v3-creator-photo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            await using var host = await ApiFixture.CreateAsync(postgres, b => b.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["V3:CreatorPhotos:Directory"] = directory }));
            await using (var db = host.Database.Open())
            {
                db.PublicWorkspaceProfiles.AddRange(
                    new PublicWorkspaceProfile { SubjectId = DevelopmentDirectory.Id(300), Role = ActorRole.Creator,
                        DisplayName = "Bella", PublicId = "CR-PHOTO-BELLA" },
                    new PublicWorkspaceProfile { SubjectId = DevelopmentDirectory.Id(400), Role = ActorRole.Creator,
                        DisplayName = "Elias", PublicId = "CR-PHOTO-ELIAS" });
                await db.SaveChangesAsync();
            }
            await run(host, directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, string type = "image/png", string name = "../../unsafe.png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "photo", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/creator/photo") { Content = form };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Creator_can_add_replace_remove_and_business_access_is_relationship_scoped()
    {
        await WithHost(async (host, directory) =>
        {
            using var creator = await host.Login("creator");
            using var business = await host.Login("business");
            using var unrelated = await host.Login("other-business");
            var creatorId = DevelopmentDirectory.Id(300);
            Assert.Equal(HttpStatusCode.NotFound, (await creator.GetAsync("/api/creator/photo")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Upload(creator, Jpeg, "image/jpeg", "photo.jpg")).StatusCode);
            var first = Directory.GetFiles(directory).Single();
            Assert.Matches(@"^p_[0-9a-f]{64}$", Path.GetFileName(first));
            if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(first));
            var own = await creator.GetAsync("/api/creator/photo");
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            Assert.Equal("image/jpeg", own.Content.Headers.ContentType?.MediaType);
            Assert.True(own.Headers.CacheControl?.NoStore);
            Assert.Equal(Jpeg, await own.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.OK, (await business.GetAsync($"/api/business/creator-photos/{creatorId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await unrelated.GetAsync($"/api/business/creator-photos/{creatorId}")).StatusCode);
            var profile = await creator.GetJson("/api/profile");
            Assert.True(profile["hasCreatorPhoto"]!.GetValue<bool>());
            Assert.DoesNotContain("CreatorPhotoKey", profile.ToJsonString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(directory, profile.ToJsonString(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await Upload(creator, Png)).StatusCode);
            Assert.Single(Directory.GetFiles(directory));
            Assert.False(File.Exists(first));
            Assert.Equal("image/png", (await creator.GetAsync("/api/creator/photo")).Content.Headers.ContentType?.MediaType);
            Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync("/api/creator/photo")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync("/api/creator/photo")).StatusCode);
            Assert.Empty(Directory.GetFiles(directory));
            Assert.Equal(HttpStatusCode.NotFound, (await business.GetAsync($"/api/business/creator-photos/{creatorId}")).StatusCode);
            Assert.False((await creator.GetJson("/api/profile"))["hasCreatorPhoto"]!.GetValue<bool>());
        });
    }

    [Fact]
    public async Task Invalid_uploads_and_other_roles_cannot_change_creator_photo()
    {
        await WithHost(async (host, directory) =>
        {
            using var creator = await host.Login("creator");
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(creator, [1, 2, 3], "image/png")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(creator, Png, "image/jpeg")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(creator, new byte[4 * 1024 * 1024 + 1])).StatusCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(creator, new byte[4 * 1024 * 1024 + 65536])).StatusCode);
            Assert.Empty(Directory.GetFiles(directory));
            Assert.Equal(HttpStatusCode.OK, (await Upload(creator, Png)).StatusCode);
            var oldFile = Directory.GetFiles(directory).Single();
            Assert.Equal(HttpStatusCode.BadRequest, (await Upload(creator, [1, 2, 3])).StatusCode);
            Assert.True(File.Exists(oldFile));
            foreach (var alias in new[] { "customer", "business", "cashier", "admin", "operations-admin" })
            {
                using var actor = await host.Login(alias);
                Assert.Equal(HttpStatusCode.Forbidden, (await Upload(actor, Png)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await actor.DeleteAsync("/api/creator/photo")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/creator/photo")).StatusCode);
            }
            using var otherCreator = await host.Login("other-creator");
            Assert.Equal(HttpStatusCode.NotFound, (await otherCreator.GetAsync("/api/creator/photo")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Upload(otherCreator, Png)).StatusCode);
            await using var db = host.Database.Open();
            var firstKey = await db.PublicWorkspaceProfiles.Where(x => x.SubjectId == DevelopmentDirectory.Id(300)).Select(x => x.CreatorPhotoKey).SingleAsync();
            var secondKey = await db.PublicWorkspaceProfiles.Where(x => x.SubjectId == DevelopmentDirectory.Id(400)).Select(x => x.CreatorPhotoKey).SingleAsync();
            Assert.NotEqual(firstKey, secondKey);
            Assert.Equal(HttpStatusCode.NoContent, (await otherCreator.DeleteAsync("/api/creator/photo")).StatusCode);
            Assert.True(File.Exists(oldFile));
        });
    }

    [Fact]
    public async Task Failed_database_reference_update_keeps_old_photo_and_cleans_new_file()
    {
        await WithHost(async (host, directory) =>
        {
            using var creator = await host.Login("creator");
            Assert.Equal(HttpStatusCode.OK, (await Upload(creator, Png)).StatusCode);
            var first = Directory.GetFiles(directory).Single();
            await using (var db = host.Database.Open())
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION v3.reject_test_photo_update() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'fixture reference update failed'; END $$;
                    CREATE TRIGGER reject_test_photo_update BEFORE UPDATE OF "CreatorPhotoKey"
                    ON v3."PublicWorkspaceProfiles" FOR EACH ROW EXECUTE FUNCTION v3.reject_test_photo_update();
                    """);
            var failed = await Upload(creator, Png);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.DoesNotContain(directory, await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(first, Directory.GetFiles(directory).Single());
            Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync("/api/creator/photo")).StatusCode);
            await using (var db = host.Database.Open())
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER reject_test_photo_update ON v3."PublicWorkspaceProfiles";
                    DROP FUNCTION v3.reject_test_photo_update();
                    """);
            File.Delete(first);
            Assert.Equal(HttpStatusCode.NotFound, (await creator.GetAsync("/api/creator/photo")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await creator.DeleteAsync("/api/creator/photo")).StatusCode);
        });
    }
}
