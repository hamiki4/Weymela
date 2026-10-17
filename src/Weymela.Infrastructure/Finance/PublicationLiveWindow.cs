using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Finance;

internal static class PublicationLiveWindow
{
    // The historical campaign cutoff still controls new submissions. An accepted,
    // exact publication has its own immutable duration starting at Go Live.
    internal static Task<bool> ContainsAsync(WeymelaDbContext db, CreatorPromotionParticipation participation,
        DateTime now, CancellationToken ct) => db.CreatorPublicationVerifications.AsNoTracking().AnyAsync(x =>
            x.CreatorAllocationId == participation.CreatorAllocationId && x.CreatorId == participation.CreatorId
            && x.CreatorSocialProfileId == participation.CreatorSocialProfileId
            && x.Provider == participation.Provider && x.ExternalContentId == participation.ExternalContentId
            && x.Status == PublicationVerificationStatus.Verified && x.WentLiveAtUtc == participation.WentLiveAtUtc
            && x.EndedAtUtc == null && x.LiveDurationDays != null && x.ExpiresAtUtc > now, ct);
}
