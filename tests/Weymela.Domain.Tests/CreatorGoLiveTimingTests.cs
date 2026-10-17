using System;
using Weymela.Domain;
using Xunit;

namespace Weymela.Domain.Tests;

public sealed class CreatorGoLiveTimingTests
{
    [Fact]
    public void Link_and_creator_intent_do_not_verify_or_start_the_window_and_retries_cannot_restart_it()
    {
        var now = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var item = new CreatorPublicationVerification(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), "Facebook", "123456789", now);
        item.RecordPublicLink("https://www.facebook.com/watch/?v=123456789");
        item.RequestGoLive(now); item.RequestGoLive(now.AddMinutes(1));
        Assert.Equal(now, item.GoLiveRequestedAtUtc); Assert.Null(item.WentLiveAtUtc);
        Assert.Throws<InvalidOperationException>(() => item.GoLive(now, 30));
        item.Verify(PublicationVerificationMethod.Manual, "controlled-fixture-review", null, now.AddHours(1), Guid.NewGuid());
        item.GoLive(now.AddHours(1), 30); item.GoLive(now.AddDays(2), 90);
        Assert.Equal(now.AddHours(1), item.WentLiveAtUtc);
        Assert.Equal(now.AddHours(1).AddDays(30), item.ExpiresAtUtc); Assert.Equal(30, item.LiveDurationDays);
        Assert.Null(item.BaselineViews);
    }
}
