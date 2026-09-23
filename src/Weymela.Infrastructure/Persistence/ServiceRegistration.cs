using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weymela.Application;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Outbox;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Finance;
using Weymela.Infrastructure.Identity;

namespace Weymela.Infrastructure.Persistence;

public static class ServiceRegistration
{
    // The host must supply its own environment-scoped connection. No environment is auto-discovered.
    public static IServiceCollection AddWeymelaPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WeymelaDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IBusinessWalletRepository, BusinessWalletRepository>();
        services.AddScoped<IWalletRepository, BusinessWalletRepository>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<ICreatorApplicationRepository, CreatorApplicationRepository>();
        services.AddScoped<IAllocationRepository, CreatorAllocationRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IFinancialConfigurationResolver, FinancialConfigurationResolver>();
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        services.AddScoped<ILegalAcceptanceGate>(sp => new LegalAcceptanceGate(sp.GetRequiredService<WeymelaDbContext>(), TimeProvider.System));
        services.AddScoped<FinancialCommands>();
        services.AddScoped<FinancialJournalRepository>();
        services.AddScoped<CreatorEarningsRepository>();
        services.AddScoped<CustomerCashbackRepository>();
        services.AddScoped<PlatformRevenueRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICommerceAccessPolicy, CommerceAccessPolicy>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<PayoutService>();
        services.AddScoped<VerifiedViewService>();
        services.AddScoped<CreatorPromotionContentService>();
        services.AddScoped<FinancialQueries>();
        services.AddScoped<IAdminFinancialQueries>(sp => sp.GetRequiredService<FinancialQueries>());
        services.AddScoped<EmailAuthService>();
        services.AddScoped<PhoneAliasService>();
        services.AddScoped<CashierService>();
        services.AddScoped<PasswordCredentialService>();
        services.AddScoped<RoleEnrollmentService>();
        services.AddScoped<AccountLegalOnboardingService>();
        services.TryAddSingleton<IEmailCodeDelivery, DisabledEmailCodeDelivery>();
        services.TryAddSingleton<IFirebaseCustomTokenIssuer, DisabledFirebaseCustomTokenIssuer>();
        // Host must provide IVerifiedViewProvider and IPublicIdentityDirectory.
        // No live provider, permissive identity stub or external connection is registered here.
        return services;
    }
}
