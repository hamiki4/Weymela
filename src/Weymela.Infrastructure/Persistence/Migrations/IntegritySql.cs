namespace Weymela.Infrastructure.Persistence.Migrations;

internal static class IntegritySql
{
    public const string Create = """
        ALTER TABLE v3."PricingSnapshots" ADD CONSTRAINT "FK_Snapshot_ConfigurationVersion"
          FOREIGN KEY ("ConfigurationVersionId") REFERENCES v3."FinancialConfigurationVersions" ("Id");
        ALTER TABLE v3."FinancialConfigurations" ADD CONSTRAINT "CK_PlatformPricingRoot" CHECK ("Name"='PlatformPricing');
        ALTER TABLE v3."FinancialConfigurationVersions" ADD CONSTRAINT "CK_Configuration_Valid" CHECK (
          "Version" > 0 AND "CreatorPayoutThreshold" > 0 AND "CustomerPayoutThreshold" > 0 AND
          "ViewOnly_ViewsPerReward" > 0 AND "ViewPlusCommission_ViewsPerReward" > 0 AND
          "ViewOnly_BusinessCharge" > 0 AND "ViewPlusCommission_BusinessCharge" > 0 AND
          "ViewOnly_CreatorEarning" >= 0 AND "ViewOnly_PlatformEarning" >= 0 AND
          "ViewPlusCommission_CreatorEarning" >= 0 AND "ViewPlusCommission_PlatformEarning" >= 0 AND
          "ViewOnly_BusinessCharge" = "ViewOnly_CreatorEarning" + "ViewOnly_PlatformEarning" AND
          "ViewPlusCommission_BusinessCharge" = "ViewPlusCommission_CreatorEarning" + "ViewPlusCommission_PlatformEarning" AND
          "ViewPlusCommission_CreatorCommissionPercent" >= 0 AND "ViewPlusCommission_CustomerCashbackPercent" >= 0 AND
          "ViewPlusCommission_PlatformPercent" >= 0 AND
          "ViewPlusCommission_CreatorCommissionPercent" + "ViewPlusCommission_CustomerCashbackPercent" + "ViewPlusCommission_PlatformPercent" <= 100 AND
          ("ViewOnly_MinimumPromotionBudget" IS NULL OR "ViewOnly_MinimumPromotionBudget" >= 0) AND
          ("ViewPlusCommission_MinimumPromotionBudget" IS NULL OR "ViewPlusCommission_MinimumPromotionBudget" >= 0));

        CREATE FUNCTION v3.reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Append-only history cannot be changed' USING ERRCODE = '23514'; END $$;
        DO $$
        DECLARE tab text;
        BEGIN
          FOREACH tab IN ARRAY ARRAY['FinancialJournals','FinancialJournalLines','WalletEntries',
            'PromotionReservations','PromotionBudgetEntries','PricingSnapshots','FinancialConfigurationVersions',
            'LegalDocumentVersions','LegalAcceptances','IdempotencyRecords','AuditEvents','CreatorEarningEntries',
            'CustomerCashbackEntries','PlatformRevenueEntries','PlatformSettlements','VerifiedSales','PromotionViewVerifications']
          LOOP
            EXECUTE format('CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3.%I FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change()', tab);
          END LOOP;
        END $$;

        CREATE FUNCTION v3.check_balanced_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE net numeric; count_lines integer;
        BEGIN
          SELECT sum(CASE WHEN "Type" = 'Debit' THEN "Amount" ELSE -"Amount" END), count(*)
            INTO net, count_lines FROM v3."FinancialJournalLines" WHERE "JournalId" = NEW."Id";
          IF count_lines < 2 OR net <> 0 THEN
            RAISE EXCEPTION 'Journal must have balanced debit and credit lines' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER balanced_journal AFTER INSERT ON v3."FinancialJournals"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_balanced_journal();

        CREATE FUNCTION v3.guard_journal_line_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM v3."FinancialJournals" WHERE "Id" = NEW."JournalId"
            AND pg_xact_status(xmin::text::xid8) = 'in progress') THEN
            RAISE EXCEPTION 'Lines may only be posted with their new journal' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER journal_line_insert BEFORE INSERT ON v3."FinancialJournalLines"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_journal_line_insert();

        CREATE FUNCTION v3.guard_verified_sale() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM v3."Promotions" p WHERE p."Id" = NEW."PromotionId"
            AND p."PromotionType" = 'ViewPlusCommission' AND p."BusinessId" = NEW."BusinessId") THEN
            RAISE EXCEPTION 'Verified sale requires the correct hybrid campaign' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER verified_sale_type BEFORE INSERT ON v3."VerifiedSales"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_verified_sale();

        CREATE FUNCTION v3.check_campaign_allocations() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE pid uuid; allocated numeric; spent numeric; declared_allocated numeric; budget numeric;
        BEGIN
          IF TG_TABLE_NAME = 'Promotions' THEN pid := NEW."Id"; ELSE pid := NEW."PromotionId"; END IF;
          SELECT coalesce(sum(CASE WHEN "Status" = 'Completed' THEN "UsedAmount"
            WHEN "Status" = 'Cancelled' THEN 0 ELSE "OriginalAllocation" END),0), coalesce(sum("UsedAmount"),0)
            INTO allocated, spent FROM v3."CreatorAllocations" WHERE "PromotionId" = pid;
          SELECT "TotalBudget", "AllocatedBudget" INTO budget, declared_allocated FROM v3."Promotions" WHERE "Id" = pid;
          IF allocated > budget OR allocated <> declared_allocated OR
              spent <> (SELECT "UsedBudget" FROM v3."Promotions" WHERE "Id" = pid) THEN
            RAISE EXCEPTION 'Campaign allocation projection must reconcile' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER campaign_allocations AFTER INSERT OR UPDATE ON v3."Promotions"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_campaign_allocations();
        CREATE CONSTRAINT TRIGGER allocation_projection AFTER INSERT OR UPDATE ON v3."CreatorAllocations"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_campaign_allocations();

        CREATE FUNCTION v3.guard_allocation_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."UsedAmount" < OLD."UsedAmount" OR
             (OLD."ActivatedAtUtc" IS NOT NULL AND NEW."OriginalAllocation" < OLD."OriginalAllocation") OR
             (OLD."Status" IN ('Completed','Cancelled') AND NEW IS DISTINCT FROM OLD) THEN
            RAISE EXCEPTION 'Earned or closed creator budgets cannot be reclaimed or reused' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER allocation_monotonic BEFORE UPDATE ON v3."CreatorAllocations"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_allocation_change();

        CREATE FUNCTION v3.check_wallet_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE bid uuid; available numeric; reserved numeric; actual_available numeric; actual_reserved numeric;
        BEGIN
          bid := NEW."BusinessId";
          IF bid IS NULL THEN RETURN NULL; END IF;
          SELECT coalesce(sum(CASE WHEN l."Account"='BusinessAvailable' THEN
            CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END ELSE 0 END),0),
            coalesce(sum(CASE WHEN l."Account" IN ('CampaignUnallocatedReserve','CreatorAllocatedReserve') THEN
            CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END ELSE 0 END),0)
          INTO available, reserved FROM v3."FinancialJournalLines" l JOIN v3."FinancialJournals" j ON j."Id"=l."JournalId"
          WHERE j."BusinessId"=bid;
          SELECT "AvailableBalance", "ReservedBalance" INTO actual_available, actual_reserved
            FROM v3."BusinessWallets" WHERE "BusinessId"=bid;
          IF actual_available IS NULL OR actual_available <> available OR actual_reserved <> reserved THEN
            RAISE EXCEPTION 'Business wallet must reconcile with authoritative journals' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER wallet_journal_projection AFTER INSERT OR UPDATE ON v3."BusinessWallets"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_wallet_journal();
        CREATE CONSTRAINT TRIGGER journal_wallet_projection AFTER INSERT ON v3."FinancialJournals"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_wallet_journal();

        CREATE FUNCTION v3.check_platform_settlement() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          PERFORM pg_advisory_xact_lock(73431001);
          IF (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformSettlements") >
             (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformRevenueEntries") THEN
            RAISE EXCEPTION 'Platform settlement cannot exceed accrued revenue' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER platform_settlement_bound AFTER INSERT ON v3."PlatformSettlements"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_platform_settlement();

        CREATE FUNCTION v3.guard_cashback_sale() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."Source"='VerifiedSale' AND NOT EXISTS (
            SELECT 1 FROM v3."VerifiedSales" WHERE "Id"=NEW."VerifiedSaleId"
              AND "CustomerId"=NEW."CustomerId" AND "CustomerCashbackAmount"=NEW."Amount") THEN
            RAISE EXCEPTION 'Cashback must match the eligible sale customer and amount' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER cashback_sale BEFORE INSERT ON v3."CustomerCashbackEntries"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_cashback_sale();
        CREATE UNIQUE INDEX "UX_Cashback_Sale" ON v3."CustomerCashbackEntries" ("VerifiedSaleId")
          WHERE "Source"='VerifiedSale';

        CREATE FUNCTION v3.check_platform_revenue_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE total numeric; source text;
        BEGIN
          SELECT "SourceType" INTO source FROM v3."FinancialJournals" WHERE "Id"=NEW."JournalId";
          SELECT coalesce(sum("Amount"),0) INTO total FROM v3."FinancialJournalLines"
            WHERE "JournalId"=NEW."JournalId" AND "Type"='Credit' AND "Account"='PlatformRevenue';
          IF total <> NEW."Amount" OR
             (NEW."Source"='ViewRewardPlatformShare' AND source <> 'ViewReward') OR
             (NEW."Source"='SalePlatformShare' AND source <> 'VerifiedSale') OR
             (NEW."Source"='AuthorizedAdjustment' AND source <> 'Adjustment') THEN
            RAISE EXCEPTION 'Platform revenue must reconcile with its authoritative journal' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER platform_revenue_journal AFTER INSERT ON v3."PlatformRevenueEntries"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_platform_revenue_journal();
        CREATE UNIQUE INDEX "UX_PlatformRevenue_Journal" ON v3."PlatformRevenueEntries" ("JournalId");
        """;
}
