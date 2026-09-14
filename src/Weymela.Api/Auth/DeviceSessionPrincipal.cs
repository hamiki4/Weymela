using System.Security.Claims;
using Weymela.Infrastructure.Identity;

namespace Weymela.Api.Auth;

internal static class DeviceSessionPrincipal
{
    public static bool TryGet(ClaimsPrincipal principal, out DeviceSessionIdentity identity)
    {
        identity = null!;
        if (principal.Identity?.IsAuthenticated != true
            || principal.FindFirst("auth-strength")?.Value != "firebase-verified"
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirst("identity-binding")?.Value, out var bindingId)
            || !long.TryParse(principal.FindFirst("identity-version")?.Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var bindingVersion)
            || userId == Guid.Empty || bindingId == Guid.Empty || bindingVersion < 0)
            return false;
        identity = new DeviceSessionIdentity(userId, bindingId, bindingVersion);
        return true;
    }
}
