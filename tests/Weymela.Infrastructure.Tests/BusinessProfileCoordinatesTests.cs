using Microsoft.EntityFrameworkCore;
using Npgsql;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Web;
using Xunit;

namespace Weymela.Infrastructure.Tests;

[Collection("V3 PostgreSQL")]
public sealed class BusinessProfileCoordinatesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Coordinates_are_nullable_and_safe_customer_projection_comes_from_business_profile()
    {
        var database = await fixture.CreateAsync();
        var businessId = Guid.NewGuid();
        await using var db = database.Open();
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
        {
            SubjectId = businessId, Role = ActorRole.Business,
            DisplayName = "Public Business", PublicId = "BU-COORDS",
        });
        await db.SaveChangesAsync();
        var directory = new PersistentWorkspaceDirectory(db);
        var initiallyMissing = await directory.CustomerOfferBusinessAsync(businessId, default);
        Assert.Null(initiallyMissing.Latitude);
        Assert.Null(initiallyMissing.Longitude);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE v3."PublicWorkspaceProfiles" SET "Latitude"={9.03m}, "Longitude"={38.74m}
            WHERE "SubjectId"={businessId} AND "Role"='Business'
            """);
        db.ChangeTracker.Clear();
        var projection = await directory.CustomerOfferBusinessAsync(businessId, default);
        Assert.Equal("Public Business", projection.DisplayName);
        Assert.Equal(9.03m, projection.Latitude);
        Assert.Equal(38.74m, projection.Longitude);
        var json = System.Text.Json.JsonSerializer.Serialize(projection);
        Assert.DoesNotContain("BusinessId", json);
        Assert.DoesNotContain(businessId.ToString(), json);
    }

    [Fact]
    public async Task View_and_sale_customer_offer_carries_business_coordinates_without_business_id()
    {
        var scenario = await Phase4Scenario.Create(fixture);
        await using var db = scenario.Database.Open();
        var businessId = scenario.Seed.Business.BusinessId!.Value;
        db.PublicWorkspaceProfiles.AddRange(
            new PublicWorkspaceProfile
            {
                SubjectId = businessId, Role = ActorRole.Business,
                DisplayName = "Coordinate Business", PublicId = "BU-COORDINATE-OFFER",
                Latitude = 9.02m, Longitude = 38.75m,
            },
            new PublicWorkspaceProfile
            {
                SubjectId = scenario.Creator.CreatorId!.Value, Role = ActorRole.Creator,
                DisplayName = "Attributed Creator", PublicId = "CR-COORDINATE-OFFER",
            });
        await db.SaveChangesAsync();

        var offer = Assert.Single(await new WorkspaceQueries(db, new PersistentWorkspaceDirectory(db), scenario.Clock)
            .OffersAsync(scenario.Customer, default));

        Assert.Equal(9.02m, offer.Business.Latitude);
        Assert.Equal(38.75m, offer.Business.Longitude);
        Assert.DoesNotContain("businessId", System.Text.Json.JsonSerializer.Serialize(offer.Business));
        Assert.DoesNotContain(businessId.ToString(), System.Text.Json.JsonSerializer.Serialize(offer.Business));
    }

    [Theory]
    [MemberData(nameof(OutOfRangeCoordinates))]
    public async Task Database_rejects_coordinates_outside_valid_ranges(decimal latitude, decimal longitude)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var businessId = Guid.NewGuid();
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
        {
            SubjectId = businessId, Role = ActorRole.Business,
            DisplayName = "Invalid Coordinates", PublicId = "BU-INVALID",
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE v3."PublicWorkspaceProfiles" SET "Latitude"={latitude}, "Longitude"={longitude}
            WHERE "SubjectId"={businessId} AND "Role"='Business'
            """));
    }

    [Theory]
    [MemberData(nameof(IncompleteCoordinatePairs))]
    public async Task Database_rejects_half_of_a_coordinate_pair(decimal? latitude, decimal? longitude)
    {
        var database = await fixture.CreateAsync();
        await using var db = database.Open();
        var businessId = Guid.NewGuid();
        db.PublicWorkspaceProfiles.Add(new PublicWorkspaceProfile
        {
            SubjectId = businessId, Role = ActorRole.Business,
            DisplayName = "Incomplete Coordinates", PublicId = "BU-INCOMPLETE",
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE v3."PublicWorkspaceProfiles" SET "Latitude"={latitude}, "Longitude"={longitude}
            WHERE "SubjectId"={businessId} AND "Role"='Business'
            """));
    }

    public static IEnumerable<object[]> OutOfRangeCoordinates =>
    [
        [91m, 38m], [-91m, 38m], [0m, 181m], [0m, -181m],
    ];

    public static IEnumerable<object?[]> IncompleteCoordinatePairs =>
    [
        [9m, null], [null, 38m],
    ];
}
