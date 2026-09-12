START TRANSACTION;
ALTER TABLE v3."OutboxMessages" ADD "FailedAtUtc" timestamp with time zone;

ALTER TABLE v3."OutboxMessages" ADD "FailureCount" integer NOT NULL DEFAULT 0;

ALTER TABLE v3."OutboxMessages" ADD "NextAttemptAtUtc" timestamp with time zone;

ALTER TABLE v3."OutboxMessages" ADD "RecipientCursor" uuid;

CREATE TABLE v3."DepositRequests" (
    "Id" uuid NOT NULL,
    "BusinessId" uuid NOT NULL,
    "SubmittedBy" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Provider" character varying(40) NOT NULL,
    "ExternalReference" character varying(120) NOT NULL,
    "ProofReference" character varying(120),
    "Status" character varying(64) NOT NULL,
    "SubmittedAtUtc" timestamp with time zone NOT NULL,
    "ReviewedAtUtc" timestamp with time zone,
    "ReviewedBy" uuid,
    "ConfirmationReference" character varying(120),
    "JournalId" uuid,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_DepositRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_DepositRequest_Journal" CHECK (("Status"='Approved' AND "JournalId" IS NOT NULL) OR ("Status" IN ('Pending','Rejected') AND "JournalId" IS NULL)),
    CONSTRAINT "CK_DepositRequest_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "CK_DepositRequest_Review" CHECK (("Status"='Pending' AND "ReviewedBy" IS NULL AND "ReviewedAtUtc" IS NULL) OR ("Status" IN ('Approved','Rejected') AND "ReviewedBy" IS NOT NULL AND "ReviewedAtUtc" IS NOT NULL AND "ConfirmationReference" IS NOT NULL)),
    CONSTRAINT "FK_DepositRequests_BusinessWallets_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES v3."BusinessWallets" ("BusinessId") ON DELETE RESTRICT,
    CONSTRAINT "FK_DepositRequests_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."IdentityBindings" (
    "Id" uuid NOT NULL,
    "Provider" character varying(40) NOT NULL,
    "ProjectId" character varying(100) NOT NULL,
    "ExternalSubject" character varying(128) NOT NULL,
    "UserId" uuid NOT NULL,
    "IsActive" boolean NOT NULL,
    "ValidAfterUtc" timestamp with time zone NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_IdentityBindings" PRIMARY KEY ("Id")
);

CREATE TABLE v3."InAppNotifications" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Role" character varying(64) NOT NULL,
    "SourceKey" character varying(160) NOT NULL,
    "EventType" character varying(100) NOT NULL,
    "Title" character varying(160) NOT NULL,
    "Message" character varying(500) NOT NULL,
    "Route" character varying(200) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ReadAtUtc" timestamp with time zone,
    "PushState" character varying(64) NOT NULL,
    "PushAttempts" integer NOT NULL,
    "NextPushAtUtc" timestamp with time zone,
    "LastPushErrorCode" character varying(80),
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_InAppNotifications" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Notification_ReadTime" CHECK ("ReadAtUtc" IS NULL OR "ReadAtUtc" >= "CreatedAtUtc")
);

CREATE TABLE v3."PublicWorkspaceProfiles" (
    "SubjectId" uuid NOT NULL,
    "Role" character varying(64) NOT NULL,
    "DisplayName" character varying(120) NOT NULL,
    "PublicId" character varying(80) NOT NULL,
    "Region" character varying(80) NOT NULL,
    "Category" character varying(80) NOT NULL,
    "VerifiedFollowers" bigint NOT NULL,
    "VerifiedViews" bigint NOT NULL,
    "SocialVerified" boolean NOT NULL,
    "PortfolioUrl" character varying(500),
    "DirectionsUrl" character varying(500),
    CONSTRAINT "PK_PublicWorkspaceProfiles" PRIMARY KEY ("SubjectId", "Role"),
    CONSTRAINT "CK_PublicProfile_Metrics" CHECK ("VerifiedFollowers" >= 0 AND "VerifiedViews" >= 0)
);

CREATE TABLE v3."WorkerCheckpoints" (
    "Name" character varying(100) NOT NULL,
    "LastEffectiveConfigurationId" uuid,
    "LastSeenAtUtc" timestamp with time zone NOT NULL,
    "LastSuccessAtUtc" timestamp with time zone,
    "LastErrorCode" character varying(80),
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_WorkerCheckpoints" PRIMARY KEY ("Name")
);

CREATE INDEX "IX_OutboxMessages_NextAttemptAtUtc" ON v3."OutboxMessages" ("NextAttemptAtUtc") WHERE "ProcessedAtUtc" IS NULL AND "FailedAtUtc" IS NULL;

CREATE INDEX "IX_OfferQrSessions_ExpiresAtUtc" ON v3."OfferQrSessions" ("ExpiresAtUtc") WHERE "Status"='Issued';

CREATE UNIQUE INDEX "IX_DepositRequests_BusinessId_Provider_ExternalReference" ON v3."DepositRequests" ("BusinessId", "Provider", "ExternalReference");

CREATE INDEX "IX_DepositRequests_JournalId" ON v3."DepositRequests" ("JournalId");

CREATE UNIQUE INDEX "IX_DepositRequests_Provider_ConfirmationReference" ON v3."DepositRequests" ("Provider", "ConfirmationReference") WHERE "Status"='Approved';

CREATE INDEX "IX_DepositRequests_Status_SubmittedAtUtc" ON v3."DepositRequests" ("Status", "SubmittedAtUtc");

CREATE UNIQUE INDEX "IX_IdentityBindings_Provider_ProjectId_ExternalSubject" ON v3."IdentityBindings" ("Provider", "ProjectId", "ExternalSubject");

CREATE UNIQUE INDEX "IX_IdentityBindings_UserId" ON v3."IdentityBindings" ("UserId");

CREATE INDEX "IX_InAppNotifications_NextPushAtUtc" ON v3."InAppNotifications" ("NextPushAtUtc") WHERE "PushState"='Pending';

CREATE INDEX "IX_InAppNotifications_UserId_Role_CreatedAtUtc" ON v3."InAppNotifications" ("UserId", "Role", "CreatedAtUtc");

CREATE UNIQUE INDEX "IX_InAppNotifications_UserId_Role_SourceKey" ON v3."InAppNotifications" ("UserId", "Role", "SourceKey");

CREATE UNIQUE INDEX "IX_PublicWorkspaceProfiles_Role_PublicId" ON v3."PublicWorkspaceProfiles" ("Role", "PublicId");

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

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260912011149_AddOperationalSecurityAndNotifications', '10.0.0');

COMMIT;

