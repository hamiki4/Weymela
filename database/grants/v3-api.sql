\set ON_ERROR_STOP on

-- Required psql variables:
--   api_role      V3 API login role (for example: weymela_v3_api)
--   database_name V3 database name (for example: weymela_v3_pilot)
-- Apply only as the database owner/migration identity after all V3 migrations.

BEGIN;

REVOKE CONNECT, TEMPORARY ON DATABASE :"database_name" FROM PUBLIC;
REVOKE ALL PRIVILEGES ON DATABASE :"database_name" FROM :"api_role";
GRANT CONNECT ON DATABASE :"database_name" TO :"api_role";

REVOKE CREATE, USAGE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE, USAGE ON SCHEMA v3 FROM PUBLIC;
REVOKE ALL PRIVILEGES ON SCHEMA public FROM :"api_role";
REVOKE ALL PRIVILEGES ON SCHEMA v3 FROM :"api_role";
GRANT USAGE ON SCHEMA public, v3 TO :"api_role";

REVOKE ALL PRIVILEGES ON TABLE public."__EFMigrationsHistory" FROM :"api_role";
GRANT SELECT ON TABLE public."__EFMigrationsHistory" TO :"api_role";

REVOKE ALL PRIVILEGES ON TABLE
    v3."AuditEvents",
    v3."AuthIdentifiers",
    v3."AuthorizedDevices",
    v3."AdminGrants",
    v3."AccountLifecycles",
    v3."AccountPreauthorizations",
    v3."AccountRoleHistory",
    v3."BusinessWallets",
    v3."CommercePermissions",
    v3."CreatorAllocations",
    v3."CreatorApplications",
    v3."CreatorEarningEntries",
    v3."CreatorEarningsAccounts",
    v3."CashierPreauthorizations",
    v3."CreatorPromotionContentSubmissions",
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
    v3."PlatformPromotionalFundings",
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
    v3."RetiredSupportSessions",
    v3."UgcAssignments",
    v3."UgcBudgetEntries",
    v3."UgcCustomerOfferBudgetEntries",
    v3."UgcCustomerOfferReservations",
    v3."UgcCustomerOfferSales",
    v3."UgcCustomerOffers",
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
FROM :"api_role";

GRANT SELECT, INSERT, UPDATE ON TABLE v3."AuthIdentifiers" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."EmailAuthChallenges" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."PasswordCredentials" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."IdentityBindings" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."CommercePermissions" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."PublicWorkspaceProfiles" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."RoleEnrollments" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."BusinessWallets" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."CustomerCashbackAccounts" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."CustomerProfiles" TO :"api_role";
GRANT SELECT ON TABLE v3."LegalDocumentVersions" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."LegalAcceptances" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."AuthorizedDevices" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."DeviceSessions" TO :"api_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."ProductHandoffTransactions" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."IdempotencyRecords" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."AuditEvents" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."OutboxMessages" TO :"api_role";
GRANT SELECT, INSERT ON TABLE v3."InAppNotifications" TO :"api_role";
GRANT UPDATE ("ReadAtUtc", "Version") ON TABLE v3."InAppNotifications" TO :"api_role";

GRANT SELECT, INSERT, UPDATE ON TABLE
    v3."AccountLifecycles",
    v3."AccountPreauthorizations"
TO :"api_role";

GRANT SELECT, INSERT ON TABLE v3."AccountRoleHistory" TO :"api_role";

GRANT SELECT, INSERT, UPDATE ON TABLE
    v3."AdminGrants",
    v3."CashierPreauthorizations",
    v3."CreatorApplications",
    v3."CreatorAllocations",
    v3."CreatorEarningsAccounts",
    v3."CreatorPromotionContentSubmissions",
    v3."CreatorPromotionParticipations",
    v3."CreatorSocialProfiles",
    v3."DepositRequests",
    v3."PayoutRecords",
    v3."PromotionPlatforms",
    v3."Promotions",
    v3."UgcAssignments",
    v3."UgcCustomerOffers",
    v3."UgcCreatorRequests",
    v3."UgcOpportunities",
    v3."UgcSubmissions"
TO :"api_role";

GRANT SELECT, INSERT ON TABLE
    v3."CreatorEarningEntries",
    v3."CustomerCashbackEntries",
    v3."FinancialConfigurationVersions",
    v3."FinancialJournals",
    v3."FinancialJournalLines",
    v3."PlatformPromotionalFundings",
    v3."PlatformRevenueEntries",
    v3."PlatformSettlements",
    v3."PricingSnapshots",
    v3."PromotionBudgetEntries",
    v3."PromotionReservations",
    v3."VerifiedSales",
    v3."ViewRewardReceipts",
    v3."WalletEntries",
    v3."UgcBudgetEntries",
    v3."UgcCustomerOfferBudgetEntries",
    v3."UgcCustomerOfferReservations",
    v3."UgcCustomerOfferSales",
    v3."UgcPlatformRequirements",
    v3."UgcReservations",
    v3."UgcRevisions"
TO :"api_role";

GRANT SELECT, INSERT, UPDATE ON TABLE v3."OfferQrSessions" TO :"api_role";

GRANT SELECT ON TABLE
    v3."WorkerCheckpoints"
TO :"api_role";

-- Migration-created routines default to PUBLIC EXECUTE in PostgreSQL. Remove that
-- ambient capability for every V3 routine before restoring only the reviewed API set.
REVOKE EXECUTE ON FUNCTION v3.reject_history_change() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_balanced_journal() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_journal_line_insert() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_verified_sale() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_campaign_allocations() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_allocation_change() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_wallet_journal() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_platform_settlement() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_cashback_sale() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_platform_revenue_journal() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_participation_baseline() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_qr_use() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_sale_snapshot_amounts() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_payout_transition() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_earned_account() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_payout_journal() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_deposit_review() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_approved_deposit_journal() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_notification_identity() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_outbox_envelope() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_creator_promotion_content_revision() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_ugc_customer_offer() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_ugc_customer_offer_sale() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_ugc_customer_offer_projection() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.guard_platform_promotional_funding_account() FROM PUBLIC, :"api_role";
REVOKE EXECUTE ON FUNCTION v3.check_admin_promotional_funding() FROM PUBLIC, :"api_role";

GRANT EXECUTE ON FUNCTION v3.check_wallet_journal() TO :"api_role";
GRANT EXECUTE ON FUNCTION v3.check_earned_account() TO :"api_role";
GRANT EXECUTE ON FUNCTION v3.guard_notification_identity() TO :"api_role";

COMMIT;
