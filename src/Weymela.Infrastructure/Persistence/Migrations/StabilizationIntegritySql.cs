namespace Weymela.Infrastructure.Persistence.Migrations;

internal static class StabilizationIntegritySql
{
    internal const string Up = """
        CREATE TRIGGER ugc_correction_history BEFORE UPDATE OR DELETE ON v3."UgcPaymentCorrections"
          FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();

        CREATE OR REPLACE FUNCTION v3.check_earned_account() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE subject uuid; actual numeric; earned numeric; paid numeric; kind text;
        BEGIN
          IF TG_TABLE_NAME IN ('CreatorEarningsAccounts','CreatorEarningEntries','UgcPaymentCorrections') THEN subject:=NEW."CreatorId";kind:='Creator';
          ELSIF TG_TABLE_NAME IN ('CustomerCashbackAccounts','CustomerCashbackEntries') THEN subject:=NEW."CustomerId";kind:='Customer';
          ELSE kind:=NEW."Beneficiary";subject:=coalesce(NEW."CreatorId",NEW."CustomerId"); END IF;
          IF kind='Creator' THEN
            SELECT "AvailableEarnings" INTO actual FROM v3."CreatorEarningsAccounts" WHERE "CreatorId"=subject;
            SELECT coalesce(sum("Amount"),0)-(SELECT coalesce(sum("CreatorAmount"),0) FROM v3."UgcPaymentCorrections" WHERE "CreatorId"=subject)
              INTO earned FROM v3."CreatorEarningEntries" WHERE "CreatorId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO paid FROM v3."PayoutRecords" WHERE "CreatorId"=subject AND "Status"='Paid';
          ELSE
            SELECT "AvailableCashback" INTO actual FROM v3."CustomerCashbackAccounts" WHERE "CustomerId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO earned FROM v3."CustomerCashbackEntries" WHERE "CustomerId"=subject;
            SELECT coalesce(sum("Amount"),0) INTO paid FROM v3."PayoutRecords" WHERE "CustomerId"=subject AND "Status"='Paid';
          END IF;
          IF actual IS NULL OR actual <> earned-paid OR actual < 0 THEN
            RAISE EXCEPTION 'Earned funds must equal credits less immutable corrections and confirmed payouts' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER ugc_correction_projection AFTER INSERT ON v3."UgcPaymentCorrections"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_earned_account();

        CREATE FUNCTION v3.check_ugc_payment_correction() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE earning record; original record; correction record; assignment record; debit numeric; fee numeric; reserve numeric;
        BEGIN
          SELECT * INTO earning FROM v3."CreatorEarningEntries" WHERE "Id"=NEW."OriginalEarningEntryId";
          SELECT * INTO original FROM v3."FinancialJournals" WHERE "Id"=NEW."OriginalJournalId";
          SELECT * INTO correction FROM v3."FinancialJournals" WHERE "Id"=NEW."CorrectionJournalId";
          SELECT * INTO assignment FROM v3."UgcAssignments" WHERE "Id"=NEW."UgcAssignmentId";
          IF earning."Source"<>'Ugc' OR earning."CreatorId"<>NEW."CreatorId" OR earning."UgcAssignmentId"<>NEW."UgcAssignmentId"
            OR earning."JournalId"<>NEW."OriginalJournalId" OR earning."Amount"<>NEW."CreatorAmount"
            OR original."SourceType"<>'UgcApproval' OR NOT original."IsPosted"
            OR assignment."CreatorId"<>NEW."CreatorId" OR assignment."CreatorPayment"<>NEW."CreatorAmount" OR assignment."PlatformFee"<>NEW."PlatformFee"
            OR correction."SourceType"<>'Adjustment' OR NOT correction."IsPosted"
            OR correction."CreatorId" IS DISTINCT FROM NEW."CreatorId" OR correction."UgcAssignmentId" IS DISTINCT FROM NEW."UgcAssignmentId"
            OR correction."ActorId" IS DISTINCT FROM NEW."AuthorizedBy" OR length(trim(NEW."AuthorizationReference"))=0
            OR NOT EXISTS (SELECT 1 FROM v3."CommercePermissions" WHERE "UserId"=NEW."AuthorizedBy" AND "Role"='PlatformAdmin' AND "IsActive")
            OR EXISTS (SELECT 1 FROM v3."CreatorPublicationVerifications" WHERE "UgcAssignmentId"=NEW."UgcAssignmentId" AND "WentLiveAtUtc" IS NOT NULL)
            OR EXISTS (SELECT 1 FROM v3."PayoutRecords" WHERE "CreatorId"=NEW."CreatorId" AND "Status"='Paid' AND "PaidAtUtc">=earning."CreatedAtUtc") THEN
            RAISE EXCEPTION 'UGC correction must reference an authorized, unspent premature payment' USING ERRCODE='23514';
          END IF;
          SELECT coalesce(sum("Amount"),0) INTO debit FROM v3."FinancialJournalLines" WHERE "JournalId"=NEW."CorrectionJournalId" AND "Type"='Debit' AND "Account"='CreatorPayable';
          SELECT coalesce(sum("Amount"),0) INTO fee FROM v3."FinancialJournalLines" WHERE "JournalId"=NEW."CorrectionJournalId" AND "Type"='Debit' AND "Account"='PlatformRevenue';
          SELECT coalesce(sum("Amount"),0) INTO reserve FROM v3."FinancialJournalLines" WHERE "JournalId"=NEW."CorrectionJournalId" AND "Type"='Credit' AND "Account"='UgcAllocatedReserve';
          IF debit<>NEW."CreatorAmount" OR fee<>NEW."PlatformFee" OR reserve<>NEW."CreatorAmount"+NEW."PlatformFee" THEN
            RAISE EXCEPTION 'UGC correction journal must restore the complete reserved obligation' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER ugc_correction_journal AFTER INSERT ON v3."UgcPaymentCorrections"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_ugc_payment_correction();

        CREATE FUNCTION v3.check_ugc_publication_earning() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."Source"='Ugc' AND NOT EXISTS (SELECT 1 FROM v3."UgcPaymentCorrections" WHERE "OriginalEarningEntryId"=NEW."Id") THEN
            IF NOT EXISTS (SELECT 1 FROM v3."CreatorPublicationVerifications" p JOIN v3."UgcSubmissions" s ON s."Id"=p."UgcSubmissionId"
              WHERE p."UgcAssignmentId"=NEW."UgcAssignmentId" AND p."CreatorId"=NEW."CreatorId" AND p."Status"='Verified'
                AND p."VerifiedAtUtc" IS NOT NULL AND p."WentLiveAtUtc" IS NOT NULL AND length(trim(p."EvidenceReference"))>0 AND s."Status"='Approved')
              OR (SELECT count(*) FROM v3."CreatorEarningEntries" e WHERE e."UgcAssignmentId"=NEW."UgcAssignmentId" AND e."Source"='Ugc'
                AND NOT EXISTS (SELECT 1 FROM v3."UgcPaymentCorrections" c WHERE c."OriginalEarningEntryId"=e."Id"))<>1 THEN
              RAISE EXCEPTION 'UGC fixed earnings require one verified approved publication Go Live' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER ugc_verified_fixed_earning AFTER INSERT ON v3."CreatorEarningEntries"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_ugc_publication_earning();

        CREATE OR REPLACE FUNCTION v3.check_platform_settlement() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          PERFORM pg_advisory_xact_lock(73431001);
          IF (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformSettlements") >
             (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformRevenueEntries")-
             (SELECT coalesce(sum("PlatformFee"),0) FROM v3."UgcPaymentCorrections") THEN
            RAISE EXCEPTION 'Platform settlement cannot exceed net accrued revenue' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER ugc_correction_platform_bound AFTER INSERT ON v3."UgcPaymentCorrections"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_platform_settlement();

        REVOKE EXECUTE ON FUNCTION v3.check_ugc_payment_correction() FROM PUBLIC;
        REVOKE EXECUTE ON FUNCTION v3.check_ugc_publication_earning() FROM PUBLIC;

        -- A leaf item must not be acknowledged just by opening its parent campaign.
        WITH events AS (
          SELECT n."Id", (SELECT o."Payload" FROM v3."OutboxMessages" o
            WHERE n."SourceKey"=o."EventType"||':'||upper(encode(sha256(convert_to(o."Payload"::text,'UTF8')),'hex')) LIMIT 1) AS payload
          FROM v3."InAppNotifications" n
        ), resources AS (
          SELECT "Id", ARRAY(SELECT value::uuid FROM jsonb_each_text(CASE WHEN jsonb_typeof(payload)='object' THEN payload ELSE '{}'::jsonb END)
            WHERE key IN ('Id','AssignmentId','CreatorAllocationId','AllocationId','ParticipationId','RequestId','SubmissionId','PublicationId','DepositId','PayoutId','SaleId','PurchaseId','EnrollmentId','GrantId','PreauthorizationId')
            AND value ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') AS ids
          FROM events
        ) UPDATE v3."InAppNotifications" n SET "RelatedResourceIds"=CASE WHEN cardinality(r.ids)>0 THEN r.ids ELSE
          ARRAY(SELECT match[1]::uuid FROM regexp_matches(n."Route", '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})','g') match) END
          FROM resources r WHERE r."Id"=n."Id";
        UPDATE v3."InAppNotifications" n SET "RelatedResourceIds"=n."RelatedResourceIds"||coalesce(p."UgcAssignmentId",p."CreatorAllocationId")
          FROM v3."CreatorPublicationVerifications" p WHERE p."Id"=ANY(n."RelatedResourceIds")
          AND coalesce(p."UgcAssignmentId",p."CreatorAllocationId") IS NOT NULL;
        """;

    internal const string Down = """
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM v3."UgcPaymentCorrections") THEN
            RAISE EXCEPTION 'Recorded financial corrections require roll-forward recovery; do not remove history';
          END IF;
        END $$;
        DROP TRIGGER ugc_verified_fixed_earning ON v3."CreatorEarningEntries";
        DROP FUNCTION v3.check_ugc_publication_earning();
        DROP TRIGGER ugc_correction_journal ON v3."UgcPaymentCorrections";
        DROP FUNCTION v3.check_ugc_payment_correction();
        DROP TRIGGER ugc_correction_projection ON v3."UgcPaymentCorrections";
        DROP TRIGGER ugc_correction_platform_bound ON v3."UgcPaymentCorrections";
        CREATE OR REPLACE FUNCTION v3.check_earned_account() RETURNS trigger LANGUAGE plpgsql AS $$
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
        CREATE OR REPLACE FUNCTION v3.check_platform_settlement() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM pg_advisory_xact_lock(73431001);
                  IF (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformSettlements") >
                     (SELECT coalesce(sum("Amount"),0) FROM v3."PlatformRevenueEntries") THEN
                    RAISE EXCEPTION 'Platform settlement cannot exceed accrued revenue' USING ERRCODE='23514';
                  END IF;
                  RETURN NULL;
                END $$;
        """;
}
