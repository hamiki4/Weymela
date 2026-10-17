using Weymela.Application;
using Weymela.Infrastructure.Providers;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class SocialVideoLinkTests
{
    [Theory]
    [InlineData("TikTok", "https://m.tiktok.com/@creator/video/7412345678901234567?share=1", "7412345678901234567", false)]
    [InlineData("TikTok", "https://m.tiktok.com/v/7412345678901234567.html", "7412345678901234567", false)]
    [InlineData("TikTok", "https://www.tiktok.com/player/v1/7412345678901234567", "7412345678901234567", false)]
    [InlineData("TikTok", "https://vm.tiktok.com/ZMabcdef/", "ZMabcdef", true)]
    [InlineData("YouTube", "https://youtu.be/dQw4w9WgXcQ?t=5", "dQw4w9WgXcQ", false)]
    [InlineData("YouTube", "https://m.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ", false)]
    [InlineData("Instagram", "https://www.instagram.com/reel/ABCdef_123/?igsh=123", "ABCdef_123", false)]
    [InlineData("Instagram", "https://www.instagram.com/share/reel/ABCdef_123/", "ABCdef_123", true)]
    [InlineData("Facebook", "https://m.facebook.com/watch/?v=12345678901234", "12345678901234", false)]
    [InlineData("Facebook", "https://www.facebook.com/creator/videos/12345678901234/", "12345678901234", false)]
    [InlineData("Facebook", "https://www.facebook.com/share/v/ABCdef123/", "ABCdef123", true)]
    [InlineData("Facebook", "https://fb.watch/ABCdef123/", "ABCdef123", true)]
    public void Public_and_mobile_video_references_are_normalized_without_claiming_verification(string platform, string url, string id, bool share)
    {
        var result = SocialVideoLink.Parse(platform, url);
        Assert.Equal(platform, result.Provider); Assert.Equal(id, result.ContentId); Assert.Equal(share, result.IsShareLink);
        Assert.DoesNotContain("igsh", result.Url);
    }

    [Theory]
    [InlineData("TikTok", "https://www.tiktok.com.evil.example/@creator/video/7412345678901234567")]
    [InlineData("YouTube", "https://youtube.com@127.0.0.1/watch?v=dQw4w9WgXcQ")]
    [InlineData("YouTube", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&v=another0000")]
    [InlineData("Instagram", "http://www.instagram.com/reel/ABCdef_123/")]
    [InlineData("Instagram", "https://www.instagram.com:444/reel/ABCdef_123/")]
    [InlineData("Facebook", "https://www.facebook.com/profile.php?id=12345678901234")]
    [InlineData("Facebook", "https://127.0.0.1/watch/?v=12345678901234")]
    [InlineData("Unsupported", "https://example.com/video/123456")]
    public void Unsafe_or_non_video_links_fail_validation(string platform, string url)
        => Assert.Throws<ApplicationFailure>(() => SocialVideoLink.Parse(platform, url));
}
