namespace Weymela.Infrastructure.Persistence.Migrations;

internal static class Phase4IntegritySql
{
    public const string Create = """
        CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3."ViewRewardReceipts"
          FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();
        CREATE FUNCTION v3.guard_participation_baseline() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."BaselineViews" <> OLD."BaselineViews" OR NEW."Provider" <> OLD."Provider" OR
             NEW."ExternalContentId" <> OLD."ExternalContentId" OR NEW."CreatorAllocationId" <> OLD."CreatorAllocationId" OR
             NEW."CreatorId" <> OLD."CreatorId" OR NEW."PromotionId" <> OLD."PromotionId" OR
             NEW."WentLiveAtUtc" <> OLD."WentLiveAtUtc" OR NEW."LatestVerifiedViews" < OLD."LatestVerifiedViews" OR
             NEW."RewardedViewCount" < OLD."RewardedViewCount" THEN
            RAISE EXCEPTION 'Participation baseline, identity and rewarded views are immutable/monotonic' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER participation_baseline BEFORE UPDATE ON v3."CreatorPromotionParticipations"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_participation_baseline();
        ALTER TABLE v3."OfferQrSessions" ADD CONSTRAINT "CK_Qr_Digest" CHECK ("TokenHash" ~ '^[0-9A-F]{64}$');
        CREATE FUNCTION v3.guard_qr_use() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD."Status" <> 'Issued' OR NEW."Status" <> 'Used' OR NEW."TokenHash" <> OLD."TokenHash" OR
             NEW."BusinessId" <> OLD."BusinessId" OR NEW."CustomerId" <> OLD."CustomerId" OR
             NEW."CreatorId" <> OLD."CreatorId" OR NEW."PromotionId" <> OLD."PromotionId" OR
             NEW."CreatorAllocationId" <> OLD."CreatorAllocationId" OR NEW."IssuedAtUtc" <> OLD."IssuedAtUtc" OR
             NEW."ExpiresAtUtc" <> OLD."ExpiresAtUtc" OR NEW."SaleId" IS NULL OR
             NEW."UsedAtUtc" IS NULL OR NEW."UsedAtUtc" < NEW."IssuedAtUtc" OR NEW."UsedAtUtc" >= NEW."ExpiresAtUtc" OR
             NOT EXISTS (SELECT 1 FROM v3."VerifiedSales" s WHERE s."Id"=NEW."SaleId" AND
               s."QrTokenReference"=NEW."Id"::text AND s."BusinessId"=NEW."BusinessId" AND
               s."CustomerId"=NEW."CustomerId" AND s."CreatorId"=NEW."CreatorId" AND
               s."CreatorAllocationId"=NEW."CreatorAllocationId" AND s."PromotionId"=NEW."PromotionId") THEN
            RAISE EXCEPTION 'QR may only be consumed once by its bound sale before expiry' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER qr_use BEFORE UPDATE ON v3."OfferQrSessions" FOR EACH ROW EXECUTE FUNCTION v3.guard_qr_use();
        CREATE UNIQUE INDEX "UX_VerifiedSale_QrReference" ON v3."VerifiedSales" ("QrTokenReference");

        CREATE FUNCTION v3.guard_sale_snapshot_amounts() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE rates record;
        BEGIN
          SELECT * INTO rates FROM v3."PricingSnapshots" WHERE "PromotionId"=NEW."PromotionId";
          IF NEW."CreatorCommissionAmount" <> round(NEW."PurchaseAmount"*rates."CreatorCommissionPercent"/100,2) OR
             NEW."CustomerCashbackAmount" <> round(NEW."PurchaseAmount"*rates."CustomerCashbackPercent"/100,2) OR
             NEW."PlatformRevenueAmount" <> round(NEW."PurchaseAmount"*rates."PlatformPercent"/100,2) THEN
            RAISE EXCEPTION 'Sale amounts must use the immutable Admin pricing snapshot' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER sale_snapshot_amounts BEFORE INSERT ON v3."VerifiedSales" FOR EACH ROW EXECUTE FUNCTION v3.guard_sale_snapshot_amounts();

        CREATE FUNCTION v3.guard_payout_transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD."Status" <> 'Eligible' OR NEW."Status" <> 'Paid' OR NEW."Amount" <> OLD."Amount" OR
             NEW."ThresholdUsed" <> OLD."ThresholdUsed" OR NEW."ConfigurationVersionId" <> OLD."ConfigurationVersionId" OR
             NEW."EligibleAtUtc" <> OLD."EligibleAtUtc" OR NEW."Beneficiary" <> OLD."Beneficiary" OR
             NEW."CreatorId" IS DISTINCT FROM OLD."CreatorId" OR NEW."CustomerId" IS DISTINCT FROM OLD."CustomerId" OR
             NEW."PaidAtUtc" IS NULL OR NEW."PaidBy" IS NULL OR NEW."JournalId" IS NULL OR
             NEW."Reference" IS NULL OR length(trim(NEW."Reference"))=0 THEN
            RAISE EXCEPTION 'Payout can only transition once to confirmed paid without changing its terms' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER payout_transition BEFORE UPDATE ON v3."PayoutRecords" FOR EACH ROW EXECUTE FUNCTION v3.guard_payout_transition();
        CREATE TRIGGER payout_history BEFORE DELETE ON v3."PayoutRecords" FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();

        CREATE FUNCTION v3.check_earned_account() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE subject uuid; actual numeric; earned numeric; paid numeric; kind text;
        BEGIN
          IF TG_TABLE_NAME IN ('CreatorEarningsAccounts','CreatorEarningEntries') THEN subject:=NEW."CreatorId";kind:='Creator';
          ELSIF TG_TABLE_NAME IN ('CustomerCashbackAccounts','CustomerCashbackEntries') THEN subject:=NEW."CustomerId";kind:='Customer';
          ELSE kind:=NEW."Beneficiary";subject:=coalesce(NEW."CreatorId",NEW."CustomerId"); END IF;
          IF kind='Creator' THEN
            SELECT "AvailableEarnings" INTO actual FROM v3."CreatorEarningsAccounts" WHERE "CreatorId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO earned FROM v3."CreatorEarningEntries" WHERE "CreatorId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO paid FROM v3."PayoutRecords" WHERE "CreatorId"=subject AND "Status"='Paid';
          ELSE
            SELECT "AvailableCashback" INTO actual FROM v3."CustomerCashbackAccounts" WHERE "CustomerId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO earned FROM v3."CustomerCashbackEntries" WHERE "CustomerId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO paid FROM v3."PayoutRecords" WHERE "CustomerId"=subject AND "Status"='Paid';
          END IF;
          IF actual IS NULL OR actual <> earned-paid OR actual < 0 THEN
            RAISE EXCEPTION 'Earned funds must equal credits less confirmed payouts' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER creator_earned_projection AFTER INSERT OR UPDATE ON v3."CreatorEarningsAccounts"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_earned_account();
        CREATE CONSTRAINT TRIGGER customer_earned_projection AFTER INSERT OR UPDATE ON v3."CustomerCashbackAccounts"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_earned_account();
        CREATE CONSTRAINT TRIGGER payout_earned_projection AFTER INSERT OR UPDATE ON v3."PayoutRecords"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_earned_account();
        CREATE CONSTRAINT TRIGGER creator_credit_projection AFTER INSERT ON v3."CreatorEarningEntries"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_earned_account();
        CREATE CONSTRAINT TRIGGER customer_credit_projection AFTER INSERT ON v3."CustomerCashbackEntries"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_earned_account();
        CREATE FUNCTION v3.check_payout_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE debit numeric; credit numeric; account text;
        BEGIN
          IF NEW."Status" <> 'Paid' THEN RETURN NULL; END IF;
          account:=CASE WHEN NEW."Beneficiary"='Creator' THEN 'CreatorPayable' ELSE 'CustomerCashbackPayable' END;
          IF NOT EXISTS (SELECT 1 FROM v3."FinancialJournals" j WHERE j."Id"=NEW."JournalId" AND j."SourceType"='Payout'
            AND j."CreatorId" IS NOT DISTINCT FROM NEW."CreatorId" AND j."CustomerId" IS NOT DISTINCT FROM NEW."CustomerId") THEN
            RAISE EXCEPTION 'Payout journal must match beneficiary and source' USING ERRCODE='23514';
          END IF;
          SELECT coalesce(sum("Amount") FILTER (WHERE "Type"='Debit' AND "Account"=account),0),
                 coalesce(sum("Amount") FILTER (WHERE "Type"='Credit' AND "Account"='CashClearing'),0)
            INTO debit,credit FROM v3."FinancialJournalLines" WHERE "JournalId"=NEW."JournalId";
          IF debit <> NEW."Amount" OR credit <> NEW."Amount" THEN
            RAISE EXCEPTION 'Payout amount must reconcile with beneficiary payable and clearing journal' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER payout_accounting AFTER INSERT OR UPDATE ON v3."PayoutRecords"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_payout_journal();
        """;
}
