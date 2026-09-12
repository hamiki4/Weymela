START TRANSACTION;
ALTER TABLE v3."PromotionViewVerifications" ADD "IsAnomaly" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE v3."PromotionViewVerifications" ADD "IsBaseline" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE v3."PromotionViewVerifications" ADD "ParticipationId" uuid;

ALTER TABLE v3."PromotionViewVerifications" ADD "ReportedViews" bigint;

ALTER TABLE v3."PlatformSettlements" ADD "SettledBy" uuid;

CREATE TABLE v3."CommercePermissions" (
    "UserId" uuid NOT NULL,
    "Role" character varying(64) NOT NULL,
    "SubjectId" uuid NOT NULL,
    "BusinessId" uuid,
    "IsActive" boolean NOT NULL,
    "CanCheckout" boolean NOT NULL,
    CONSTRAINT "PK_CommercePermissions" PRIMARY KEY ("UserId", "Role", "SubjectId")
);

CREATE TABLE v3."CreatorPromotionParticipations" (
    "Id" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "CreatorAllocationId" uuid NOT NULL,
    "Provider" text NOT NULL,
    "ExternalContentId" text NOT NULL,
    "BaselineViews" bigint NOT NULL,
    "LatestVerifiedViews" bigint NOT NULL,
    "RewardedViewCount" bigint NOT NULL,
    "Status" character varying(64) NOT NULL,
    "WentLiveAtUtc" timestamp with time zone NOT NULL,
    "LatestVerifiedAtUtc" timestamp with time zone NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_CreatorPromotionParticipations" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Participation_Views" CHECK ("BaselineViews" >= 0 AND "LatestVerifiedViews" >= "BaselineViews" AND "RewardedViewCount" >= 0 AND "RewardedViewCount" <= "LatestVerifiedViews" - "BaselineViews"),
    CONSTRAINT "FK_CreatorPromotionParticipations_CreatorAllocations_CreatorAl~" FOREIGN KEY ("CreatorAllocationId", "PromotionId", "CreatorId") REFERENCES v3."CreatorAllocations" ("Id", "PromotionId", "CreatorId") ON DELETE RESTRICT
);

CREATE TABLE v3."OfferQrSessions" (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "CreatorAllocationId" uuid NOT NULL,
    "BusinessId" uuid NOT NULL,
    "TokenHash" character varying(64) NOT NULL,
    "IssuedAtUtc" timestamp with time zone NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "UsedAtUtc" timestamp with time zone,
    "SaleId" uuid,
    "Status" character varying(64) NOT NULL,
    "IdempotencyReference" text NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_OfferQrSessions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Qr_Expiry" CHECK ("ExpiresAtUtc" = "IssuedAtUtc" + INTERVAL '5 minutes'),
    CONSTRAINT "FK_OfferQrSessions_CreatorAllocations_CreatorAllocationId_Prom~" FOREIGN KEY ("CreatorAllocationId", "PromotionId", "CreatorId") REFERENCES v3."CreatorAllocations" ("Id", "PromotionId", "CreatorId") ON DELETE RESTRICT,
    CONSTRAINT "FK_OfferQrSessions_VerifiedSales_SaleId" FOREIGN KEY ("SaleId") REFERENCES v3."VerifiedSales" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."PayoutRecords" (
    "Id" uuid NOT NULL,
    "Beneficiary" character varying(64) NOT NULL,
    "CreatorId" uuid,
    "CustomerId" uuid,
    "ThresholdUsed" numeric(18,2) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "ConfigurationVersionId" uuid NOT NULL,
    "EligibleAtUtc" timestamp with time zone NOT NULL,
    "PaidAtUtc" timestamp with time zone,
    "PaidBy" uuid,
    "Reference" text,
    "JournalId" uuid,
    "Status" character varying(64) NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_PayoutRecords" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Payout_Amounts" CHECK ("Amount" > 0 AND "Amount" = "ThresholdUsed" AND (("Beneficiary"='Creator' AND "CreatorId" IS NOT NULL AND "CustomerId" IS NULL) OR ("Beneficiary"='Customer' AND "CustomerId" IS NOT NULL AND "CreatorId" IS NULL))),
    CONSTRAINT "FK_PayoutRecords_CreatorEarningsAccounts_CreatorId" FOREIGN KEY ("CreatorId") REFERENCES v3."CreatorEarningsAccounts" ("CreatorId") ON DELETE RESTRICT,
    CONSTRAINT "FK_PayoutRecords_CustomerCashbackAccounts_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES v3."CustomerCashbackAccounts" ("CustomerId") ON DELETE RESTRICT,
    CONSTRAINT "FK_PayoutRecords_FinancialConfigurationVersions_ConfigurationV~" FOREIGN KEY ("ConfigurationVersionId") REFERENCES v3."FinancialConfigurationVersions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PayoutRecords_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."ViewRewardReceipts" (
    "Id" uuid NOT NULL,
    "ParticipationId" uuid NOT NULL,
    "JournalId" uuid NOT NULL,
    "Blocks" bigint NOT NULL,
    "RewardedThrough" bigint NOT NULL,
    "BusinessCharge" numeric(18,2) NOT NULL,
    "CreatorEarning" numeric(18,2) NOT NULL,
    "PlatformEarning" numeric(18,2) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ViewRewardReceipts" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ViewReward_Amounts" CHECK ("Blocks" > 0 AND "RewardedThrough" > 0 AND "BusinessCharge" > 0 AND "BusinessCharge" = "CreatorEarning" + "PlatformEarning"),
    CONSTRAINT "FK_ViewRewardReceipts_CreatorPromotionParticipations_Participa~" FOREIGN KEY ("ParticipationId") REFERENCES v3."CreatorPromotionParticipations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ViewRewardReceipts_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_PromotionViewVerifications_ParticipationId" ON v3."PromotionViewVerifications" ("ParticipationId");

CREATE INDEX "IX_CommercePermissions_SubjectId_Role_IsActive" ON v3."CommercePermissions" ("SubjectId", "Role", "IsActive");

CREATE UNIQUE INDEX "IX_CreatorPromotionParticipations_CreatorAllocationId" ON v3."CreatorPromotionParticipations" ("CreatorAllocationId");

CREATE INDEX "IX_CreatorPromotionParticipations_CreatorAllocationId_Promotio~" ON v3."CreatorPromotionParticipations" ("CreatorAllocationId", "PromotionId", "CreatorId");

CREATE UNIQUE INDEX "IX_CreatorPromotionParticipations_Provider_ExternalContentId" ON v3."CreatorPromotionParticipations" ("Provider", "ExternalContentId");

CREATE INDEX "IX_OfferQrSessions_CreatorAllocationId_PromotionId_CreatorId" ON v3."OfferQrSessions" ("CreatorAllocationId", "PromotionId", "CreatorId");

CREATE INDEX "IX_OfferQrSessions_CustomerId_IssuedAtUtc" ON v3."OfferQrSessions" ("CustomerId", "IssuedAtUtc");

CREATE UNIQUE INDEX "IX_OfferQrSessions_SaleId" ON v3."OfferQrSessions" ("SaleId");

CREATE UNIQUE INDEX "IX_OfferQrSessions_TokenHash" ON v3."OfferQrSessions" ("TokenHash");

CREATE INDEX "IX_PayoutRecords_ConfigurationVersionId" ON v3."PayoutRecords" ("ConfigurationVersionId");

CREATE UNIQUE INDEX "IX_PayoutRecords_CreatorId" ON v3."PayoutRecords" ("CreatorId") WHERE "Status"='Eligible' AND "CreatorId" IS NOT NULL;

CREATE UNIQUE INDEX "IX_PayoutRecords_CustomerId" ON v3."PayoutRecords" ("CustomerId") WHERE "Status"='Eligible' AND "CustomerId" IS NOT NULL;

CREATE INDEX "IX_PayoutRecords_JournalId" ON v3."PayoutRecords" ("JournalId");

CREATE UNIQUE INDEX "IX_PayoutRecords_Reference" ON v3."PayoutRecords" ("Reference") WHERE "Reference" IS NOT NULL;

CREATE INDEX "IX_ViewRewardReceipts_JournalId" ON v3."ViewRewardReceipts" ("JournalId");

CREATE UNIQUE INDEX "IX_ViewRewardReceipts_ParticipationId_RewardedThrough" ON v3."ViewRewardReceipts" ("ParticipationId", "RewardedThrough");

ALTER TABLE v3."PromotionViewVerifications" ADD CONSTRAINT "FK_PromotionViewVerifications_CreatorPromotionParticipations_P~" FOREIGN KEY ("ParticipationId") REFERENCES v3."CreatorPromotionParticipations" ("Id") ON DELETE RESTRICT;

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

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260911233032_AddViewRewardsQrAndPayouts', '10.0.0');

COMMIT;
