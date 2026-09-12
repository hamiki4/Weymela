CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'v3') THEN
        CREATE SCHEMA v3;
    END IF;
END $EF$;

CREATE TABLE v3."AuditEvents" (
    "Id" uuid NOT NULL,
    "EventType" text NOT NULL,
    "ActorId" uuid NOT NULL,
    "BusinessId" uuid,
    "PromotionId" uuid,
    "CreatorId" uuid,
    "CorrelationId" uuid NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "Detail" text NOT NULL,
    CONSTRAINT "PK_AuditEvents" PRIMARY KEY ("Id")
);

CREATE TABLE v3."BusinessWallets" (
    "Id" uuid NOT NULL,
    "Version" bigint NOT NULL,
    "BusinessId" uuid NOT NULL,
    "AvailableBalance" numeric(18,2) NOT NULL,
    "ReservedBalance" numeric(18,2) NOT NULL,
    "TotalBalance" numeric(18,2) GENERATED ALWAYS AS ("AvailableBalance" + "ReservedBalance") STORED NOT NULL,
    CONSTRAINT "PK_BusinessWallets" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_BusinessWallets_BusinessId" UNIQUE ("BusinessId"),
    CONSTRAINT "CK_Wallet_NonNegative" CHECK ("AvailableBalance" >= 0 AND "ReservedBalance" >= 0)
);

CREATE TABLE v3."CreatorEarningsAccounts" (
    "CreatorId" uuid NOT NULL,
    "AvailableEarnings" numeric(18,2) NOT NULL,
    CONSTRAINT "PK_CreatorEarningsAccounts" PRIMARY KEY ("CreatorId"),
    CONSTRAINT "CK_CreatorEarnings_NonNegative" CHECK ("AvailableEarnings" >= 0)
);

CREATE TABLE v3."CustomerCashbackAccounts" (
    "CustomerId" uuid NOT NULL,
    "AvailableCashback" numeric(18,2) NOT NULL,
    CONSTRAINT "PK_CustomerCashbackAccounts" PRIMARY KEY ("CustomerId"),
    CONSTRAINT "CK_Cashback_NonNegative" CHECK ("AvailableCashback" >= 0)
);

CREATE TABLE v3."FinancialConfigurations" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    CONSTRAINT "PK_FinancialConfigurations" PRIMARY KEY ("Id")
);

CREATE TABLE v3."IdempotencyRecords" (
    "ActorId" uuid NOT NULL,
    "OperationType" character varying(100) NOT NULL,
    "Key" character varying(200) NOT NULL,
    "RequestFingerprint" text NOT NULL,
    "ResultReference" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("ActorId", "OperationType", "Key")
);

CREATE TABLE v3."LegalDocumentVersions" (
    "Id" uuid NOT NULL,
    "Type" character varying(64) NOT NULL,
    "Version" text NOT NULL,
    "ContentHash" text NOT NULL,
    "EffectiveFromUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LegalDocumentVersions" PRIMARY KEY ("Id")
);

CREATE TABLE v3."OutboxMessages" (
    "Id" uuid NOT NULL,
    "EventType" text NOT NULL,
    "Payload" jsonb NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "ProcessedAtUtc" timestamp with time zone,
    "AttemptCount" integer NOT NULL,
    "LastError" text,
    CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id")
);

CREATE TABLE v3."Promotions" (
    "Id" uuid NOT NULL,
    "PublicPromotionId" text NOT NULL,
    "BusinessId" uuid NOT NULL,
    "Title" text NOT NULL,
    "Description" text NOT NULL,
    "PromotionType" character varying(64) NOT NULL,
    "TotalBudget" numeric(18,2) NOT NULL,
    "ReservedBudget" numeric(18,2) NOT NULL,
    "UsedBudget" numeric(18,2) NOT NULL,
    "Eligibility_Category" text,
    "Eligibility_MinimumVerifiedFollowers" bigint,
    "Eligibility_Market" text,
    "Eligibility_Requirements" text,
    "StartDateUtc" timestamp with time zone NOT NULL,
    "EndDateUtc" timestamp with time zone NOT NULL,
    "Status" character varying(64) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone,
    "ActivatedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "Version" bigint NOT NULL,
    "AllocatedBudget" numeric(18,2) NOT NULL,
    "RemainingBudget" numeric(18,2) GENERATED ALWAYS AS ("TotalBudget" - "UsedBudget") STORED NOT NULL,
    CONSTRAINT "PK_Promotions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Promotion_Budgets" CHECK ("TotalBudget" > 0 AND "ReservedBudget" >= 0 AND "UsedBudget" >= 0 AND "ReservedBudget" + "UsedBudget" <= "TotalBudget" AND "AllocatedBudget" >= "UsedBudget" AND "AllocatedBudget" <= "TotalBudget"),
    CONSTRAINT "CK_Promotion_Dates" CHECK ("EndDateUtc" > "StartDateUtc"),
    CONSTRAINT "FK_Promotions_BusinessWallets_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES v3."BusinessWallets" ("BusinessId") ON DELETE RESTRICT
);

CREATE TABLE v3."FinancialConfigurationVersions" (
    "Id" uuid NOT NULL,
    "ConfigurationId" uuid NOT NULL,
    "Version" integer NOT NULL,
    "ChangedBy" uuid NOT NULL,
    "EffectiveFromUtc" timestamp with time zone NOT NULL,
    "ViewOnly_PromotionType" character varying(64) NOT NULL,
    "ViewOnly_ViewsPerReward" integer NOT NULL,
    "ViewOnly_BusinessCharge" numeric(18,2) NOT NULL,
    "ViewOnly_CreatorEarning" numeric(18,2) NOT NULL,
    "ViewOnly_PlatformEarning" numeric(18,2) NOT NULL,
    "ViewOnly_CreatorCommissionPercent" numeric(9,4) NOT NULL,
    "ViewOnly_CustomerCashbackPercent" numeric(9,4) NOT NULL,
    "ViewOnly_PlatformPercent" numeric(9,4) NOT NULL,
    "ViewOnly_EffectiveFromUtc" timestamp with time zone NOT NULL,
    "ViewOnly_ConfigurationVersionId" uuid NOT NULL,
    "ViewOnly_MinimumPromotionBudget" numeric(18,2),
    "ViewPlusCommission_PromotionType" character varying(64) NOT NULL,
    "ViewPlusCommission_ViewsPerReward" integer NOT NULL,
    "ViewPlusCommission_BusinessCharge" numeric(18,2) NOT NULL,
    "ViewPlusCommission_CreatorEarning" numeric(18,2) NOT NULL,
    "ViewPlusCommission_PlatformEarning" numeric(18,2) NOT NULL,
    "ViewPlusCommission_CreatorCommissionPercent" numeric(9,4) NOT NULL,
    "ViewPlusCommission_CustomerCashbackPercent" numeric(9,4) NOT NULL,
    "ViewPlusCommission_PlatformPercent" numeric(9,4) NOT NULL,
    "ViewPlusCommission_EffectiveFromUtc" timestamp with time zone NOT NULL,
    "ViewPlusCommission_ConfigurationVersionId" uuid NOT NULL,
    "ViewPlusCommission_MinimumPromotionBudget" numeric(18,2),
    "CreatorPayoutThreshold" numeric(18,2) NOT NULL,
    "CustomerPayoutThreshold" numeric(18,2) NOT NULL,
    CONSTRAINT "PK_FinancialConfigurationVersions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_FinancialConfigurationVersions_FinancialConfigurations_Conf~" FOREIGN KEY ("ConfigurationId") REFERENCES v3."FinancialConfigurations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."LegalAcceptances" (
    "UserId" uuid NOT NULL,
    "Role" character varying(64) NOT NULL,
    "DocumentVersionId" uuid NOT NULL,
    "AcceptedAtUtc" timestamp with time zone NOT NULL,
    "IpReference" text,
    "UserAgentReference" text,
    CONSTRAINT "PK_LegalAcceptances" PRIMARY KEY ("UserId", "Role", "DocumentVersionId"),
    CONSTRAINT "FK_LegalAcceptances_LegalDocumentVersions_DocumentVersionId" FOREIGN KEY ("DocumentVersionId") REFERENCES v3."LegalDocumentVersions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."CreatorAllocations" (
    "Id" uuid NOT NULL,
    "Version" bigint NOT NULL,
    "PromotionId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "OriginalAllocation" numeric(18,2) NOT NULL,
    "UsedAmount" numeric(18,2) NOT NULL,
    "Status" character varying(64) NOT NULL,
    "ApprovedAtUtc" timestamp with time zone NOT NULL,
    "ActivatedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "RemainingAmount" numeric(18,2) GENERATED ALWAYS AS ("OriginalAllocation" - "UsedAmount") STORED NOT NULL,
    CONSTRAINT "PK_CreatorAllocations" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_CreatorAllocations_Id_PromotionId_CreatorId" UNIQUE ("Id", "PromotionId", "CreatorId"),
    CONSTRAINT "CK_Allocation_Budgets" CHECK ("OriginalAllocation" > 0 AND "UsedAmount" >= 0 AND "UsedAmount" <= "OriginalAllocation"),
    CONSTRAINT "FK_CreatorAllocations_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."CreatorApplications" (
    "Id" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "Message" text NOT NULL,
    "ContentConcept" text,
    "Status" character varying(64) NOT NULL,
    "AppliedAtUtc" timestamp with time zone NOT NULL,
    "ReviewedAtUtc" timestamp with time zone,
    "ReviewedByBusinessUserId" uuid,
    CONSTRAINT "PK_CreatorApplications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CreatorApplications_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."FinancialJournals" (
    "Id" uuid NOT NULL,
    "Reference" text NOT NULL,
    "CorrelationId" uuid NOT NULL,
    "ActorId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "SourceType" character varying(64) NOT NULL,
    "IdempotencyReference" text,
    "IsPosted" boolean NOT NULL,
    "BusinessId" uuid,
    "CreatorId" uuid,
    "CustomerId" uuid,
    "PromotionId" uuid,
    CONSTRAINT "PK_FinancialJournals" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Journal_Posted" CHECK ("IsPosted"),
    CONSTRAINT "FK_FinancialJournals_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."PricingSnapshots" (
    "PromotionId" uuid NOT NULL,
    "PromotionType" character varying(64) NOT NULL,
    "ViewsPerReward" integer NOT NULL,
    "BusinessCharge" numeric(18,2) NOT NULL,
    "CreatorEarning" numeric(18,2) NOT NULL,
    "PlatformEarning" numeric(18,2) NOT NULL,
    "CreatorCommissionPercent" numeric(9,4) NOT NULL,
    "CustomerCashbackPercent" numeric(9,4) NOT NULL,
    "PlatformPercent" numeric(9,4) NOT NULL,
    "EffectiveFromUtc" timestamp with time zone NOT NULL,
    "ConfigurationVersionId" uuid NOT NULL,
    "MinimumPromotionBudget" numeric(18,2),
    CONSTRAINT "PK_PricingSnapshots" PRIMARY KEY ("PromotionId"),
    CONSTRAINT "FK_PricingSnapshots_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE CASCADE
);

CREATE TABLE v3."PromotionViewVerifications" (
    "Id" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "CreatorAllocationId" uuid NOT NULL,
    "ExternalPlatform" text NOT NULL,
    "ExternalContentId" text NOT NULL,
    "PreviousVerifiedViews" bigint NOT NULL,
    "CurrentVerifiedViews" bigint NOT NULL,
    "RewardedViewCount" bigint NOT NULL,
    "VerifiedAtUtc" timestamp with time zone NOT NULL,
    "EvidenceReference" text NOT NULL,
    "IdempotencyKey" text NOT NULL,
    CONSTRAINT "PK_PromotionViewVerifications" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Views_Valid" CHECK ("PreviousVerifiedViews" >= 0 AND "CurrentVerifiedViews" >= "PreviousVerifiedViews" AND "RewardedViewCount" >= 0),
    CONSTRAINT "FK_PromotionViewVerifications_CreatorAllocations_CreatorAlloca~" FOREIGN KEY ("CreatorAllocationId", "PromotionId", "CreatorId") REFERENCES v3."CreatorAllocations" ("Id", "PromotionId", "CreatorId") ON DELETE RESTRICT
);

CREATE TABLE v3."CreatorEarningEntries" (
    "Id" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "PromotionId" uuid,
    "Source" character varying(64) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CorrelationId" uuid NOT NULL,
    "JournalId" uuid NOT NULL,
    CONSTRAINT "PK_CreatorEarningEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CreatorEarning_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "FK_CreatorEarningEntries_CreatorEarningsAccounts_CreatorId" FOREIGN KEY ("CreatorId") REFERENCES v3."CreatorEarningsAccounts" ("CreatorId") ON DELETE RESTRICT,
    CONSTRAINT "FK_CreatorEarningEntries_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CreatorEarningEntries_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."FinancialJournalLines" (
    "Id" uuid NOT NULL,
    "Type" character varying(64) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Account" text NOT NULL,
    "JournalId" uuid NOT NULL,
    CONSTRAINT "PK_FinancialJournalLines" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_JournalLine_Positive" CHECK ("Amount" > 0 AND "Type" IN ('Debit', 'Credit')),
    CONSTRAINT "FK_FinancialJournalLines_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."PlatformRevenueEntries" (
    "Id" uuid NOT NULL,
    "PromotionId" uuid,
    "Source" character varying(64) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Status" character varying(64) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CorrelationId" uuid NOT NULL,
    "JournalId" uuid NOT NULL,
    CONSTRAINT "PK_PlatformRevenueEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Revenue_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "FK_PlatformRevenueEntries_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PlatformRevenueEntries_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."PlatformSettlements" (
    "Id" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "SettledAtUtc" timestamp with time zone NOT NULL,
    "Reference" text NOT NULL,
    "JournalId" uuid NOT NULL,
    CONSTRAINT "PK_PlatformSettlements" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Settlement_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "FK_PlatformSettlements_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."PromotionBudgetEntries" (
    "Id" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "AllocationId" uuid,
    "Amount" numeric(18,2) NOT NULL,
    "Movement" text NOT NULL,
    "JournalId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PromotionBudgetEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_BudgetEntry_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "FK_PromotionBudgetEntries_CreatorAllocations_AllocationId" FOREIGN KEY ("AllocationId") REFERENCES v3."CreatorAllocations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PromotionBudgetEntries_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PromotionBudgetEntries_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."PromotionReservations" (
    "PromotionId" uuid NOT NULL,
    "BusinessId" uuid NOT NULL,
    "OriginalAmount" numeric(18,2) NOT NULL,
    "JournalId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PromotionReservations" PRIMARY KEY ("PromotionId"),
    CONSTRAINT "CK_Reservation_Positive" CHECK ("OriginalAmount" > 0),
    CONSTRAINT "FK_PromotionReservations_BusinessWallets_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES v3."BusinessWallets" ("BusinessId") ON DELETE RESTRICT,
    CONSTRAINT "FK_PromotionReservations_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PromotionReservations_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES v3."Promotions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."VerifiedSales" (
    "Id" uuid NOT NULL,
    "PromotionId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "CreatorAllocationId" uuid NOT NULL,
    "BusinessId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "CashierId" uuid NOT NULL,
    "PurchaseAmount" numeric(18,2) NOT NULL,
    "CreatorCommissionAmount" numeric(18,2) NOT NULL,
    "CustomerCashbackAmount" numeric(18,2) NOT NULL,
    "PlatformRevenueAmount" numeric(18,2) NOT NULL,
    "TotalPromotionCharge" numeric(18,2) NOT NULL,
    "QrTokenReference" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "Status" character varying(64) NOT NULL,
    "IdempotencyKey" text NOT NULL,
    "JournalId" uuid NOT NULL,
    CONSTRAINT "PK_VerifiedSales" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Sale_Amounts" CHECK ("PurchaseAmount" > 0 AND "CreatorCommissionAmount" >= 0 AND "CustomerCashbackAmount" >= 0 AND "PlatformRevenueAmount" >= 0 AND "TotalPromotionCharge" = "CreatorCommissionAmount" + "CustomerCashbackAmount" + "PlatformRevenueAmount"),
    CONSTRAINT "FK_VerifiedSales_CreatorAllocations_CreatorAllocationId_Promot~" FOREIGN KEY ("CreatorAllocationId", "PromotionId", "CreatorId") REFERENCES v3."CreatorAllocations" ("Id", "PromotionId", "CreatorId") ON DELETE RESTRICT,
    CONSTRAINT "FK_VerifiedSales_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."WalletEntries" (
    "Id" uuid NOT NULL,
    "BusinessId" uuid NOT NULL,
    "PromotionId" uuid,
    "Amount" numeric(18,2) NOT NULL,
    "Movement" text NOT NULL,
    "JournalId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WalletEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_WalletEntry_Positive" CHECK ("Amount" > 0),
    CONSTRAINT "FK_WalletEntries_BusinessWallets_BusinessId" FOREIGN KEY ("BusinessId") REFERENCES v3."BusinessWallets" ("BusinessId") ON DELETE RESTRICT,
    CONSTRAINT "FK_WalletEntries_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT
);

CREATE TABLE v3."CustomerCashbackEntries" (
    "Id" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "VerifiedSaleId" uuid,
    "Source" character varying(64) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "CorrelationId" uuid NOT NULL,
    "JournalId" uuid NOT NULL,
    CONSTRAINT "PK_CustomerCashbackEntries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Cashback_PositiveSale" CHECK ("Amount" > 0 AND ("Source" <> 'VerifiedSale' OR "VerifiedSaleId" IS NOT NULL)),
    CONSTRAINT "FK_CustomerCashbackEntries_CustomerCashbackAccounts_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES v3."CustomerCashbackAccounts" ("CustomerId") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerCashbackEntries_FinancialJournals_JournalId" FOREIGN KEY ("JournalId") REFERENCES v3."FinancialJournals" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerCashbackEntries_VerifiedSales_VerifiedSaleId" FOREIGN KEY ("VerifiedSaleId") REFERENCES v3."VerifiedSales" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AuditEvents_CorrelationId" ON v3."AuditEvents" ("CorrelationId");

CREATE INDEX "IX_AuditEvents_PromotionId_OccurredAtUtc" ON v3."AuditEvents" ("PromotionId", "OccurredAtUtc");

CREATE UNIQUE INDEX "IX_CreatorAllocations_PromotionId_CreatorId" ON v3."CreatorAllocations" ("PromotionId", "CreatorId");

CREATE UNIQUE INDEX "IX_CreatorApplications_PromotionId_CreatorId" ON v3."CreatorApplications" ("PromotionId", "CreatorId") WHERE "Status" IN ('Pending', 'Approved');

CREATE INDEX "IX_CreatorApplications_PromotionId_Status" ON v3."CreatorApplications" ("PromotionId", "Status");

CREATE INDEX "IX_CreatorEarningEntries_CreatorId" ON v3."CreatorEarningEntries" ("CreatorId");

CREATE INDEX "IX_CreatorEarningEntries_JournalId" ON v3."CreatorEarningEntries" ("JournalId");

CREATE INDEX "IX_CreatorEarningEntries_PromotionId" ON v3."CreatorEarningEntries" ("PromotionId");

CREATE INDEX "IX_CustomerCashbackEntries_CustomerId" ON v3."CustomerCashbackEntries" ("CustomerId");

CREATE INDEX "IX_CustomerCashbackEntries_JournalId" ON v3."CustomerCashbackEntries" ("JournalId");

CREATE INDEX "IX_CustomerCashbackEntries_VerifiedSaleId" ON v3."CustomerCashbackEntries" ("VerifiedSaleId");

CREATE UNIQUE INDEX "IX_FinancialConfigurations_Name" ON v3."FinancialConfigurations" ("Name");

CREATE UNIQUE INDEX "IX_FinancialConfigurationVersions_ConfigurationId_Version" ON v3."FinancialConfigurationVersions" ("ConfigurationId", "Version");

CREATE INDEX "IX_FinancialConfigurationVersions_EffectiveFromUtc_Version" ON v3."FinancialConfigurationVersions" ("EffectiveFromUtc", "Version");

CREATE INDEX "IX_FinancialJournalLines_Account" ON v3."FinancialJournalLines" ("Account");

CREATE INDEX "IX_FinancialJournalLines_JournalId" ON v3."FinancialJournalLines" ("JournalId");

CREATE INDEX "IX_FinancialJournals_BusinessId" ON v3."FinancialJournals" ("BusinessId");

CREATE INDEX "IX_FinancialJournals_CorrelationId" ON v3."FinancialJournals" ("CorrelationId");

CREATE INDEX "IX_FinancialJournals_CreatedAtUtc" ON v3."FinancialJournals" ("CreatedAtUtc");

CREATE INDEX "IX_FinancialJournals_CreatorId" ON v3."FinancialJournals" ("CreatorId");

CREATE INDEX "IX_FinancialJournals_CustomerId" ON v3."FinancialJournals" ("CustomerId");

CREATE INDEX "IX_FinancialJournals_IdempotencyReference" ON v3."FinancialJournals" ("IdempotencyReference");

CREATE INDEX "IX_FinancialJournals_PromotionId" ON v3."FinancialJournals" ("PromotionId");

CREATE UNIQUE INDEX "IX_FinancialJournals_Reference" ON v3."FinancialJournals" ("Reference");

CREATE INDEX "IX_FinancialJournals_SourceType" ON v3."FinancialJournals" ("SourceType");

CREATE INDEX "IX_LegalAcceptances_DocumentVersionId" ON v3."LegalAcceptances" ("DocumentVersionId");

CREATE INDEX "IX_LegalDocumentVersions_Type_EffectiveFromUtc" ON v3."LegalDocumentVersions" ("Type", "EffectiveFromUtc");

CREATE UNIQUE INDEX "IX_LegalDocumentVersions_Type_Version" ON v3."LegalDocumentVersions" ("Type", "Version");

CREATE INDEX "IX_OutboxMessages_OccurredAtUtc" ON v3."OutboxMessages" ("OccurredAtUtc") WHERE "ProcessedAtUtc" IS NULL;

CREATE INDEX "IX_PlatformRevenueEntries_JournalId" ON v3."PlatformRevenueEntries" ("JournalId");

CREATE INDEX "IX_PlatformRevenueEntries_PromotionId" ON v3."PlatformRevenueEntries" ("PromotionId");

CREATE INDEX "IX_PlatformRevenueEntries_Source_CreatedAtUtc" ON v3."PlatformRevenueEntries" ("Source", "CreatedAtUtc");

CREATE INDEX "IX_PlatformSettlements_JournalId" ON v3."PlatformSettlements" ("JournalId");

CREATE UNIQUE INDEX "IX_PlatformSettlements_Reference" ON v3."PlatformSettlements" ("Reference");

CREATE INDEX "IX_PromotionBudgetEntries_AllocationId" ON v3."PromotionBudgetEntries" ("AllocationId");

CREATE INDEX "IX_PromotionBudgetEntries_JournalId" ON v3."PromotionBudgetEntries" ("JournalId");

CREATE INDEX "IX_PromotionBudgetEntries_PromotionId_CreatedAtUtc" ON v3."PromotionBudgetEntries" ("PromotionId", "CreatedAtUtc");

CREATE INDEX "IX_PromotionReservations_BusinessId" ON v3."PromotionReservations" ("BusinessId");

CREATE INDEX "IX_PromotionReservations_JournalId" ON v3."PromotionReservations" ("JournalId");

CREATE INDEX "IX_Promotions_BusinessId_Status" ON v3."Promotions" ("BusinessId", "Status");

CREATE UNIQUE INDEX "IX_Promotions_PublicPromotionId" ON v3."Promotions" ("PublicPromotionId");

CREATE INDEX "IX_PromotionViewVerifications_CreatorAllocationId_PromotionId_~" ON v3."PromotionViewVerifications" ("CreatorAllocationId", "PromotionId", "CreatorId");

CREATE UNIQUE INDEX "IX_PromotionViewVerifications_IdempotencyKey" ON v3."PromotionViewVerifications" ("IdempotencyKey");

CREATE UNIQUE INDEX "IX_VerifiedSales_BusinessId_IdempotencyKey" ON v3."VerifiedSales" ("BusinessId", "IdempotencyKey");

CREATE INDEX "IX_VerifiedSales_CreatorAllocationId_PromotionId_CreatorId" ON v3."VerifiedSales" ("CreatorAllocationId", "PromotionId", "CreatorId");

CREATE INDEX "IX_VerifiedSales_JournalId" ON v3."VerifiedSales" ("JournalId");

CREATE INDEX "IX_WalletEntries_BusinessId_CreatedAtUtc" ON v3."WalletEntries" ("BusinessId", "CreatedAtUtc");

CREATE INDEX "IX_WalletEntries_JournalId" ON v3."WalletEntries" ("JournalId");

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

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260911225904_InitialV3Schema', '10.0.0');

COMMIT;
