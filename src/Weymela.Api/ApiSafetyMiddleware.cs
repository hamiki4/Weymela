using System.Text.RegularExpressions;
using Weymela.Application;
using Weymela.Api.Security;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api;

public sealed partial class ApiSafetyMiddleware(RequestDelegate next, RuntimeOptions options, ILogger<ApiSafetyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlation=Guid.TryParse(context.Request.Headers["X-Correlation-ID"],out var provided)?provided.ToString("N"):Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"]=correlation;
        using var scope=logger.BeginScope(new Dictionary<string,object>{{"CorrelationId",correlation}});
        context.Response.Headers["X-Content-Type-Options"]="nosniff";context.Response.Headers["Referrer-Policy"]="no-referrer";
        context.Response.Headers["Permissions-Policy"]="camera=(self), microphone=(), geolocation=(), payment=(), usb=()";
        context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        context.Response.Headers["X-Frame-Options"]="DENY";
        if(!options.Development&&context.Request.IsHttps)context.Response.Headers.StrictTransportSecurity="max-age=31536000";
        if(context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl="no-store";
            if(HttpMethods.IsPost(context.Request.Method)||HttpMethods.IsPut(context.Request.Method)||HttpMethods.IsPatch(context.Request.Method)||HttpMethods.IsDelete(context.Request.Method))
            {
                // Non-simple requests require the explicit configured origin (same-origin is also permitted in Development).
                var origin=context.Request.Headers.Origin.ToString();
                var ownOrigin=$"{context.Request.Scheme}://{context.Request.Host}";
                var allowed=origin.Length>0&&(options.Development&&origin==ownOrigin||options.AllowedOrigins.Contains(origin,StringComparer.Ordinal));
                if(context.Request.Headers["X-Weymela-Request"]!="1"||(origin.Length>0&&!allowed)||(context.Request.Headers["Sec-Fetch-Site"]=="cross-site"&&!allowed))
                {await Error(context,403,"Forbidden","Use the Weymela workspace to submit this action.");return;}
                if(!options.Development&&!context.Request.IsHttps){await Error(context,403,"SecureTransportRequired","A secure connection is required.");return;}
                if(context.Request.ContentLength>RuntimeOptions.RequestBytes){await Error(context,413,"RequestTooLarge","This request is too large.");return;}
                // Bound unknown-length/chunked bodies too, including non-Kestrel test hosts. This remains in memory, never on disk.
                if(context.Request.ContentLength is null)
                {
                    var payload=new MemoryStream();var buffer=new byte[4096];int read;
                    while((read=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0)
                    {
                        if(payload.Length+read>RuntimeOptions.RequestBytes){payload.Dispose();await Error(context,413,"RequestTooLarge","This request is too large.");return;}
                        payload.Write(buffer,0,read);
                    }
                    payload.Position=0;context.Request.Body=payload;context.Request.ContentLength=payload.Length;context.Response.RegisterForDispose(payload);
                }
                if(context.Request.ContentLength>0||context.Request.Headers.TransferEncoding.Count>0)
                {
                    var media=context.Request.ContentType?.Split(';')[0].Trim();
                    if(media!="application/json"){await Error(context,415,"UnsupportedContentType","Use a JSON request. File uploads are not enabled.");return;}
                }
                if(!options.FinancialWritesEnabled&&EndpointSecurity.Financial(context)){await Error(context,503,"FinancialWritesPaused","Financial actions are currently paused. No funds have moved.");return;}
            }
        }
        try { await next(context); }
        catch(ApplicationFailure e)
        {
            OperationalTelemetry.Failure(e.Kind,EndpointSecurity.Financial(context),EndpointSecurity.Category(context) is "qr" or "checkout");
            var status=e.Kind switch{FailureKind.Forbidden=>403,FailureKind.NotFound=>404,FailureKind.ConcurrencyConflict or FailureKind.IdempotencyConflict=>409,_=>400};
            await Error(context,status,e.Kind.ToString(),UserLanguage(e.Message));
        }
        catch(ArgumentException){await Error(context,400,"Validation","Check the entered amounts, pricing split and required fields.");}
        catch(InvalidOperationException){await Error(context,400,"Validation","That action is not available. Refresh the workspace and check the saved values.");}
        catch(BadHttpRequestException e){await Error(context,e.StatusCode==413?413:400,"Validation","Check the required fields and request size.");}
        catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested){context.Response.StatusCode=499;}
        catch(Exception){await Error(context,503,"Unavailable","The workspace could not complete this request. Please try again.");}
        finally {logger.LogInformation("Request completed {Operation} {StatusCode}",EndpointSecurity.Operation(context),context.Response.StatusCode);}
    }
    private static async Task Error(HttpContext c,int status,string code,string message)
    {if(c.Response.HasStarted)return;c.Response.StatusCode=status;await c.Response.WriteAsJsonAsync(new{code,message});}
    private static string UserLanguage(string message)=>DomainWords().Replace(message,m=>m.Value.ToLowerInvariant() switch{"promotion"=>"Campaign","promotions"=>"Campaigns","allocation"=>"Creator Budget","allocations"=>"Creator Budgets",_=>m.Value});
    [GeneratedRegex(@"\b(promotions?|allocations?)\b",RegexOptions.IgnoreCase)] private static partial Regex DomainWords();
}
