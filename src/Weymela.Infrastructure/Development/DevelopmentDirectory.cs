using Weymela.Application;
using Weymela.Application.Web;

namespace Weymela.Infrastructure.Development;

// Explicit local fixtures only. Production hosts replace this directory with a trusted adapter.
public sealed class DevelopmentDirectory : IWorkspaceDirectory
{
    public sealed record Persona(string Alias,string Name,string PublicId,Actor Actor);
    public static Guid Id(int number)=>Guid.Parse($"00000000-0000-4000-8000-{number:D12}");
    public IReadOnlyList<Persona> Personas { get; } =
    [
        new("admin","Weymela Admin","ADMIN-01",new(Id(1),ActorRole.PlatformAdmin)),
        new("business","Abc Coffee","BUS-100",new(Id(2),ActorRole.Business,Id(100))),
        new("other-business","Bole Studio","BUS-200",new(Id(3),ActorRole.Business,Id(200))),
        new("creator","Bella","CR-100",new(Id(4),ActorRole.Creator,CreatorId:Id(300))),
        new("other-creator","Elias","CR-200",new(Id(5),ActorRole.Creator,CreatorId:Id(400))),
        new("ineligible-creator","Mika","CR-300",new(Id(6),ActorRole.Creator,CreatorId:Id(500))),
        new("customer","Hana","CU-100",new(Id(7),ActorRole.Customer,CustomerId:Id(600))),
        new("other-customer","Teddy","CU-200",new(Id(8),ActorRole.Customer,CustomerId:Id(700))),
        new("cashier","Abc Checkout","POS-100",new(Id(9),ActorRole.Cashier,Id(100))),
        new("other-cashier","Bole Checkout","POS-200",new(Id(10),ActorRole.Cashier,Id(200))),
        new("operations-admin","Weymela Operations","ADMIN-02",new(Id(11),ActorRole.OperationsAdmin))
    ];
    public Persona Get(string alias)=>Personas.Single(x=>x.Alias==alias);
    public Task<BusinessCard> BusinessCardAsync(Guid id,CancellationToken ct)=>Task.FromResult(id==Id(100)
        ?new BusinessCard(id,"Abc Coffee","Addis Ababa","https://www.google.com/maps/search/?api=1&query=Addis+Ababa")
        :id==Id(200)?new BusinessCard(id,"Bole Studio","Addis Ababa","https://www.google.com/maps/search/?api=1&query=Bole+Addis+Ababa"):throw new ApplicationFailure(FailureKind.NotFound,"Business profile not found."));
    public Task<CreatorCard> CreatorCardAsync(Guid id,CancellationToken ct)=>Task.FromResult(id==Id(300)
        ?new CreatorCard(id,"Bella","CR-100","Addis Ababa","Food",42000,186000,true,null)
        :id==Id(400)?new CreatorCard(id,"Elias","CR-200","Addis Ababa","Food",29000,121000,true,null)
        :id==Id(500)?new CreatorCard(id,"Mika","CR-300","Hawassa","Fashion",800,5200,false,null):throw new ApplicationFailure(FailureKind.NotFound,"Creator profile not found."));
    public Task<CustomerCard> CustomerCardAsync(Guid id,CancellationToken ct)
    {
        var p=Personas.SingleOrDefault(x=>x.Actor.CustomerId==id)??throw new ApplicationFailure(FailureKind.NotFound,"Customer profile not found.");
        return Task.FromResult(new CustomerCard(id,p.Name,p.PublicId));
    }
    public async Task<PublicBusiness> BusinessAsync(Guid id,CancellationToken ct)=>new(id,(await BusinessCardAsync(id,ct)).DisplayName);
    public async Task<PublicCreator> CreatorAsync(Guid id,CancellationToken ct){var c=await CreatorCardAsync(id,ct);return new(id,c.PublicId,c.DisplayName);}
}

public sealed class UnavailableWorkspaceDirectory : IWorkspaceDirectory
{
    private static ApplicationFailure Unavailable()=>new(FailureKind.Validation,"The public-profile connection is not configured.");
    public Task<BusinessCard> BusinessCardAsync(Guid id,CancellationToken ct)=>throw Unavailable();
    public Task<CreatorCard> CreatorCardAsync(Guid id,CancellationToken ct)=>throw Unavailable();
    public Task<CustomerCard> CustomerCardAsync(Guid id,CancellationToken ct)=>throw Unavailable();
    public Task<PublicBusiness> BusinessAsync(Guid id,CancellationToken ct)=>throw Unavailable();
    public Task<PublicCreator> CreatorAsync(Guid id,CancellationToken ct)=>throw Unavailable();
}
