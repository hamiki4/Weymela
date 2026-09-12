using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Weymela.Api;
using Xunit;

namespace Weymela.Api.IntegrationTests;

public sealed class AuthBoundaryTests
{
    [Fact] public void Development_identity_cannot_be_enabled_in_a_non_development_host()
    {
        Assert.Throws<InvalidOperationException>(()=>ApiHost.Build(["--environment","Acceptance"],b=>b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["V3:EnableDevelopmentIdentity"]="true"})));
    }
    [Fact] public void Development_identity_requires_an_explicit_access_key()
    {
        Assert.Throws<InvalidOperationException>(()=>ApiHost.Build(["--environment","Development"],b=>b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
            ["V3:EnableDevelopmentIdentity"]="true",["ConnectionStrings:WeymelaV3"]="Host=127.0.0.1;Port=65432;Database=v3_test_guard;Username=unconfigured"})));
    }
    [Fact] public void Non_V3_database_names_are_rejected_without_opening_a_connection()
    {
        Assert.Throws<InvalidOperationException>(()=>ApiHost.Build(["--environment","Acceptance"],b=>b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
            ["ConnectionStrings:WeymelaV3"]="Host=127.0.0.1;Port=65432;Database=not_a_v3_database;Username=unconfigured"})));
    }
    [Fact] public async Task Non_development_host_has_no_test_personas_or_development_signin_route()
    {
        using var settings = new AcceptanceConfiguration();
        await using var app=ApiHost.Build(["--environment","Acceptance"],b=>{b.WebHost.UseTestServer();b.Logging.ClearProviders();settings.Apply(b.Configuration);});await app.StartAsync();using var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");client.DefaultRequestHeaders.Add("X-Weymela-Request","1");
        var mode=await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/auth/mode");Assert.False(mode!["development"]!.GetValue<bool>());Assert.Null(mode["personas"]);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync("/api/development/session",new{alias="admin",accessKey="not-enabled"})).StatusCode);
    }
}
