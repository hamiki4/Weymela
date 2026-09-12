using Weymela.Domain;
namespace Weymela.Application;
public sealed record BusinessPromotionProjection(Guid Id,string PublicId,string Title,PromotionType Type,Money TotalBudget,Money AllocatedBudget,Money UnallocatedBudget,Money UsedBudget,Money RemainingBudget,PromotionStatus Status,DateTime StartDateUtc,DateTime EndDateUtc,int CreatorCount);
public sealed record CreatorPromotionProjection(Guid Id,string PublicId,string Title,PromotionType Type,string? Requirements,string? Market,DateTime StartDateUtc,DateTime EndDateUtc,Money? OwnBudget,Money? OwnUsed,Money? OwnRemaining);
public sealed record AdminPromotionProjection(Guid Id,Guid BusinessId,string PublicId,string Title,PromotionType Type,Money TotalBudget,Money AllocatedBudget,Money UsedBudget,Money RemainingBudget,int CreatorCount,PromotionStatus Status,DateTime StartDateUtc,DateTime EndDateUtc);
public sealed class PromotionQueryService(IPromotionRepository promotions,ICreatorEligibility eligibility)
{
 public async Task<IReadOnlyList<BusinessPromotionProjection>> ForBusinessAsync(Actor actor,CancellationToken ct){Require(actor,ActorRole.Business);return(await promotions.QueryAsync(ct)).Where(x=>x.BusinessId==actor.BusinessId).Select(x=>new BusinessPromotionProjection(x.Id,x.PublicPromotionId,x.Title,x.PromotionType,x.TotalBudget,x.AllocatedBudget,x.UnallocatedBudget,x.UsedBudget,x.RemainingBudget,x.Status,x.StartDateUtc,x.EndDateUtc,x.Allocations.Count)).ToList();}
 public async Task<IReadOnlyList<CreatorPromotionProjection>> ForCreatorAsync(Actor actor,CreatorVerifiedProfile profile,CancellationToken ct){Require(actor,ActorRole.Creator);return(await promotions.QueryAsync(ct)).Where(x=>(x.Status is PromotionStatus.Published or PromotionStatus.Active)&&x.RemainingBudget.Amount>0&&eligibility.IsEligible(actor.CreatorId!.Value,x.Eligibility,profile)).Select(x=>new CreatorPromotionProjection(x.Id,x.PublicPromotionId,x.Title,x.PromotionType,x.Eligibility.Requirements,x.Eligibility.Market,x.StartDateUtc,x.EndDateUtc,null,null,null)).ToList();}
 private static void Require(Actor a,ActorRole role){if(a.Role!=role)throw new ApplicationFailure(FailureKind.Forbidden,"The actor is not authorized for this query.");}
}
