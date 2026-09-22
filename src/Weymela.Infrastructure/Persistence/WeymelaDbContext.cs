using Microsoft.EntityFrameworkCore;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence.Configurations;
using Weymela.Infrastructure.Persistence.Records;

namespace Weymela.Infrastructure.Persistence;

public sealed class WeymelaDbContext(DbContextOptions<WeymelaDbContext> options) : DbContext(options)
{
    public DbSet<BusinessWallet> BusinessWallets => Set<BusinessWallet>();
    public DbSet<WalletEntry> WalletEntries => Set<WalletEntry>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionPlatform> PromotionPlatforms => Set<PromotionPlatform>();
    public DbSet<UgcOpportunity> UgcOpportunities => Set<UgcOpportunity>();
    public DbSet<UgcPlatformRequirement> UgcPlatformRequirements => Set<UgcPlatformRequirement>();
    public DbSet<UgcRevision> UgcRevisions => Set<UgcRevision>();
    public DbSet<UgcCreatorRequest> UgcCreatorRequests => Set<UgcCreatorRequest>();
    public DbSet<UgcAssignment> UgcAssignments => Set<UgcAssignment>();
    public DbSet<UgcSubmission> UgcSubmissions => Set<UgcSubmission>();
    public DbSet<PromotionReservation> PromotionReservations => Set<PromotionReservation>();
    public DbSet<PromotionBudgetEntry> PromotionBudgetEntries => Set<PromotionBudgetEntry>();
    public DbSet<UgcReservation> UgcReservations => Set<UgcReservation>();
    public DbSet<UgcBudgetEntry> UgcBudgetEntries => Set<UgcBudgetEntry>();
    public DbSet<UgcCustomerOffer> UgcCustomerOffers => Set<UgcCustomerOffer>();
    public DbSet<UgcCustomerOfferSale> UgcCustomerOfferSales => Set<UgcCustomerOfferSale>();
    public DbSet<UgcCustomerOfferReservation> UgcCustomerOfferReservations => Set<UgcCustomerOfferReservation>();
    public DbSet<UgcCustomerOfferBudgetEntry> UgcCustomerOfferBudgetEntries => Set<UgcCustomerOfferBudgetEntry>();
    public DbSet<CreatorApplication> CreatorApplications => Set<CreatorApplication>();
    public DbSet<CreatorAllocation> CreatorAllocations => Set<CreatorAllocation>();
    public DbSet<CreatorPromotionContentSubmission> CreatorPromotionContentSubmissions => Set<CreatorPromotionContentSubmission>();
    public DbSet<PromotionViewVerification> PromotionViewVerifications => Set<PromotionViewVerification>();
    public DbSet<VerifiedSale> VerifiedSales => Set<VerifiedSale>();
    public DbSet<CreatorEarningsAccount> CreatorEarningsAccounts => Set<CreatorEarningsAccount>();
    public DbSet<CreatorEarningEntry> CreatorEarningEntries => Set<CreatorEarningEntry>();
    public DbSet<CustomerCashbackAccount> CustomerCashbackAccounts => Set<CustomerCashbackAccount>();
    public DbSet<CustomerCashbackEntry> CustomerCashbackEntries => Set<CustomerCashbackEntry>();
    public DbSet<CustomerProfileRecord> CustomerProfiles => Set<CustomerProfileRecord>();
    public DbSet<PlatformRevenueEntry> PlatformRevenueEntries => Set<PlatformRevenueEntry>();
    public DbSet<PlatformSettlement> PlatformSettlements => Set<PlatformSettlement>();
    public DbSet<FinancialConfiguration> FinancialConfigurations => Set<FinancialConfiguration>();
    public DbSet<FinancialConfigurationVersion> FinancialConfigurationVersions => Set<FinancialConfigurationVersion>();
    public DbSet<FinancialJournal> FinancialJournals => Set<FinancialJournal>();
    public DbSet<FinancialJournalLine> FinancialJournalLines => Set<FinancialJournalLine>();
    public DbSet<LegalDocumentVersion> LegalDocumentVersions => Set<LegalDocumentVersion>();
    public DbSet<LegalAcceptance> LegalAcceptances => Set<LegalAcceptance>();
    public DbSet<StoredIdempotencyRecord> IdempotencyRecords => Set<StoredIdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CreatorPromotionParticipation> CreatorPromotionParticipations => Set<CreatorPromotionParticipation>();
    public DbSet<ViewRewardReceipt> ViewRewardReceipts => Set<ViewRewardReceipt>();
    public DbSet<OfferQrSession> OfferQrSessions => Set<OfferQrSession>();
    public DbSet<PayoutRecord> PayoutRecords => Set<PayoutRecord>();
    public DbSet<CommercePermission> CommercePermissions => Set<CommercePermission>();
    public DbSet<IdentityBinding> IdentityBindings => Set<IdentityBinding>();
    public DbSet<PublicWorkspaceProfile> PublicWorkspaceProfiles => Set<PublicWorkspaceProfile>();
    public DbSet<DepositRequest> DepositRequests => Set<DepositRequest>();
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();
    public DbSet<WorkerCheckpoint> WorkerCheckpoints => Set<WorkerCheckpoint>();
    public DbSet<AuthIdentifierRecord> AuthIdentifiers => Set<AuthIdentifierRecord>();
    public DbSet<EmailAuthChallengeRecord> EmailAuthChallenges => Set<EmailAuthChallengeRecord>();
    public DbSet<PasswordCredentialRecord> PasswordCredentials => Set<PasswordCredentialRecord>();
    public DbSet<AuthorizedDeviceRecord> AuthorizedDevices => Set<AuthorizedDeviceRecord>();
    public DbSet<DeviceSessionRecord> DeviceSessions => Set<DeviceSessionRecord>();
    public DbSet<RoleEnrollmentRecord> RoleEnrollments => Set<RoleEnrollmentRecord>();
    public DbSet<ProductHandoffTransaction> ProductHandoffTransactions => Set<ProductHandoffTransaction>();
    public DbSet<CreatorSocialProfileRecord> CreatorSocialProfiles => Set<CreatorSocialProfileRecord>();
    public DbSet<AdminGrantRecord> AdminGrants => Set<AdminGrantRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("v3");
        model.ApplyConfigurationsFromAssembly(typeof(WeymelaDbContext).Assembly);
        EarningsConfiguration.Configure(model);
        OperationalConfiguration.Configure(model);
        Phase4Configuration.Configure(model);
        OperationalReadinessConfiguration.Configure(model);
        // Relationships never cascade-delete audit/accounting history.
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ValidateChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void ValidateChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var e in ChangeTracker.Entries().ToArray())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (IsImmutable(e.Entity) && e.State != EntityState.Added)
                throw new InvalidOperationException("Posted accounting, snapshots and audit records are append-only.");
            if (e.Entity is CreatorPromotionContentSubmission && e.State != EntityState.Added)
            {
                if (e.State == EntityState.Deleted || new[] { "Id", "CreatorAllocationId", "RevisionNumber", "Provider", "ContentReference", "SubmittedAtUtc" }
                    .Any(name => !Equals(e.Property(name).OriginalValue, e.Property(name).CurrentValue)))
                    throw new InvalidOperationException("Submitted Promotion content revisions are immutable; only review metadata may change.");
            }
            foreach (var p in e.Properties)
            {
                if (p.CurrentValue is Money m && (m.Currency != "ETB" || decimal.Round(m.Amount, 2) != m.Amount || m.Amount > 9999999999999999.99m))
                    throw new InvalidOperationException("Persisted ETB money requires at most two decimal places and numeric(18,2) range.");
                if (p.Metadata.GetPrecision() == 9 && p.CurrentValue is decimal percent &&
                    (percent < 0 || percent > 100 || decimal.Round(percent, 4) != percent))
                    throw new InvalidOperationException("Rates require at most four decimal places between zero and 100.");
            }
            if (e.Entity is FinancialJournal j)
            {
                if (!j.IsPosted || j.Lines.Count < 2 || j.Lines.Sum(l => l.Type == JournalLineType.Debit ? l.Amount.Amount : -l.Amount.Amount) != 0)
                    throw new InvalidOperationException("Only balanced posted journals may be persisted.");
            }
            if (e.Entity is Promotion promotion)
                e.Property("StoredAllocatedBudget").CurrentValue = promotion.AllocatedBudget.Amount;
            if (e.State == EntityState.Modified && e.Metadata.FindProperty("Version") is not null)
            {
                var version = e.Property("Version");
                version.CurrentValue = Math.Max((long)version.CurrentValue!, (long)version.OriginalValue! + 1);
            }
        }
    }

    private static bool IsImmutable(object entity) => entity is FinancialJournal or FinancialJournalLine or
        WalletEntry or PromotionReservation or PromotionBudgetEntry or UgcReservation or UgcBudgetEntry or UgcCustomerOfferReservation or UgcCustomerOfferBudgetEntry or UgcRevision or StoredIdempotencyRecord or AuditEvent or
        PricingSnapshot or FinancialConfigurationVersion or LegalDocumentVersion or LegalAcceptance or
        CreatorEarningEntry or CustomerCashbackEntry or PlatformRevenueEntry or PlatformSettlement or VerifiedSale or UgcCustomerOfferSale or PromotionViewVerification or ViewRewardReceipt;
}
