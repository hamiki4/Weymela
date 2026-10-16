namespace Weymela.Infrastructure.Persistence.Migrations;

// Append-only funding increases; all pricing, identity and financial history protections remain.
internal static class CashbackReplenishmentIntegritySql
{
    public const string Up = """
        CREATE OR REPLACE FUNCTION v3.guard_ugc_customer_offer() RETURNS trigger LANGUAGE plpgsql AS $$
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
          IF NEW."BenefitMode" IS DISTINCT FROM OLD."BenefitMode" THEN
            RAISE EXCEPTION 'Saved Customer benefit mode is immutable' USING ERRCODE='23514';
          END IF;
          IF OLD."Status" IN ('Active','Exhausted') AND NEW."FundedLimit">OLD."FundedLimit" THEN
            IF NEW."Status"<>'Active' OR NEW."UsedFunding"<>OLD."UsedFunding" OR
               NEW."ReservedFunding"-OLD."ReservedFunding"<>NEW."FundedLimit"-OLD."FundedLimit" OR
               NEW."CustomerDiscountPercent"<>OLD."CustomerDiscountPercent" OR
               NEW."CustomerFacingSlogan" IS DISTINCT FROM OLD."CustomerFacingSlogan" OR
               NEW."StartsAtUtc"<>OLD."StartsAtUtc" OR NEW."EndsAtUtc"<>OLD."EndsAtUtc" OR
               NEW."PublishedAtUtc" IS DISTINCT FROM OLD."PublishedAtUtc" THEN
              RAISE EXCEPTION 'Cashback top-up must preserve terms and reserve its entire increase' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
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
        CREATE OR REPLACE FUNCTION v3.check_ugc_customer_offer_projection() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE ledger_reserved numeric; recorded_used numeric; budget_reserved numeric; budget_released numeric; budget_used numeric;
        BEGIN
          SELECT coalesce(sum(CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END),0)
            INTO ledger_reserved FROM v3."FinancialJournalLines" l JOIN v3."FinancialJournals" j ON j."Id"=l."JournalId"
            WHERE j."UgcCustomerOfferId"=NEW."Id" AND l."Account"='UgcCustomerOfferReserve';
          SELECT coalesce(sum("TotalOfferCharge"),0) INTO recorded_used FROM v3."UgcCustomerOfferSales"
            WHERE "UgcCustomerOfferId"=NEW."Id";
          SELECT coalesce(sum("Amount") FILTER (WHERE "Movement" IN ('Reserved','Increased')),0),
                 coalesce(sum("Amount") FILTER (WHERE "Movement"='Released'),0),
                 coalesce(sum("Amount") FILTER (WHERE "Movement"='Sale'),0)
            INTO budget_reserved,budget_released,budget_used FROM v3."UgcCustomerOfferBudgetEntries"
            WHERE "UgcCustomerOfferId"=NEW."Id";
          IF ledger_reserved<>NEW."ReservedFunding" OR recorded_used<>NEW."UsedFunding" OR
             budget_reserved-budget_released-budget_used<>NEW."ReservedFunding" OR budget_used<>NEW."UsedFunding" OR
             (NEW."PublishedAtUtc" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM v3."UgcCustomerOfferReservations" r
               WHERE r."UgcCustomerOfferId"=NEW."Id" AND r."BusinessId"=NEW."BusinessId" AND r."OriginalAmount" + coalesce((SELECT sum(b."Amount") FROM v3."UgcCustomerOfferBudgetEntries" b
                 WHERE b."UgcCustomerOfferId"=NEW."Id" AND b."Movement"='Increased'),0)=NEW."FundedLimit")) THEN
            RAISE EXCEPTION 'UGC Customer Offer reserve and Sale projections must reconcile' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        """;
    public const string Down = """
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM v3."UgcCustomerOfferBudgetEntries" WHERE "Movement"='Increased') THEN
            RAISE EXCEPTION 'Cashback top-up history requires the forward-compatible guards; roll back application images only';
          END IF;
        END $$;
        CREATE OR REPLACE FUNCTION v3.guard_ugc_customer_offer() RETURNS trigger LANGUAGE plpgsql AS $$
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
        CREATE OR REPLACE FUNCTION v3.check_ugc_customer_offer_projection() RETURNS trigger LANGUAGE plpgsql AS $$
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
        """;
}
