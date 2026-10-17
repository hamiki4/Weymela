using Microsoft.EntityFrameworkCore;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Tests;

internal static class ConsolidatedConfiguration
{
    internal static async Task<FinancialConfigurationVersion> AddAsync(WeymelaDbContext db, DateTime now,
        int day, DateTime effective, bool enabled = true, bool ugcExempt = true)
    {
        var previous=await db.FinancialConfigurationVersions.AsNoTracking().OrderByDescending(x=>x.Version).FirstAsync();
        var version=FinancialConfigurationVersion.CreatePartial(Guid.NewGuid(),previous.ConfigurationId,previous.Version+1,
            Phase4Scenario.Admin.UserId,now,previous.ViewOnly,previous.ViewPlusCommission,previous.CreatorPayoutThreshold,
            previous.CustomerPayoutThreshold,previous.Ugc,previous.PromotionLiveDurationDays,previous.EnforceAudienceRequirements,
            previous.CreatorCommissionPercent,previous.CustomerCashbackPercent,previous.PlatformPercent);
        version.SetPayoutAndAlertControls(1000,day,effective,ugcExempt,enabled);
        db.FinancialConfigurationVersions.Add(version);await db.SaveChangesAsync();
        return version;
    }
}
