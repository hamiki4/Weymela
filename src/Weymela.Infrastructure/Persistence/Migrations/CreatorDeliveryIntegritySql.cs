namespace Weymela.Infrastructure.Persistence.Migrations;

internal static class CreatorDeliveryIntegritySql
{
    internal const string Up = """
        CREATE UNIQUE INDEX "UX_UgcPublication_StartedOnce" ON v3."CreatorPublicationVerifications" ("UgcAssignmentId")
          WHERE "UgcAssignmentId" IS NOT NULL AND "WentLiveAtUtc" IS NOT NULL;
        CREATE UNIQUE INDEX "UX_PromotionPublication_StartedOnce" ON v3."CreatorPublicationVerifications" ("CreatorAllocationId")
          WHERE "CreatorAllocationId" IS NOT NULL AND "WentLiveAtUtc" IS NOT NULL;
        CREATE FUNCTION v3.guard_creator_delivery_terms() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME='CreatorPublicationVerifications' THEN
            IF EXISTS (SELECT 1 FROM v3."UgcAssignments" a JOIN v3."UgcOpportunities" o ON o."Id"=a."UgcOpportunityId"
              WHERE a."Id"=NEW."UgcAssignmentId" AND o."DeliverableType"='DirectVideoDelivery') THEN
              RAISE EXCEPTION 'Direct delivery cannot become a public-post campaign' USING ERRCODE='23514';
            END IF;
            IF TG_OP='INSERT' THEN RETURN NEW; END IF;
            IF OLD."WentLiveAtUtc" IS NOT NULL AND (NEW."WentLiveAtUtc" IS DISTINCT FROM OLD."WentLiveAtUtc"
              OR NEW."LiveDurationDays" IS DISTINCT FROM OLD."LiveDurationDays" OR NEW."ExpiresAtUtc" IS DISTINCT FROM OLD."ExpiresAtUtc"
              OR NEW."CreatorId"<>OLD."CreatorId" OR NEW."CreatorSocialProfileId"<>OLD."CreatorSocialProfileId"
              OR NEW."CreatorAllocationId" IS DISTINCT FROM OLD."CreatorAllocationId" OR NEW."UgcAssignmentId" IS DISTINCT FROM OLD."UgcAssignmentId"
              OR NEW."ExternalContentId"<>OLD."ExternalContentId" OR NEW."Provider"<>OLD."Provider"
              OR NEW."UgcSubmissionId" IS DISTINCT FROM OLD."UgcSubmissionId" OR NEW."PromotionContentSubmissionId" IS DISTINCT FROM OLD."PromotionContentSubmissionId") THEN
              RAISE EXCEPTION 'Live publication identity and timing cannot restart' USING ERRCODE='23514';
            END IF;
            IF OLD."GoLiveRequestedAtUtc" IS NOT NULL AND NEW."GoLiveRequestedAtUtc" IS DISTINCT FROM OLD."GoLiveRequestedAtUtc" THEN
              RAISE EXCEPTION 'Creator Go Live intent is immutable' USING ERRCODE='23514';
            END IF;
          ELSE
            IF NEW."DeliverableType"<>OLD."DeliverableType" AND (OLD."Status"<>'Draft' OR OLD."ApprovedCreatorCount">0) THEN
              RAISE EXCEPTION 'Agreed delivery terms are immutable' USING ERRCODE='23514';
            END IF;
            IF OLD."PromotionLiveDurationDays" IS NOT NULL AND NEW."PromotionLiveDurationDays" IS DISTINCT FROM OLD."PromotionLiveDurationDays" THEN
              RAISE EXCEPTION 'Saved campaign duration is immutable' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER creator_live_terms BEFORE INSERT OR UPDATE ON v3."CreatorPublicationVerifications"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_creator_delivery_terms();
        CREATE TRIGGER ugc_delivery_terms BEFORE UPDATE ON v3."UgcOpportunities"
          FOR EACH ROW EXECUTE FUNCTION v3.guard_creator_delivery_terms();

        CREATE OR REPLACE FUNCTION v3.check_ugc_publication_earning() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE public_evidence boolean; direct_evidence boolean;
        BEGIN
          IF NEW."Source"='Ugc' AND NOT EXISTS (SELECT 1 FROM v3."UgcPaymentCorrections" WHERE "OriginalEarningEntryId"=NEW."Id") THEN
            SELECT EXISTS (SELECT 1 FROM v3."CreatorPublicationVerifications" p JOIN v3."UgcSubmissions" s ON s."Id"=p."UgcSubmissionId"
              JOIN v3."UgcAssignments" a ON a."Id"=p."UgcAssignmentId" JOIN v3."UgcOpportunities" o ON o."Id"=a."UgcOpportunityId"
              WHERE p."UgcAssignmentId"=NEW."UgcAssignmentId" AND p."CreatorId"=NEW."CreatorId" AND p."Status"='Verified'
                AND p."VerifiedAtUtc" IS NOT NULL AND p."WentLiveAtUtc" IS NOT NULL AND length(trim(p."EvidenceReference"))>0
                AND s."Status"='Approved' AND o."DeliverableType"='PublicSocialVideo') INTO public_evidence;
            SELECT EXISTS (SELECT 1 FROM v3."UgcAssignments" a JOIN v3."UgcOpportunities" o ON o."Id"=a."UgcOpportunityId"
              JOIN v3."UgcSubmissions" s ON s."UgcAssignmentId"=a."Id" JOIN v3."PrivateReviewMediaAssets" m ON m."Id"=s."ReviewMediaAssetId"
              JOIN v3."FinancialJournals" j ON j."Id"=NEW."JournalId"
              WHERE a."Id"=NEW."UgcAssignmentId" AND a."CreatorId"=NEW."CreatorId" AND a."Status"='Approved' AND a."CompletedAtUtc" IS NOT NULL
                AND o."DeliverableType"='DirectVideoDelivery' AND o."ContentType"='Video' AND s."Status"='Approved'
                AND s."ReviewedAtUtc" IS NOT NULL AND s."ReviewedByUserId" IS NOT NULL
                AND m."UgcAssignmentId"=a."Id" AND m."CreatorId"=a."CreatorId" AND m."BusinessId"=o."BusinessId" AND m."ContentType"='video/mp4'
                AND j."SourceType"='UgcApproval' AND j."IsPosted" AND j."ActorId"=s."ReviewedByUserId"
                AND j."UgcAssignmentId"=a."Id" AND j."CreatorId"=a."CreatorId" AND j."BusinessId"=o."BusinessId"
                AND EXISTS (SELECT 1 FROM v3."CommercePermissions" c WHERE c."UserId"=s."ReviewedByUserId" AND c."Role"='Business' AND c."SubjectId"=o."BusinessId" AND c."IsActive")
                AND NOT EXISTS (SELECT 1 FROM v3."UgcSubmissions" newer WHERE newer."UgcAssignmentId"=a."Id" AND newer."ContentRevisionNumber">s."ContentRevisionNumber")
                AND NOT EXISTS (SELECT 1 FROM v3."UgcCustomerOffers" f WHERE f."UgcOpportunityId"=o."Id")) INTO direct_evidence;
            IF NOT (public_evidence OR direct_evidence)
              OR (SELECT count(*) FROM v3."CreatorEarningEntries" e WHERE e."UgcAssignmentId"=NEW."UgcAssignmentId" AND e."Source"='Ugc'
                AND NOT EXISTS (SELECT 1 FROM v3."UgcPaymentCorrections" c WHERE c."OriginalEarningEntryId"=e."Id"))<>1 THEN
              RAISE EXCEPTION 'UGC earnings require one accepted publication or approved direct delivery under saved terms' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE FUNCTION v3.check_direct_delivery_scope() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE opportunity uuid;
        BEGIN
          IF TG_TABLE_NAME='UgcOpportunities' THEN opportunity:=NEW."Id"; ELSE opportunity:=NEW."UgcOpportunityId"; END IF;
          IF EXISTS (SELECT 1 FROM v3."UgcOpportunities" o WHERE o."Id"=opportunity AND o."DeliverableType"='DirectVideoDelivery')
            AND (EXISTS (SELECT 1 FROM v3."UgcCustomerOffers" WHERE "UgcOpportunityId"=opportunity)
              OR EXISTS (SELECT 1 FROM v3."UgcPlatformRequirements" WHERE "UgcOpportunityId"=opportunity)
              OR EXISTS (SELECT 1 FROM v3."UgcPlatformCapacities" WHERE "UgcOpportunityId"=opportunity)) THEN
            RAISE EXCEPTION 'Direct delivery is UGC Only without social requirements' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER direct_delivery_scope AFTER INSERT OR UPDATE ON v3."UgcOpportunities"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_direct_delivery_scope();
        CREATE CONSTRAINT TRIGGER direct_offer_scope AFTER INSERT OR UPDATE ON v3."UgcCustomerOffers"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_direct_delivery_scope();
        CREATE CONSTRAINT TRIGGER direct_requirement_scope AFTER INSERT OR UPDATE ON v3."UgcPlatformRequirements"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_direct_delivery_scope();
        CREATE CONSTRAINT TRIGGER direct_capacity_scope AFTER INSERT OR UPDATE ON v3."UgcPlatformCapacities"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_direct_delivery_scope();
        CREATE FUNCTION v3.reject_direct_delivery_correction() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM v3."UgcAssignments" a JOIN v3."UgcOpportunities" o ON o."Id"=a."UgcOpportunityId"
            WHERE a."Id"=NEW."UgcAssignmentId" AND o."DeliverableType"='DirectVideoDelivery') THEN
            RAISE EXCEPTION 'Approved direct delivery cannot be reversed as premature public earnings' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER direct_delivery_correction BEFORE INSERT ON v3."UgcPaymentCorrections"
          FOR EACH ROW EXECUTE FUNCTION v3.reject_direct_delivery_correction();
        REVOKE EXECUTE ON FUNCTION v3.reject_direct_delivery_correction() FROM PUBLIC;
        REVOKE EXECUTE ON FUNCTION v3.guard_creator_delivery_terms() FROM PUBLIC;
        REVOKE EXECUTE ON FUNCTION v3.check_direct_delivery_scope() FROM PUBLIC;
        """;
}
