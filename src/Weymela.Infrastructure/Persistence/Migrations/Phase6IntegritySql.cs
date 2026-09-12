namespace Weymela.Infrastructure.Persistence.Migrations;

// Additive operational hardening. Historical migration helpers are unchanged.
internal static class Phase6IntegritySql
{
    public const string Create = """
        CREATE OR REPLACE FUNCTION v3.guard_qr_use() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD."Status" <> 'Issued' OR NEW."TokenHash" <> OLD."TokenHash" OR NEW."Id"<>OLD."Id" OR
             NEW."BusinessId" <> OLD."BusinessId" OR NEW."CustomerId" <> OLD."CustomerId" OR
             NEW."CreatorId" <> OLD."CreatorId" OR NEW."PromotionId" <> OLD."PromotionId" OR
             NEW."CreatorAllocationId" <> OLD."CreatorAllocationId" OR NEW."IssuedAtUtc" <> OLD."IssuedAtUtc" OR
             NEW."ExpiresAtUtc" <> OLD."ExpiresAtUtc" OR NEW."IdempotencyReference" <> OLD."IdempotencyReference" THEN
            RAISE EXCEPTION 'QR binding and history are immutable' USING ERRCODE='23514';
          END IF;
          IF NEW."Status"='Expired' AND clock_timestamp()>=OLD."ExpiresAtUtc" AND NEW."UsedAtUtc" IS NULL AND NEW."SaleId" IS NULL THEN RETURN NEW; END IF;
          IF NEW."Status" <> 'Used' OR NEW."SaleId" IS NULL OR NEW."UsedAtUtc" IS NULL OR
             NEW."UsedAtUtc" < NEW."IssuedAtUtc" OR NEW."UsedAtUtc" >= NEW."ExpiresAtUtc" OR
             NOT EXISTS (SELECT 1 FROM v3."VerifiedSales" s WHERE s."Id"=NEW."SaleId" AND s."QrTokenReference"=NEW."Id"::text AND
               s."BusinessId"=NEW."BusinessId" AND s."CustomerId"=NEW."CustomerId" AND s."CreatorId"=NEW."CreatorId" AND
               s."CreatorAllocationId"=NEW."CreatorAllocationId" AND s."PromotionId"=NEW."PromotionId") THEN
            RAISE EXCEPTION 'QR may only be consumed once by its bound sale before expiry' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;

        CREATE FUNCTION v3.guard_deposit_review() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW."Status"<>'Pending' THEN RAISE EXCEPTION 'Deposit must begin pending' USING ERRCODE='23514'; END IF;
            RETURN NEW;
          END IF;
          IF OLD."Status"<>'Pending' OR NEW."Status" NOT IN ('Approved','Rejected') OR NEW."Id"<>OLD."Id" OR
             NEW."BusinessId"<>OLD."BusinessId" OR NEW."SubmittedBy"<>OLD."SubmittedBy" OR NEW."Amount"<>OLD."Amount" OR
             NEW."Provider"<>OLD."Provider" OR NEW."ExternalReference"<>OLD."ExternalReference" OR
             NEW."ProofReference" IS DISTINCT FROM OLD."ProofReference" OR NEW."SubmittedAtUtc"<>OLD."SubmittedAtUtc" OR
             NEW."ReviewedAtUtc"<OLD."SubmittedAtUtc" OR NOT EXISTS
               (SELECT 1 FROM v3."CommercePermissions" p WHERE p."UserId"=NEW."ReviewedBy" AND p."Role"='PlatformAdmin' AND p."IsActive") THEN
            RAISE EXCEPTION 'Deposit requires one authorized immutable review' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION v3.check_approved_deposit_journal() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."Status"='Approved' AND NOT EXISTS
             (SELECT 1 FROM v3."FinancialJournals" j JOIN v3."FinancialJournalLines" l ON l."JournalId"=j."Id"
              WHERE j."Id"=NEW."JournalId" AND j."BusinessId"=NEW."BusinessId" AND j."SourceType"='Deposit' AND
                j."IdempotencyReference"='approved-deposit-'||replace(NEW."Id"::text,'-','') AND
                l."Account"='BusinessAvailable' AND l."Type"='Credit' AND l."Amount"=NEW."Amount") THEN
            RAISE EXCEPTION 'Approved deposit must reference its exact wallet journal' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER deposit_review BEFORE INSERT OR UPDATE ON v3."DepositRequests" FOR EACH ROW EXECUTE FUNCTION v3.guard_deposit_review();
        CREATE CONSTRAINT TRIGGER deposit_journal AFTER INSERT OR UPDATE ON v3."DepositRequests"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_approved_deposit_journal();
        CREATE TRIGGER deposit_history BEFORE DELETE ON v3."DepositRequests" FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();

        CREATE FUNCTION v3.guard_notification_identity() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."Id"<>OLD."Id" OR NEW."UserId"<>OLD."UserId" OR NEW."Role"<>OLD."Role" OR NEW."SourceKey"<>OLD."SourceKey" OR
             NEW."EventType"<>OLD."EventType" OR NEW."Title"<>OLD."Title" OR NEW."Message"<>OLD."Message" OR
             NEW."Route"<>OLD."Route" OR NEW."CreatedAtUtc"<>OLD."CreatedAtUtc" OR
             (OLD."ReadAtUtc" IS NOT NULL AND NEW."ReadAtUtc" IS DISTINCT FROM OLD."ReadAtUtc") THEN
            RAISE EXCEPTION 'Notification identity, content and read history are immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER notification_identity BEFORE UPDATE ON v3."InAppNotifications" FOR EACH ROW EXECUTE FUNCTION v3.guard_notification_identity();
        CREATE FUNCTION v3.guard_outbox_envelope() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."Id"<>OLD."Id" OR NEW."EventType"<>OLD."EventType" OR NEW."Payload"<>OLD."Payload" OR NEW."OccurredAtUtc"<>OLD."OccurredAtUtc" THEN
            RAISE EXCEPTION 'Outbox event envelopes are immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER outbox_envelope BEFORE UPDATE ON v3."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION v3.guard_outbox_envelope();
        """;
}
