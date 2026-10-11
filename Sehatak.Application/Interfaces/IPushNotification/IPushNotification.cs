

namespace Sehatak.Application.Interfaces.IPushNotification
{
    public interface IPushNotification
    {
        Task<bool> SendAsync(string token, string title, string body, IDictionary<string, string>? data = null);
    }
}
