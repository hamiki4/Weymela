using System.Security.Claims;
using Weymela.Api.Security;
using Weymela.Application;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Endpoints;

internal static class OnboardingEndpoints
{
    private sealed record ProfileRequest(string Role, string DisplayName, string PublicId, string? Region,
        string? Category, string? Submission, Guid? ProposedBusinessId = null);
    private sealed record ReviewRequest(bool Approve, string? Reason, long ExpectedVersion);

    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/api/onboarding").RequireAuthorization("VerifiedAccount")
            .AddEndpointFilter<ValidatedInputFilter>();
        account.MapGet("/status", async (HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
        {
            var userId = UserId(c);
            return Results.Ok(new { profiles = await service.MineAsync(userId, ct) });
        });
        account.MapPost("/profile", async (ProfileRequest input, HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
        {
            var userId = UserId(c);
            if (!Enum.TryParse<ActorRole>(input.Role, true, out var role))
                throw new ApplicationFailure(FailureKind.Validation, "Choose an available profile.");
            var actor = new Actor(userId, ActorRole.Customer, CustomerId: Guid.Empty);
            var result = await service.SubmitAsync(actor,
                new RoleEnrollmentRequest(role, input.DisplayName, input.PublicId, input.Region, input.Category, input.Submission, input.ProposedBusinessId),
                EndpointSupport.Key(c), ct);
            return Results.Ok(result);
        });

        var admin = app.MapGroup("/api/admin/role-enrollments").RequireAuthorization("PlatformAdmin")
            .AddEndpointFilter<ValidatedInputFilter>();
        admin.MapGet("", (RoleEnrollmentService service, CancellationToken ct) => service.PendingAsync(ct));
        admin.MapPost("/{id:guid}/review", async (Guid id, ReviewRequest input, HttpContext c, RoleEnrollmentService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewAsync(EndpointSupport.Actor(c), id, input.Approve, input.Reason, input.ExpectedVersion, EndpointSupport.Key(c), ct)));
    }

    private static Guid UserId(HttpContext c)
        => Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
}
