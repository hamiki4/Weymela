namespace Weymela.Infrastructure.Persistence.Migrations;

// Additive integrity for UGC Customer Offers. View & Sale rules remain intact;
// this only adds a source-specific branch with no Creator allocation.
internal static class UgcCustomerOfferIntegritySql
{
    public const string Create = """
        ALTER TABLE v3."FinancialConfigurationVersions"
          ADD CONSTRAINT "CK_Configuration_Ugc" CHECK (
            "Ugc_ConfigurationVersionId" IS NULL OR (
              "Ugc_MinimumCreatorPayment" > 0 AND
              "Ugc_PlatformFeePercent" BETWEEN 0 AND 100 AND
              ("Ugc_MinimumUgcBudget" IS NULL OR "Ugc_MinimumUgcBudget" > 0) AND
              ("Ugc_CustomerOfferPlatformSalePercent" IS NULL OR "Ugc_CustomerOfferPlatformSalePercent" BETWEEN 0 AND 100)));
        ALTER TABLE v3."UgcOpportunities"
          ADD CONSTRAINT "FK_UgcOpportunity_ConfigurationVersion" FOREIGN KEY ("PricingSnapshot_ConfigurationVersionId")
          REFERENCES v3."FinancialConfigurationVersions" ("Id");
        ALTER TABLE v3."UgcOpportunities"
          ADD CONSTRAINT "CK_UgcOpportunity_CustomerOfferRate" CHECK (
            "PricingSnapshot_CustomerOfferPlatformSalePercent" IS NULL OR
            "PricingSnapshot_CustomerOfferPlatformSalePercent" BETWEEN 0 AND 100);
        ALTER TABLE v3."UgcCustomerOffers"
          ADD CONSTRAINT "FK_UgcCustomerOffer_ConfigurationVersion" FOREIGN KEY ("PricingSnapshot_ConfigurationVersionId")
          REFERENCES v3."FinancialConfigurationVersions" ("Id");
        ALTER TABLE v3."UgcCustomerOffers"
          ADD CONSTRAINT "CK_UgcCustomerOffer_PlatformPercent" CHECK (
            "PricingSnapshot_PlatformSalePercent" BETWEEN 0 AND 100);

        CREATE FUNCTION v3.guard_ugc_customer_offer() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM v3."UgcOpportunities" u
                         WHERE u."Id"=NEW."UgcOpportunityId" AND u."BusinessId"=NEW."BusinessId") THEN
            RAISE EXCEPTION 'UGC Customer Offer must match its Business and UGC opportunity' USING ERRCODE='23514';
          END IF;
          IF TG_OP='INSERT' THEN
            IF NEW."Status"<>'Draft' OR NEW."ReservedFunding"<>0 OR NEW."UsedFunding"<>0 OR NEW."PublishedAtUtc" IS NOT NULL THEN
              RAISE EXCEPTION 'UGC Customer Offer must begin as an unfunded draft' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF NEW."Id"<>OLD."Id" OR NEW."UgcOpportunityId"<>OLD."UgcOpportunityId" OR
             NEW."BusinessId"<>OLD."BusinessId" OR NEW."CreatedAtUtc"<>OLD."CreatedAtUtc" OR
             NEW."PricingSnapshot_PlatformSalePercent"<>OLD."PricingSnapshot_PlatformSalePercent" OR
             NEW."PricingSnapshot_EffectiveFromUtc"<>OLD."PricingSnapshot_EffectiveFromUtc" OR
             NEW."PricingSnapshot_ConfigurationVersionId"<>OLD."PricingSnapshot_ConfigurationVersionId" OR
             NEW."UsedFunding"<OLD."UsedFunding" THEN
            RAISE EXCEPTION 'UGC Customer Offer ownership, pricing and consumed funding are immutable' USING ERRCODE='23514';
          END IF;
          IF OLD."Status"='Draft' AND NEW."Status"='Draft' THEN
            IF NEW."ReservedFunding"<>0 OR NEW."UsedFunding"<>0 OR NEW."PublishedAtUtc" IS NOT NULL THEN
              RAISE EXCEPTION 'Draft UGC Customer Offer cannot hold reserved or used funding' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF OLD."Status"='Draft' AND NEW."Status"='Active' THEN
            IF NEW."ReservedFunding"<>NEW."FundedLimit" OR NEW."UsedFunding"<>0 OR NEW."PublishedAtUtc" IS NULL THEN
              RAISE EXCEPTION 'Published UGC Customer Offer must reserve its complete funded allocation' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF OLD."Status"='Draft' AND NEW."Status"='Cancelled' THEN RETURN NEW; END IF;
          IF OLD."Status"='Active' AND NEW."Status" IN ('Active','Exhausted','Cancelled') THEN
            IF NEW."FundedLimit"<>OLD."FundedLimit" OR NEW."CustomerDiscountPercent"<>OLD."CustomerDiscountPercent" OR
               NEW."CustomerFacingSlogan" IS DISTINCT FROM OLD."CustomerFacingSlogan" OR
               NEW."StartsAtUtc"<>OLD."StartsAtUtc" OR NEW."EndsAtUtc"<>OLD."EndsAtUtc" OR
               NEW."ReservedFunding">OLD."ReservedFunding" OR NEW."PublishedAtUtc"<>OLD."PublishedAtUtc" OR
               (NEW."Status"='Exhausted' AND (NEW."UsedFunding"<>NEW."FundedLimit" OR NEW."ReservedFunding"<>0)) OR
               (NEW."Status"='Cancelled' AND NEW."ReservedFunding"<>0) THEN
              RAISE EXCEPTION 'Active UGC Customer Offer funding and terms are immutable/monotonic' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          RAISE EXCEPTION 'Invalid UGC Customer Offer status transition' USING ERRCODE='23514';
        END $$;
        CREATE TRIGGER ugc_customer_offer_guard BEFORE INSERT OR UPDATE ON v3."UgcCustomerOffers"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_ugc_customer_offer();

        CREATE FUNCTION v3.guard_ugc_customer_offer_sale() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE offer record;
        BEGIN
          SELECT * INTO offer FROM v3."UgcCustomerOffers" WHERE "Id"=NEW."UgcCustomerOfferId";
          IF offer IS NULL OR offer."UgcOpportunityId"<>NEW."UgcOpportunityId" OR offer."BusinessId"<>NEW."BusinessId" OR
             NEW."CustomerDiscountAmount"<>round(NEW."PurchaseAmount"*offer."CustomerDiscountPercent"/100,2) OR
             NEW."PlatformRevenueAmount"<>round(NEW."PurchaseAmount"*offer."PricingSnapshot_PlatformSalePercent"/100,2) OR
             NOT EXISTS (SELECT 1 FROM v3."OfferQrSessions" q WHERE q."Id"::text=NEW."QrTokenReference" AND
               q."Source"='UgcCustomerOffer' AND q."Status"='Issued' AND q."UgcCustomerOfferId"=NEW."UgcCustomerOfferId" AND
               q."BusinessId"=NEW."BusinessId" AND q."CustomerId"=NEW."CustomerId") THEN
            RAISE EXCEPTION 'UGC Customer Offer Sale must match its immutable offer snapshot and issued QR' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER ugc_customer_offer_sale_guard BEFORE INSERT ON v3."UgcCustomerOfferSales"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_ugc_customer_offer_sale();

        CREATE FUNCTION v3.check_ugc_customer_offer_projection() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE ledger_reserved numeric; recorded_used numeric; budget_reserved numeric; budget_released numeric; budget_used numeric;
        BEGIN
          SELECT coalesce(sum(CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END),0)
            INTO ledger_reserved FROM v3."FinancialJournalLines" l JOIN v3."FinancialJournals" j ON j."Id"=l."JournalId"
            WHERE j."UgcCustomerOfferId"=NEW."Id" AND l."Account"='UgcCustomerOfferReserve';
          SELECT coalesce(sum("TotalOfferCharge"),0) INTO recorded_used FROM v3."UgcCustomerOfferSales"
            WHERE "UgcCustomerOfferId"=NEW."Id";
          SELECT coalesce(sum("Amount") FILTER (WHERE "Movement"='Reserved'),0),
                 coalesce(sum("Amount") FILTER (WHERE "Movement"='Released'),0),
                 coalesce(sum("Amount") FILTER (WHERE "Movement"='Sale'),0)
            INTO budget_reserved,budget_released,budget_used FROM v3."UgcCustomerOfferBudgetEntries"
            WHERE "UgcCustomerOfferId"=NEW."Id";
          IF ledger_reserved<>NEW."ReservedFunding" OR recorded_used<>NEW."UsedFunding" OR
             budget_reserved-budget_released-budget_used<>NEW."ReservedFunding" OR budget_used<>NEW."UsedFunding" OR
             (NEW."PublishedAtUtc" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM v3."UgcCustomerOfferReservations" r
               WHERE r."UgcCustomerOfferId"=NEW."Id" AND r."BusinessId"=NEW."BusinessId" AND r."OriginalAmount"=NEW."FundedLimit")) THEN
            RAISE EXCEPTION 'UGC Customer Offer reserve and Sale projections must reconcile' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER ugc_customer_offer_projection AFTER INSERT OR UPDATE ON v3."UgcCustomerOffers"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_ugc_customer_offer_projection();

        CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3."UgcCustomerOfferReservations"
          FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();
        CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3."UgcCustomerOfferBudgetEntries"
          FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();
        CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3."UgcCustomerOfferSales"
          FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();

        CREATE OR REPLACE FUNCTION v3.guard_qr_use() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD."Status"<>'Issued' OR NEW."TokenHash"<>OLD."TokenHash" OR NEW."Id"<>OLD."Id" OR
             NEW."BusinessId"<>OLD."BusinessId" OR NEW."CustomerId"<>OLD."CustomerId" OR
             NEW."Source"<>OLD."Source" OR NEW."CreatorId" IS DISTINCT FROM OLD."CreatorId" OR
             NEW."PromotionId" IS DISTINCT FROM OLD."PromotionId" OR
             NEW."CreatorAllocationId" IS DISTINCT FROM OLD."CreatorAllocationId" OR
             NEW."UgcCustomerOfferId" IS DISTINCT FROM OLD."UgcCustomerOfferId" OR
             NEW."IssuedAtUtc"<>OLD."IssuedAtUtc" OR NEW."ExpiresAtUtc"<>OLD."ExpiresAtUtc" OR
             NEW."IdempotencyReference"<>OLD."IdempotencyReference" THEN
            RAISE EXCEPTION 'QR binding and history are immutable' USING ERRCODE='23514';
          END IF;
          IF NEW."Status"='Expired' AND clock_timestamp()>=OLD."ExpiresAtUtc" AND NEW."UsedAtUtc" IS NULL AND
             NEW."SaleId" IS NULL AND NEW."UgcCustomerOfferSaleId" IS NULL THEN RETURN NEW; END IF;
          IF NEW."Status"<>'Used' OR NEW."UsedAtUtc" IS NULL OR NEW."UsedAtUtc"<NEW."IssuedAtUtc" OR
             NEW."UsedAtUtc">=NEW."ExpiresAtUtc" THEN
            RAISE EXCEPTION 'QR may only be consumed once before expiry' USING ERRCODE='23514';
          END IF;
          IF NEW."Source"='ViewAndSalePromotion' AND NOT EXISTS (
            SELECT 1 FROM v3."VerifiedSales" s WHERE s."Id"=NEW."SaleId" AND s."QrTokenReference"=NEW."Id"::text AND
              s."BusinessId"=NEW."BusinessId" AND s."CustomerId"=NEW."CustomerId" AND
              s."CreatorId"=NEW."CreatorId" AND s."CreatorAllocationId"=NEW."CreatorAllocationId" AND
              s."PromotionId"=NEW."PromotionId") THEN
            RAISE EXCEPTION 'View & Sale QR must match its bound Sale' USING ERRCODE='23514';
          END IF;
          IF NEW."Source"='UgcCustomerOffer' AND NOT EXISTS (
            SELECT 1 FROM v3."UgcCustomerOfferSales" s WHERE s."Id"=NEW."UgcCustomerOfferSaleId" AND
              s."QrTokenReference"=NEW."Id"::text AND s."BusinessId"=NEW."BusinessId" AND
              s."CustomerId"=NEW."CustomerId" AND s."UgcCustomerOfferId"=NEW."UgcCustomerOfferId") THEN
            RAISE EXCEPTION 'UGC Customer Offer QR must match its bound Sale' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;

        CREATE OR REPLACE FUNCTION v3.check_wallet_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE bid uuid; available numeric; reserved numeric; actual_available numeric; actual_reserved numeric;
        BEGIN
          bid:=NEW."BusinessId"; IF bid IS NULL THEN RETURN NULL; END IF;
          SELECT coalesce(sum(CASE WHEN l."Account"='BusinessAvailable' THEN CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END ELSE 0 END),0),
            coalesce(sum(CASE WHEN l."Account" IN ('CampaignUnallocatedReserve','CreatorAllocatedReserve','UgcAllocatedReserve','UgcCustomerOfferReserve')
              THEN CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END ELSE 0 END),0)
            INTO available,reserved FROM v3."FinancialJournalLines" l JOIN v3."FinancialJournals" j ON j."Id"=l."JournalId"
            WHERE j."BusinessId"=bid;
          SELECT "AvailableBalance","ReservedBalance" INTO actual_available,actual_reserved FROM v3."BusinessWallets" WHERE "BusinessId"=bid;
          IF actual_available IS NULL OR actual_available<>available OR actual_reserved<>reserved THEN
            RAISE EXCEPTION 'Business wallet must reconcile with authoritative journals' USING ERRCODE='23514';
          END IF; RETURN NULL;
        END $$;

        CREATE OR REPLACE FUNCTION v3.check_platform_revenue_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE total numeric; source text;
        BEGIN
          SELECT "SourceType" INTO source FROM v3."FinancialJournals" WHERE "Id"=NEW."JournalId";
          SELECT coalesce(sum("Amount"),0) INTO total FROM v3."FinancialJournalLines"
            WHERE "JournalId"=NEW."JournalId" AND "Type"='Credit' AND "Account"='PlatformRevenue';
          IF total<>NEW."Amount" OR
             (NEW."Source"='ViewRewardPlatformShare' AND source<>'ViewReward') OR
             (NEW."Source"='SalePlatformShare' AND source<>'VerifiedSale') OR
             (NEW."Source"='UgcCustomerOfferSaleFee' AND source<>'UgcCustomerOfferSale') OR
             (NEW."Source"='AuthorizedAdjustment' AND source<>'Adjustment') THEN
            RAISE EXCEPTION 'Platform revenue must reconcile with its authoritative journal' USING ERRCODE='23514';
          END IF; RETURN NULL;
        END $$;
        """;
}
