using System.Collections.Concurrent;
using Sehatak.Application.Interfaces.IPushNotification;

namespace Sehatak.IntegrationTests.Infrastructure;

/// <summary>Records pushes instead of calling FCM. Set <see cref="Succeed"/> = false to simulate FCM failure.</summary>
public sealed class FakePushNotification : IPushNotification
{
    public record Sent(string Token, string Title, string Body, IDictionary<string, string>? Data);

    public ConcurrentQueue<Sent> Messages { get; } = new();
    public bool Succeed { get; set; } = true;

    public Task<bool> SendAsync(string token, string title, string body, IDictionary<string, string>? data = null)
    {
        Messages.Enqueue(new Sent(token, title, body, data));
        return Task.FromResult(Succeed);
    }
}
