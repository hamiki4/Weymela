using System.Net;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[Collection("V3 HTTP PostgreSQL")]
public sealed class UgcArrangementHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ugc_platform_capacity_audience_metadata_is_mapped_and_strictly_validated()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        using var business = await host.Login("business");
        var input = new
        {
            title = "Audience contract HTTP", slogan = (string?)null, contentType = "Video",
            instructions = "Make a short product video.", resources = Array.Empty<string>(), location = (string?)null,
            dueDateUtc = DateTime.UtcNow.AddDays(30), productProvided = true, creatorMustPurchase = false,
            usageRights = (string?)null, creatorPayment = 500m, creatorsNeeded = 2,
            platformRequirements = new[] {
                new { platform = "TikTok", format = "Social post", minimumAudience = (long?)50_000 },
                new { platform = "YouTube", format = "Social post", minimumAudience = (long?)0 },
            },
            platformCapacities = new[] {
                new { platform = "TikTok", capacity = 1, minimumAudience = (long?)50_000 },
                new { platform = "YouTube", capacity = 1, minimumAudience = (long?)0 },
            },
            customerOfferEnabled = false,
        };

        var id = (await business.PostJson("/api/business/ugc", input))!["id"]!.GetValue<Guid>();
        await using (var db = host.Database.Open())
        {
            var requirements = await db.UgcPlatformRequirements.Where(x => x.UgcOpportunityId == id).ToListAsync();
            Assert.Equal(50_000, requirements.Single(x => x.Platform == CreatorPlatform.TikTok).MinimumAudience);
            Assert.Null(requirements.Single(x => x.Platform == CreatorPlatform.YouTube).MinimumAudience);
            Assert.Equal(2, await db.UgcPlatformCapacities.CountAsync(x => x.UgcOpportunityId == id));
        }

        var nullAudience = await business.PostJson("/api/business/ugc", new
        {
            input.title, input.slogan, input.contentType, input.instructions, input.resources, input.location,
            input.dueDateUtc, input.productProvided, input.creatorMustPurchase, input.usageRights,
            input.creatorPayment, creatorsNeeded = 1,
            platformRequirements = new[] { new { platform = "TikTok", format = "Social post", minimumAudience = (long?)null } },
            platformCapacities = new[] { new { platform = "TikTok", capacity = 1, minimumAudience = (long?)null } },
            input.customerOfferEnabled,
        });
        Assert.NotEqual(Guid.Empty, nullAudience["id"]!.GetValue<Guid>());

        var negative = await business.Post("/api/business/ugc", new
        {
            input.title, input.slogan, input.contentType, input.instructions, input.resources, input.location,
            input.dueDateUtc, input.productProvided, input.creatorMustPurchase, input.usageRights,
            input.creatorPayment, creatorsNeeded = 1,
            platformRequirements = new[] { new { platform = "TikTok", format = "Social post", minimumAudience = (long?)-1 } },
            platformCapacities = new[] { new { platform = "TikTok", capacity = 1, minimumAudience = (long?)-1 } },
            input.customerOfferEnabled,
        });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var unknown = await business.Post("/api/business/ugc", new
        {
            input.title, input.slogan, input.contentType, input.instructions, input.resources, input.location,
            input.dueDateUtc, input.productProvided, input.creatorMustPurchase, input.usageRights,
            input.creatorPayment, creatorsNeeded = 1,
            platformRequirements = new[] { new { platform = "TikTok", format = "Social post", minimumAudience = (long?)null } },
            platformCapacities = new[] { new { platform = "TikTok", capacity = 1, minimumAudience = (long?)null } },
            input.customerOfferEnabled, unrelatedField = "must remain rejected",
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Ugc_publication_requires_arrangement_and_creator_discovers_selected_term()
    {
        await using var host = await ApiFixture.CreateAsync(postgres);
        await using (var db = host.Database.Open())
        {
            db.PublicWorkspaceProfiles.AddRange(
                new PublicWorkspaceProfile { SubjectId = DevelopmentDirectory.Id(100), Role = ActorRole.Business, DisplayName = "Abc Coffee", PublicId = "BUS-100" },
                new PublicWorkspaceProfile { SubjectId = DevelopmentDirectory.Id(300), Role = ActorRole.Creator, DisplayName = "Bella", PublicId = "CR-100" });
            await db.SaveChangesAsync();
        }
        using var business = await host.Login("business");
        using var creator = await host.Login("creator");
        var input = new
        {
            title = "Product arrangement HTTP", slogan = (string?)null, contentType = "Video",
            instructions = "Make a short product video.", resources = Array.Empty<string>(),
            location = (string?)null, dueDateUtc = DateTime.UtcNow.AddDays(30),
            productProvided = false, creatorMustPurchase = false, usageRights = (string?)null,
            creatorPayment = 500m, creatorsNeeded = 1, platformRequirements = Array.Empty<object>(),
            customerOfferEnabled = false
        };
        var missing = (await business.PostJson("/api/business/ugc", input))["id"]!.GetValue<Guid>();
        var before = (await business.GetJson("/api/business/wallet"))["available"]!.GetValue<decimal>();
        var rejected = await business.Post($"/api/business/ugc/{missing}/publish", new { version = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before, (await business.GetJson("/api/business/wallet"))["available"]!.GetValue<decimal>());
        await using (var db = host.Database.Open())
            Assert.False(await db.UgcReservations.AnyAsync(x => x.UgcOpportunityId == missing));

        var selected = (await business.PostJson("/api/business/ugc", new
        {
            input.title, input.slogan, input.contentType, input.instructions, input.resources, input.location, input.dueDateUtc,
            productProvided = false, creatorMustPurchase = true, input.usageRights, input.creatorPayment,
            input.creatorsNeeded, input.platformRequirements, input.customerOfferEnabled
        }))["id"]!.GetValue<Guid>();
        var businessCard = (await business.GetJson("/api/business/ugc")).AsArray().Single(x => x!["id"]!.GetValue<Guid>() == selected)!;
        Assert.True(businessCard["creatorMustPurchase"]!.GetValue<bool>());
        Assert.Equal(500m, businessCard["requiredFunding"]!.GetValue<decimal>());
        await business.PostJson($"/api/business/ugc/{selected}/publish", new { version = 0 });
        var creatorCard = (await creator.GetJson("/api/creator/ugc")).AsArray().Single(x => x!["id"]!.GetValue<Guid>() == selected)!;
        Assert.True(creatorCard["creatorMustPurchase"]!.GetValue<bool>());
        Assert.False(creatorCard["productProvided"]!.GetValue<bool>());
        Assert.Equal(450m, creatorCard["creatorPayment"]!.GetValue<decimal>());
        Assert.Equal(before - 500m, (await business.GetJson("/api/business/wallet"))["available"]!.GetValue<decimal>());
    }
}
