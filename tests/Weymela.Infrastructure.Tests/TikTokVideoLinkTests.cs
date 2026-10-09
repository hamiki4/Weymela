using Weymela.Infrastructure.Providers;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class TikTokVideoLinkTests
{
    [Fact]
    public void Normalizes_supported_canonical_video_links_without_fetching_them()
    {
        var normalized = TikTokVideoLink.Normalize("https://www.tiktok.com/@weymela.creator/video/7412345678901234567/");

        Assert.Equal("https://www.tiktok.com/@weymela.creator/video/7412345678901234567", normalized);
        Assert.Equal("7412345678901234567", TikTokVideoLink.VideoId(normalized));
    }

    [Fact]
    public void Accepts_supported_shared_links_for_review_but_does_not_treat_the_token_as_a_verified_video_id()
    {
        var normalized = TikTokVideoLink.Normalize("https://vm.tiktok.com/ZMabcdef/");

        Assert.Equal("https://vm.tiktok.com/ZMabcdef", normalized);
        Assert.Null(TikTokVideoLink.VideoId(normalized));
    }

    [Theory]
    [InlineData("http://www.tiktok.com/@creator/video/7412345678901234567")]
    [InlineData("https://example.com/@creator/video/7412345678901234567")]
    [InlineData("https://www.tiktok.com/@creator/video/123")]
    [InlineData("https://www.tiktok.com/@creator/video/7412345678901234567?redirect=https://example.com")]
    [InlineData("https://www.tiktok.com/@creator/video/7412345678901234567#fragment")]
    public void Rejects_unsafe_or_unsupported_links(string value)
    {
        Assert.False(TikTokVideoLink.TryNormalize(value, out _));
    }
}
