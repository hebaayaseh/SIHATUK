using Microsoft.Extensions.Logging.Abstractions;
using Sehatak.Infrastructure.Services.PushNotificationService;
using Shouldly;

namespace Sehatak.IntegrationTests.Notifications;

/// <summary>Only what can be checked without a real Firebase project.</summary>
public class FcmPushServiceTests
{
    private static FcmPushService NewSvc() => new(NullLogger<FcmPushService>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_token_is_not_sent_and_says_so(string? token)
    {
        (await NewSvc().SendAsync(token!, "title", "body")).ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_push_that_cannot_be_delivered_returns_false_and_never_throws()
    {
        // callers (reminder job, delay job, chat) just check the bool; an exception here would kill their whole pass.
        // With Firebase not set up DefaultInstance is null (NullReferenceException), with a bad key / no network
        // the failure is not always a FirebaseMessagingException - only that one type is caught today.
        var send = NewSvc().SendAsync("not-a-real-token", "title", "body", new Dictionary<string, string> { ["k"] = "v" });

        var result = await send.WaitAsync(TimeSpan.FromSeconds(60));

        result.ShouldBeFalse();
    }
}
