using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Operations;

public sealed record ReconciliationIssue(string Account, Guid? SubjectId, decimal Expected, decimal Actual);
public sealed class ReconciliationService(WeymelaDbContext db)
{
    // Read-only, server-aggregated and bounded. Does not repair/edit accounting or invent a second ledger.
    public async Task<IReadOnlyList<ReconciliationIssue>> CheckAsync(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.PlatformAdmin) throw new ApplicationFailure(FailureKind.Forbidden, "Admin reconciliation access is required.");
        return await db.Database.SqlQueryRaw<ReconciliationIssue>("""
            WITH entries AS (
              SELECT j."BusinessId", j."PromotionId", j."UgcOpportunityId", j."CreatorId", j."CustomerId", l."Account", l."Type", l."Amount"
              FROM v3."FinancialJournalLines" l JOIN v3."FinancialJournals" j ON j."Id"=l."JournalId"
            ), balances AS (
              SELECT 'BusinessAvailable' AS "Account", w."BusinessId" AS "SubjectId",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."BusinessId"=w."BusinessId" AND e."Account"='BusinessAvailable'),0) AS "Expected", w."AvailableBalance" AS "Actual" FROM v3."BusinessWallets" w
              UNION ALL SELECT 'BusinessReserved', w."BusinessId",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."BusinessId"=w."BusinessId" AND e."Account" IN ('CampaignUnallocatedReserve','CreatorAllocatedReserve','UgcAllocatedReserve')),0), w."ReservedBalance" FROM v3."BusinessWallets" w
              UNION ALL SELECT 'CampaignReserve', p."Id",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."PromotionId"=p."Id" AND e."Account" IN ('CampaignUnallocatedReserve','CreatorAllocatedReserve')),0), p."ReservedBudget" FROM v3."Promotions" p
              UNION ALL SELECT 'CreatorBudgetReserve', a."Id",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."PromotionId"=a."PromotionId" AND e."CreatorId"=a."CreatorId" AND e."Account"='CreatorAllocatedReserve'),0),
                CASE WHEN a."Status" IN ('Completed','Cancelled') THEN 0 ELSE a."OriginalAllocation"-a."UsedAmount" END FROM v3."CreatorAllocations" a
              UNION ALL SELECT 'UgcReserve', u."Id",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."UgcOpportunityId"=u."Id" AND e."Account"='UgcAllocatedReserve'),0), u."ReservedFunding" FROM v3."UgcOpportunities" u
              UNION ALL SELECT 'CreatorPayable', c."CreatorId",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."CreatorId"=c."CreatorId" AND e."Account"='CreatorPayable'),0), c."AvailableEarnings" FROM v3."CreatorEarningsAccounts" c
              UNION ALL SELECT 'CustomerCashbackPayable', c."CustomerId",
                coalesce((SELECT sum(CASE WHEN e."Type"='Credit' THEN e."Amount" ELSE -e."Amount" END) FROM entries e WHERE e."CustomerId"=c."CustomerId" AND e."Account"='CustomerCashbackPayable'),0), c."AvailableCashback" FROM v3."CustomerCashbackAccounts" c
              UNION ALL SELECT 'PlatformAccrued', NULL::uuid,
                coalesce((SELECT sum("Amount") FROM entries WHERE "Account"='PlatformRevenue' AND "Type"='Credit'),0), coalesce((SELECT sum("Amount") FROM v3."PlatformRevenueEntries"),0)
              UNION ALL SELECT 'PlatformSettled', NULL::uuid,
                coalesce((SELECT sum("Amount") FROM entries WHERE "Account"='PlatformRevenue' AND "Type"='Debit'),0), coalesce((SELECT sum("Amount") FROM v3."PlatformSettlements"),0)
            ) SELECT * FROM balances WHERE "Expected"<>"Actual" LIMIT 100
            """).ToListAsync(ct);
    }
}
