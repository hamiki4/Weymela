using System.Net;
using Weymela.Application;
using Weymela.Infrastructure.Providers;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class TikTokLinkResolverTests
{
    private const string Canonical = "https://www.tiktok.com/@creator/video/7412345678901234567";
    [Fact] public async Task Resolves_mobile_share_redirects_and_strips_query_without_fetching_canonical_post()
    {
        var handler = new Links(request => request.RequestUri!.Host == "vm.tiktok.com"
            ? Redirect("https://www.tiktok.com/t/ZMabcdef?share=1") : Redirect(Canonical + "?share=1"));
        var actual = await new TikTokLinkResolver(new HttpClient(handler)).ResolveAsync("https://vm.tiktok.com/ZMabcdef?share=1", default);
        Assert.Equal(Canonical, actual); Assert.Equal(2, handler.Urls.Count);
        Assert.All(handler.Urls, uri => Assert.Empty(uri.Query));
        Assert.DoesNotContain(handler.Urls, uri => uri.AbsolutePath.Contains("/video/"));
    }
    [Theory]
    [InlineData("http://www.tiktok.com/t/ZMabcdef")]
    [InlineData("https://127.0.0.1/t/ZMabcdef")]
    [InlineData("https://www.tiktok.com.evil.test/t/ZMabcdef")]
    [InlineData("https://www.tiktok.com:8443/t/ZMabcdef")]
    [InlineData("https://user@www.tiktok.com/t/ZMabcdef")]
    public async Task Rejects_unsafe_redirect_before_second_fetch(string target)
    {
        var handler = new Links(_ => Redirect(target));
        var failure = await Assert.ThrowsAsync<ApplicationFailure>(() => new TikTokLinkResolver(new HttpClient(handler)).ResolveAsync("https://vm.tiktok.com/ZMabcdef", default));
        Assert.Equal("UnsafeTikTokRedirect", failure.Code); Assert.Single(handler.Urls);
    }
    [Theory]
    [InlineData(403, "PostInaccessible")]
    [InlineData(404, "PostInaccessible")]
    [InlineData(200, "PostInaccessible")]
    [InlineData(503, "ProviderUnavailable")]
    public async Task Distinguishes_inaccessible_post_from_provider_failure(int status, string code)
    {
        var handler = new Links(_ => new((HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ApplicationFailure>(() => new TikTokLinkResolver(new HttpClient(handler)).ResolveAsync("https://vm.tiktok.com/ZMabcdef", default));
        Assert.Equal(code, error.Code);
    }
    [Fact] public async Task Redirect_loops_are_bounded()
    {
        var handler = new Links(_ => Redirect("https://vm.tiktok.com/ZMabcdef"));
        await Assert.ThrowsAsync<ApplicationFailure>(() => new TikTokLinkResolver(new HttpClient(handler)).ResolveAsync("https://vm.tiktok.com/ZMabcdef", default));
        Assert.Equal(6, handler.Urls.Count);
    }
    [Theory]
    [InlineData("127.0.0.1")][InlineData("10.1.2.3")][InlineData("169.254.169.254")]
    [InlineData("192.168.1.1")][InlineData("100.64.0.1")][InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")][InlineData("2002:7f00:1::")][InlineData("2001:db8::1")]
    public void Rejects_non_public_addresses(string address) => Assert.False(TikTokLinkResolver.PublicAddress(IPAddress.Parse(address)));
    private static HttpResponseMessage Redirect(string url) { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new(url); return response; }
    private sealed class Links(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Urls.Add(request.RequestUri!); return Task.FromResult(respond(request)); }
    }
}
