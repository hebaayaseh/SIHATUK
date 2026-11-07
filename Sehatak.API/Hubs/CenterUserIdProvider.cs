using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Sehatak.API.Hubs;

/// <summary>
/// User ids are only unique INSIDE one center (every center has its own database), so SignalR must never address a user by the bare id:
/// "5" would reach user 5 of every center that is connected. The SignalR user identifier is "{centerId}:{userId}".
/// </summary>
public sealed class CenterUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var user = connection.User;
        var id = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var center = user?.FindFirst("CenterId")?.Value;
        return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(center) ? null : For(center, id);
    }

    public static string For(int centerId, int userId) => $"{centerId}:{userId}";
    private static string For(string centerId, string userId) => $"{centerId}:{userId}";
}
