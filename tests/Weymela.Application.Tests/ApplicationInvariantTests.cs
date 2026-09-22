using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Weymela.Application;
using Weymela.Domain;
using Xunit;

namespace Weymela.Application.Tests;

public sealed class ApplicationInvariantTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact] public async Task Deposit_idempotency_replay_does_not_credit_twice()
    {
        var business = Guid.NewGuid(); var actor = new Actor(Guid.NewGuid(), ActorRole.Business, business); var wallet = new BusinessWallet(business);
        var repo = new WalletRepo(wallet); var idem = new IdemStore(); var service = new WalletApplicationService(repo, idem, new Events()); var command = new CreditBusinessDepositCommand(actor, new Money(250), "deposit-1", Now);
        await service.CreditDepositAsync(command, 0, CancellationToken.None); await service.CreditDepositAsync(command, 0, CancellationToken.None);
        Assert.Equal(250, wallet.TotalBalance.Amount); Assert.Single(idem.Records);
    }

    [Fact] public async Task Conflicting_idempotency_request_is_rejected()
    {
        var business = Guid.NewGuid(); var actor = new Actor(Guid.NewGuid(), ActorRole.Business, business); var wallet = new BusinessWallet(business); var idem = new IdemStore(); var service = new WalletApplicationService(new WalletRepo(wallet), idem, new Events());
        await service.CreditDepositAsync(new(actor, new Money(10), "same", Now), 0, CancellationToken.None);
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => service.CreditDepositAsync(new(actor, new Money(20), "same", Now), 0, CancellationToken.None));
        Assert.Equal(FailureKind.IdempotencyConflict, failure.Kind);
    }

    [Fact] public async Task Business_cannot_fund_another_business_campaign()
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var p = TestPromotion(other); var w = new BusinessWallet(owner); w.CreditDeposit(new Money(1000), Now, Guid.NewGuid());
        var service = new PromotionApplicationService(new PromotionRepo(p), new WalletRepo(w), new Pricing(), new IdemStore(), new Events(), new Legal());
        await Assert.ThrowsAsync<ApplicationFailure>(() => service.FundAsync(new(new Actor(Guid.NewGuid(), ActorRole.Business, owner), p.Id, 0, 0, "k", Now), CancellationToken.None));
    }

    [Fact] public async Task Funding_idempotency_replay_does_not_reserve_twice()
    {
        var business = Guid.NewGuid(); var actor = new Actor(Guid.NewGuid(), ActorRole.Business, business); var p = TestPromotion(business); var wallet = new BusinessWallet(business); wallet.CreditDeposit(new Money(1500), Now, Guid.NewGuid()); var idem = new IdemStore();
        var service = new PromotionApplicationService(new PromotionRepo(p), new WalletRepo(wallet), new Pricing(), idem, new Events(), new Legal()); var command = new FundPromotionCommand(actor, p.Id, 0, 0, "fund-1", Now);
        await service.FundAsync(command, CancellationToken.None); await service.FundAsync(command, CancellationToken.None);
        Assert.Equal(1000, wallet.ReservedBalance.Amount); Assert.Single(idem.Records);
    }

    [Fact] public async Task Creator_discovery_returns_only_eligible_funded_state_campaigns()
    {
        var eligible = TestPromotion(Guid.NewGuid()); var wallet = new BusinessWallet(eligible.BusinessId); wallet.CreditDeposit(new Money(1000), Now, Guid.NewGuid()); eligible.Fund(wallet, Now, Guid.NewGuid()); eligible.Publish(Now, Guid.NewGuid());
        var draft = TestPromotion(Guid.NewGuid()); var query = new PromotionQueryService(new PromotionRepo(eligible, draft), new Eligibility(true)); var actor = new Actor(Guid.NewGuid(), ActorRole.Creator, CreatorId: Guid.NewGuid());
        var result = await query.ForCreatorAsync(actor, new CreatorVerifiedProfile(null, null, 0, false), CancellationToken.None); Assert.Single(result); Assert.Equal(eligible.Id, result[0].Id);
    }

    [Fact] public async Task Creator_projection_does_not_expose_other_financial_data()
    {
        var names = typeof(CreatorPromotionProjection).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("Wallet", StringComparison.OrdinalIgnoreCase) || n.Contains("Platform", StringComparison.OrdinalIgnoreCase) || n.Contains("Customer", StringComparison.OrdinalIgnoreCase) || n.Contains("Other", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public void Admin_projection_contains_campaign_financial_oversight_contract() { var names = typeof(AdminPromotionProjection).GetProperties().Select(x => x.Name).ToHashSet(); Assert.Contains("TotalBudget", names); Assert.Contains("AllocatedBudget", names); Assert.Contains("UsedBudget", names); Assert.Contains("RemainingBudget", names); Assert.Contains("CreatorCount", names); }
    [Fact] public void Application_contracts_preserve_internal_names_for_clear_ui_mapping() { var names = typeof(BusinessPromotionProjection).GetProperties().Select(x => x.Name).ToHashSet(); Assert.Contains("TotalBudget", names); Assert.Contains("UnallocatedBudget", names); Assert.Contains("RemainingBudget", names); Assert.Contains("OwnBudget", typeof(CreatorPromotionProjection).GetProperties().Select(x => x.Name)); }
    [Fact] public async Task Legal_acceptance_is_version_specific_and_missing_gate_is_forbidden() { var v1 = new LegalDocumentVersion(Guid.NewGuid(), LegalDocumentType.BusinessAgreement, "1", "a", Now); var v2 = v1 with { Id = Guid.NewGuid(), Version = "2" }; var a = new LegalAcceptance(Guid.NewGuid(), LegalRole.Business, v1.Id, Now, null, null); Assert.Equal(v1.Id, a.DocumentVersionId); Assert.NotEqual(v1.Id, v2.Id); var gate = new Legal(); var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => gate.EnsureCurrentAcceptedAsync(a.UserId, LegalRole.Business, new[] { LegalDocumentType.BusinessAgreement }, CancellationToken.None)); Assert.Equal(FailureKind.Forbidden, failure.Kind); }

    private static Promotion TestPromotion(Guid businessId) => new(businessId, "Campaign", "", PromotionType.ViewOnly, new Money(1000), new(null, null, null, null), Now, Now.AddDays(1), new PricingSnapshot(PromotionType.ViewOnly, 1000, new Money(300), new Money(200), new Money(100), 0, 0, 0, Now, Guid.NewGuid()), Now, 30);
    private sealed class WalletRepo(BusinessWallet wallet) : IWalletRepository { public Task<BusinessWallet?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<BusinessWallet?>(id == wallet.BusinessId ? wallet : null); public Task SaveAsync(BusinessWallet w, long v, CancellationToken ct) => Task.CompletedTask; }
    private sealed class PromotionRepo(params Promotion[] values) : IPromotionRepository { private readonly List<Promotion> list = values.ToList(); public Task<Promotion?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(list.SingleOrDefault(x => x.Id == id)); public Task AddAsync(Promotion p, CancellationToken ct) { list.Add(p); return Task.CompletedTask; } public Task SaveAsync(Promotion p, long v, CancellationToken ct) => Task.CompletedTask; public Task<IReadOnlyList<Promotion>> QueryAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Promotion>>(list); }
    private sealed class IdemStore : IIdempotencyStore { public List<IdempotencyRecord> Records { get; } = []; public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct) => Task.FromResult(Records.SingleOrDefault(x => x.Key == key)); public Task SaveAsync(IdempotencyRecord record, CancellationToken ct) { Records.Add(record); return Task.CompletedTask; } }
    private sealed class Events : IEventPublisher { public Task PublishAsync(IReadOnlyCollection<DomainEvent> events, CancellationToken ct) => Task.CompletedTask; }
    private sealed class Pricing : IFinancialConfigurationResolver
    {
        public Task<PricingSnapshot> ResolveAsync(PromotionType type, DateTime at, CancellationToken ct) =>
            Task.FromResult(new PricingSnapshot(type, 1, new Money(1), new Money(1), new Money(0), 0, 0, 0, at, Guid.NewGuid()));
        public Task<PromotionConfigurationSnapshot> ResolvePromotionAsync(PromotionType type, DateTime at, CancellationToken ct) =>
            Task.FromResult(new PromotionConfigurationSnapshot(
                new PricingSnapshot(type, 1, new Money(1), new Money(1), new Money(0), 0, 0, 0, at, Guid.NewGuid()), 30));
    }
    private sealed class Legal : ILegalAcceptanceGate { public Task EnsureCurrentAcceptedAsync(Guid userId, LegalRole role, IReadOnlyCollection<LegalDocumentType> required, CancellationToken ct) => throw new ApplicationFailure(FailureKind.Forbidden, "Required legal acceptance is missing."); }
    private sealed class Eligibility(bool allowed) : ICreatorEligibility { public bool IsEligible(Guid creatorId, CreatorEligibilityCriteria criteria, CreatorVerifiedProfile profile) => allowed; }
}
