\set ON_ERROR_STOP on

-- Required psql variables: backup_role, database_name.
-- Apply after approved migrations with the reviewed maintenance identity that
-- can grant on both this database and the migration-owned tables. Stop if it
-- lacks authority; do not add role membership or transfer ownership to work
-- around a grant failure. Backup is read-only; restore remains separate.
BEGIN;

REVOKE ALL PRIVILEGES ON DATABASE :"database_name" FROM :"backup_role";
GRANT CONNECT ON DATABASE :"database_name" TO :"backup_role";
REVOKE ALL PRIVILEGES ON SCHEMA public, v3 FROM :"backup_role";
GRANT USAGE ON SCHEMA public, v3 TO :"backup_role";

REVOKE ALL PRIVILEGES ON TABLE public."__EFMigrationsHistory" FROM :"backup_role";
GRANT SELECT ON TABLE public."__EFMigrationsHistory" TO :"backup_role";

-- Explicit inventory of all persisted tables in the nine approved migrations.
-- A complete disaster-recovery archive must include auth, device, legal and finance.
REVOKE ALL PRIVILEGES ON TABLE
    v3."AuditEvents",
    v3."AuthIdentifiers",
    v3."AuthorizedDevices",
    v3."AdminGrants",
    v3."BusinessWallets",
    v3."CommercePermissions",
    v3."CreatorAllocations",
    v3."CreatorApplications",
    v3."CreatorEarningEntries",
    v3."CreatorEarningsAccounts",
    v3."CreatorPromotionParticipations",
    v3."CreatorSocialProfiles",
    v3."CustomerCashbackAccounts",
    v3."CustomerCashbackEntries",
    v3."CustomerProfiles",
    v3."DepositRequests",
    v3."DeviceSessions",
    v3."EmailAuthChallenges",
    v3."FinancialConfigurations",
    v3."FinancialConfigurationVersions",
    v3."FinancialJournalLines",
    v3."FinancialJournals",
    v3."IdempotencyRecords",
    v3."IdentityBindings",
    v3."InAppNotifications",
    v3."LegalAcceptances",
    v3."LegalDocumentVersions",
    v3."OfferQrSessions",
    v3."OutboxMessages",
    v3."PasswordCredentials",
    v3."PayoutRecords",
    v3."PlatformRevenueEntries",
    v3."PlatformSettlements",
    v3."PricingSnapshots",
    v3."ProductHandoffTransactions",
    v3."PromotionBudgetEntries",
    v3."PromotionReservations",
    v3."PromotionPlatforms",
    v3."PromotionViewVerifications",
    v3."Promotions",
    v3."PublicWorkspaceProfiles",
    v3."RoleEnrollments",
    v3."UgcAssignments",
    v3."UgcBudgetEntries",
    v3."UgcCreatorRequests",
    v3."UgcOpportunities",
    v3."UgcPlatformRequirements",
    v3."UgcReservations",
    v3."UgcRevisions",
    v3."UgcSubmissions",
    v3."VerifiedSales",
    v3."ViewRewardReceipts",
    v3."WalletEntries",
    v3."WorkerCheckpoints"
FROM :"backup_role";

GRANT SELECT ON TABLE
    v3."AuditEvents",
    v3."AuthIdentifiers",
    v3."AuthorizedDevices",
    v3."AdminGrants",
    v3."BusinessWallets",
    v3."CommercePermissions",
    v3."CreatorAllocations",
    v3."CreatorApplications",
    v3."CreatorEarningEntries",
    v3."CreatorEarningsAccounts",
    v3."CreatorPromotionParticipations",
    v3."CreatorSocialProfiles",
    v3."CustomerCashbackAccounts",
    v3."CustomerCashbackEntries",
    v3."CustomerProfiles",
    v3."DepositRequests",
    v3."DeviceSessions",
    v3."EmailAuthChallenges",
    v3."FinancialConfigurations",
    v3."FinancialConfigurationVersions",
    v3."FinancialJournalLines",
    v3."FinancialJournals",
    v3."IdempotencyRecords",
    v3."IdentityBindings",
    v3."InAppNotifications",
    v3."LegalAcceptances",
    v3."LegalDocumentVersions",
    v3."OfferQrSessions",
    v3."OutboxMessages",
    v3."PasswordCredentials",
    v3."PayoutRecords",
    v3."PlatformRevenueEntries",
    v3."PlatformSettlements",
    v3."PricingSnapshots",
    v3."ProductHandoffTransactions",
    v3."PromotionBudgetEntries",
    v3."PromotionReservations",
    v3."PromotionPlatforms",
    v3."PromotionViewVerifications",
    v3."Promotions",
    v3."PublicWorkspaceProfiles",
    v3."RoleEnrollments",
    v3."UgcAssignments",
    v3."UgcBudgetEntries",
    v3."UgcCreatorRequests",
    v3."UgcOpportunities",
    v3."UgcPlatformRequirements",
    v3."UgcReservations",
    v3."UgcRevisions",
    v3."UgcSubmissions",
    v3."VerifiedSales",
    v3."ViewRewardReceipts",
    v3."WalletEntries",
    v3."WorkerCheckpoints"
TO :"backup_role";

COMMIT;
