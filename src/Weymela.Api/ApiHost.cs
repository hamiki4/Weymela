using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Weymela.Api.Auth;
using Weymela.Api.Endpoints;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Infrastructure.Development;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weymela.Application.Operations;
using Weymela.Infrastructure.Deposits;
using Weymela.Infrastructure.Identity;
using Weymela.Infrastructure.Notifications;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Providers;
using Weymela.Api.Security;

namespace Weymela.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args,Action<WebApplicationBuilder>? configure=null)
    {
        var builder=WebApplication.CreateBuilder(args);configure?.Invoke(builder);
        var options=RuntimeOptions.Load(builder.Configuration,builder.Environment.EnvironmentName);
        var development=options.DevelopmentIdentity;
        builder.Services.AddSingleton(options);
        builder.WebHost.ConfigureKestrel(o=>{o.Limits.MaxRequestBodySize=RuntimeOptions.RequestBytes;o.Limits.MaxRequestHeaderCount=40;o.Limits.MaxRequestHeadersTotalSize=16384;o.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15);});
        builder.Logging.AddFilter("Microsoft.AspNetCore",LogLevel.Error).AddFilter("Microsoft.EntityFrameworkCore",LogLevel.Error);
        builder.Logging.AddJsonConsole(o=>{o.IncludeScopes=true;o.TimestampFormat="yyyy-MM-ddTHH:mm:ss.fffZ";o.UseUtcTimestamp=true;});
        builder.Services.ConfigureHttpJsonOptions(o=>{o.SerializerOptions.UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow;o.SerializerOptions.MaxDepth=16;});
        builder.Services.AddWeymelaPersistence(options.ConnectionString);
        builder.Services.AddScoped<WorkspaceQueries>();builder.Services.AddScoped<WorkspaceCommands>();
        builder.Services.AddScoped<NotificationService>();builder.Services.AddScoped<WorkerPump>();builder.Services.AddScoped<DepositService>();
        builder.Services.AddScoped<LegalWorkspaceService>();builder.Services.AddScoped<OperationalHealth>();builder.Services.AddScoped<ReconciliationService>();
        builder.Services.TryAddSingleton<INotificationPushProvider,DisabledPushProvider>();
        builder.Services.AddSingleton<IDepositProvider>(options.DepositMode=="ManualApproval"?new ManualApprovalDepositProvider():new DisabledDepositProvider());
        if(development)
        {
            builder.Services.AddSingleton<DevelopmentDirectory>();builder.Services.AddSingleton<IWorkspaceDirectory>(sp=>sp.GetRequiredService<DevelopmentDirectory>());
            builder.Services.AddSingleton<DevelopmentViewProvider>();builder.Services.AddSingleton<IVerifiedViewProvider>(sp=>sp.GetRequiredService<DevelopmentViewProvider>());
        }
        else
        {
            builder.Services.AddScoped<IWorkspaceDirectory,PersistentWorkspaceDirectory>();builder.Services.AddScoped<IVerifiedViewProvider,SocialProviderRouter>();
        }
        builder.Services.AddScoped<IPublicIdentityDirectory>(sp=>sp.GetRequiredService<IWorkspaceDirectory>());
        LiveAuthentication.Add(builder.Services,options);
        EndpointSecurity.AddLimits(builder.Services,options);
        builder.Services.AddCors(o=>o.AddPolicy("V3Origins",p=>{if(options.AllowedOrigins.Length>0)p.WithOrigins(options.AllowedOrigins).WithMethods("GET","POST","OPTIONS").WithHeaders("Content-Type","X-Weymela-Request","Idempotency-Key","X-Correlation-ID").WithExposedHeaders("Retry-After","X-Correlation-ID").AllowCredentials();}));
        if(options.TrustedProxies.Length>0)builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o=>
        {o.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor|Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;o.ForwardLimit=1;o.KnownIPNetworks.Clear();o.KnownProxies.Clear();foreach(var proxy in options.TrustedProxies)o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));});
        builder.Services.AddAuthorization(o=>
        {
            foreach(var role in Enum.GetValues<ActorRole>())o.AddPolicy(role.ToString(),p=>p.RequireAuthenticatedUser().RequireRole(role.ToString()).AddRequirements(new ActiveWorkspaceRequirement()));
            o.AddPolicy("Workspace",p=>p.RequireAuthenticatedUser().AddRequirements(new ActiveWorkspaceRequirement()));
            o.AddPolicy("Checkout",p=>p.RequireAuthenticatedUser().RequireRole(ActorRole.Business.ToString(),ActorRole.Cashier.ToString()).AddRequirements(new ActiveWorkspaceRequirement(checkout:true)));
        });
        builder.Services.AddScoped<IAuthorizationHandler,ActiveWorkspaceHandler>();
        var app=builder.Build();
        if(options.TrustedProxies.Length>0)app.UseForwardedHeaders();
        app.UseRouting();
        app.UseMiddleware<ApiSafetyMiddleware>();
        app.UseCors("V3Origins");app.UseAuthentication();app.UseRateLimiter();app.UseAuthorization();
        app.MapGet("/health",()=>Results.Ok(new{status="ok",phase=6})).AllowAnonymous();
        app.MapOperationalEndpoints();app.MapAuthEndpoints(development);app.MapBusinessEndpoints(development);app.MapCreatorEndpoints();app.MapAdminEndpoints();app.MapCommerceEndpoints();
        var webRoot=builder.Configuration["V3:WebRoot"];
        if(!string.IsNullOrEmpty(webRoot))
        {
            var files=new PhysicalFileProvider(Path.GetFullPath(webRoot));
            app.UseDefaultFiles(new DefaultFilesOptions{FileProvider=files});
            app.UseStaticFiles(new StaticFileOptions{FileProvider=files,OnPrepareResponse=c=>c.Context.Response.Headers.CacheControl=c.Context.Request.Path.StartsWithSegments("/assets")?"public,max-age=31536000,immutable":"no-cache"});
            app.MapFallback(async context=>{if(context.Request.Path.StartsWithSegments("/api")){context.Response.StatusCode=404;return;}context.Response.ContentType="text/html";context.Response.Headers.CacheControl="no-cache";await context.Response.SendFileAsync(files.GetFileInfo("index.html"));});
        }
        return app;
    }
}
