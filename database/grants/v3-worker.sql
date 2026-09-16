\set ON_ERROR_STOP on

-- Required psql variables:
--   worker_role   V3 Worker login role (for example: weymela_v3_worker)
--   database_name V3 database name (for example: weymela_v3_pilot)
-- Apply only as the database owner/migration identity after all V3 migrations.

BEGIN;

REVOKE CONNECT, TEMPORARY ON DATABASE :"database_name" FROM PUBLIC;
REVOKE ALL PRIVILEGES ON DATABASE :"database_name" FROM :"worker_role";
GRANT CONNECT ON DATABASE :"database_name" TO :"worker_role";

REVOKE CREATE, USAGE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE, USAGE ON SCHEMA v3 FROM PUBLIC;
REVOKE ALL PRIVILEGES ON SCHEMA public FROM :"worker_role";
REVOKE ALL PRIVILEGES ON SCHEMA v3 FROM :"worker_role";
GRANT USAGE ON SCHEMA v3 TO :"worker_role";

REVOKE ALL PRIVILEGES ON TABLE public."__EFMigrationsHistory" FROM :"worker_role";
REVOKE ALL PRIVILEGES ON TABLE
    v3."AuditEvents",
    v3."AuthIdentifiers",
    v3."AuthorizedDevices",
    v3."BusinessWallets",
    v3."CommercePermissions",
    v3."CreatorAllocations",
    v3."CreatorApplications",
    v3."CreatorEarningEntries",
    v3."CreatorEarningsAccounts",
    v3."CreatorPromotionParticipations",
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
    v3."PromotionBudgetEntries",
    v3."PromotionReservations",
    v3."PromotionViewVerifications",
    v3."Promotions",
    v3."PublicWorkspaceProfiles",
    v3."RoleEnrollments",
    v3."VerifiedSales",
    v3."ViewRewardReceipts",
    v3."WalletEntries",
    v3."WorkerCheckpoints"
FROM :"worker_role";

GRANT SELECT, INSERT, UPDATE ON TABLE v3."WorkerCheckpoints" TO :"worker_role";
GRANT SELECT ON TABLE v3."FinancialConfigurationVersions" TO :"worker_role";
GRANT SELECT, INSERT, UPDATE ON TABLE v3."OutboxMessages" TO :"worker_role";
GRANT SELECT ON TABLE v3."CommercePermissions" TO :"worker_role";
GRANT SELECT, INSERT ON TABLE v3."InAppNotifications" TO :"worker_role";
GRANT UPDATE ("PushState", "PushAttempts", "NextPushAtUtc", "LastPushErrorCode", "Version")
    ON TABLE v3."InAppNotifications" TO :"worker_role";
GRANT SELECT ON TABLE v3."OfferQrSessions" TO :"worker_role";
GRANT UPDATE ("Status", "Version") ON TABLE v3."OfferQrSessions" TO :"worker_role";
GRANT SELECT ON TABLE
    v3."Promotions",
    v3."PricingSnapshots",
    v3."CreatorAllocations",
    v3."PayoutRecords",
    v3."DepositRequests"
TO :"worker_role";

-- Keep this script independently safe if it is applied before the API script.
REVOKE EXECUTE ON FUNCTION v3.reject_history_change() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_balanced_journal() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_journal_line_insert() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_verified_sale() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_campaign_allocations() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_allocation_change() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_wallet_journal() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_platform_settlement() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_cashback_sale() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_platform_revenue_journal() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_participation_baseline() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_qr_use() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_sale_snapshot_amounts() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_payout_transition() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_earned_account() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_payout_journal() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_deposit_review() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.check_approved_deposit_journal() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_notification_identity() FROM PUBLIC, :"worker_role";
REVOKE EXECUTE ON FUNCTION v3.guard_outbox_envelope() FROM PUBLIC, :"worker_role";

GRANT EXECUTE ON FUNCTION v3.guard_outbox_envelope() TO :"worker_role";
GRANT EXECUTE ON FUNCTION v3.guard_notification_identity() TO :"worker_role";
GRANT EXECUTE ON FUNCTION v3.guard_qr_use() TO :"worker_role";

COMMIT;
