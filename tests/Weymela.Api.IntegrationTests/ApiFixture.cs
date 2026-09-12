using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weymela.Api;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Tests;
using Xunit;

namespace Weymela.Api.IntegrationTests;

[CollectionDefinition("V3 HTTP PostgreSQL")]
public sealed class ApiCollection:ICollectionFixture<PostgresFixture>;

public sealed class ApiFixture: IAsyncDisposable
{
    public required WebApplication App{get;init;}
    public required TestDatabase Database{get;init;}
    public required string AccessKey{get;init;}
    public static async Task<ApiFixture> CreateAsync(PostgresFixture postgres, Action<WebApplicationBuilder>? configure = null)
    {
        var database=await postgres.CreateAsync();var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var app=ApiHost.Build(["--environment","Development"],builder=>
        {
            builder.WebHost.UseTestServer();builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ConnectionStrings:WeymelaV3"]=database.ConnectionString,["V3:EnableDevelopmentIdentity"]="true",["V3:DevelopmentAccessKey"]=key});
            configure?.Invoke(builder);
        });
        await using(var scope=app.Services.CreateAsyncScope())
            await DevelopmentWorkspaceSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<WeymelaDbContext>(),scope.ServiceProvider.GetRequiredService<DevelopmentDirectory>(),scope.ServiceProvider.GetRequiredService<DevelopmentViewProvider>(),TimeProvider.System);
        await app.StartAsync();return new(){App=app,Database=database,AccessKey=key};
    }
    public HttpClient Anonymous()
    {
        var client=App.GetTestClient();client.DefaultRequestHeaders.Add("X-Weymela-Request","1");return client;
    }
    public async Task<HttpClient> Login(string alias)
    {
        var client=Anonymous();var response=await client.PostAsJsonAsync("/api/development/session",new{alias,accessKey=AccessKey});response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie",response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);return client;
    }
    public async ValueTask DisposeAsync()=>await App.DisposeAsync();
}
internal static class JsonRequests
{
    public static async Task<JsonNode> GetJson(this HttpClient c,string path)
    {
        var response=await c.GetAsync(path);var body=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,$"GET {path}: {(int)response.StatusCode}: {body}");return JsonNode.Parse(body)!;
    }
    public static async Task<HttpResponseMessage> Post(this HttpClient c,string path,object body,string? key=null)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return await c.SendAsync(request);
    }
    public static async Task<JsonNode> PostJson(this HttpClient c,string path,object body,string? key=null)
    {
        var response=await c.Post(path,body,key);var text=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,$"POST {path}: {(int)response.StatusCode}: {text}");return JsonNode.Parse(text)!;
    }
}
