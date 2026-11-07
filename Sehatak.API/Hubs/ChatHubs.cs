using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.General;
using Sehatak.Infrastructure.Data;

namespace Sehatak.API.Hubs;

[Authorize(Policy = "MedicalStaff")]
public class ChatHubs : Hub
{
    private const int MaxMessageLength = 2000;

    private readonly TenantDbContextFactory _tenantFactory;
    private readonly OnlineUserTracker _onlineTracker;

    public ChatHubs(TenantDbContextFactory tenantFactory, OnlineUserTracker onlineTracker)
    {
        _tenantFactory = tenantFactory;
        _onlineTracker = onlineTracker;
    }

    private static string CenterGroup(int centerId) => $"center-{centerId}";

    private int GetCenterIdFromClaims()
    {
        var centerIdClaim = Context.User?.FindFirst("CenterId")?.Value;
        if (string.IsNullOrEmpty(centerIdClaim) || !int.TryParse(centerIdClaim, out var centerId))
            throw new HubException("Center context missing.");
        return centerId;
    }

    private int GetUserIdFromClaims()
    {
        var idClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(idClaim) || !int.TryParse(idClaim, out var userId))
            throw new HubException("Unauthorized.");
        return userId;
    }

    public async Task SendMessage(int receiverId, string message)
    {
        var senderId = GetUserIdFromClaims();
        var centerId = GetCenterIdFromClaims();

        if (string.IsNullOrWhiteSpace(message))
            throw new HubException("Message is empty.");
        message = message.Trim();
        if (message.Length > MaxMessageLength)
            throw new HubException("Message is too long.");
        if (receiverId == senderId)
            throw new HubException("You cannot message yourself.");

        using var db = _tenantFactory.CreateForCenter(centerId);

        var receiverExists = await db.Users.AnyAsync(u => u.Id == receiverId && u.isActive);
        if (!receiverExists)
            throw new HubException("Receiver not found.");

        var chatMessage = new Chat
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Message = message,
            SentAt = DateTime.UtcNow,
            IsRead = false,
            IsDeleted = false
        };

        db.Chats.Add(chatMessage);
        await db.SaveChangesAsync();

        var payload = new
        {
            id = chatMessage.Id,
            SenderId = senderId,
            ReceiverId = receiverId,
            message = chatMessage.Message,
            sentAt = chatMessage.SentAt,
            IsRead = false,
            IsDeleted = false
        };

        await Clients.User(CenterUserIdProvider.For(centerId, receiverId)).SendAsync("ReceiveMessage", payload);
        await Clients.User(CenterUserIdProvider.For(centerId, senderId)).SendAsync("MessageSent", payload);
    }

    public async Task Typing(int receiverId)
    {
        var centerId = GetCenterIdFromClaims();
        var senderId = GetUserIdFromClaims();
        await Clients.User(CenterUserIdProvider.For(centerId, receiverId)).SendAsync("UserTyping", new { UserId = senderId.ToString() });
    }

    public async Task StopTyping(int receiverId)
    {
        var centerId = GetCenterIdFromClaims();
        var senderId = GetUserIdFromClaims();
        await Clients.User(CenterUserIdProvider.For(centerId, receiverId)).SendAsync("UserStoppedTyping", new { UserId = senderId.ToString() });
    }

    /// <summary>Only the person the message was sent TO can mark it as read.</summary>
    public async Task MarkAsRead(int messageId)
    {
        var centerId = GetCenterIdFromClaims();
        var userId = GetUserIdFromClaims();
        using var db = _tenantFactory.CreateForCenter(centerId);

        var message = await db.Chats.FirstOrDefaultAsync(c => c.Id == messageId && c.ReceiverId == userId);
        if (message == null) throw new HubException("Message not found.");

        message.IsRead = true;
        await db.SaveChangesAsync();

        await Clients.User(CenterUserIdProvider.For(centerId, message.SenderId))
            .SendAsync("MessageRead", new { MessageId = messageId });
    }

    /// <summary>Only the person who SENT the message can delete it.</summary>
    public async Task DeleteMessage(int messageId)
    {
        var centerId = GetCenterIdFromClaims();
        var userId = GetUserIdFromClaims();
        using var db = _tenantFactory.CreateForCenter(centerId);

        var message = await db.Chats.FirstOrDefaultAsync(c => c.Id == messageId && c.SenderId == userId);
        if (message == null) throw new HubException("Message not found.");

        message.IsDeleted = true;
        await db.SaveChangesAsync();

        await Clients.User(CenterUserIdProvider.For(centerId, message.SenderId)).SendAsync("MessageDeleted", new { MessageId = messageId });
        await Clients.User(CenterUserIdProvider.For(centerId, message.ReceiverId)).SendAsync("MessageDeleted", new { MessageId = messageId });
    }

    public override async Task OnConnectedAsync()
    {
        var centerId = GetCenterIdFromClaims();
        var userId = GetUserIdFromClaims();

        // everyone connected is put in his center's group: presence is only ever sent inside the center
        await Groups.AddToGroupAsync(Context.ConnectionId, CenterGroup(centerId));

        if (_onlineTracker.UserConnected(CenterUserIdProvider.For(centerId, userId)))
            await Clients.OthersInGroup(CenterGroup(centerId)).SendAsync("UserOnline", userId.ToString());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var centerClaim = Context.User?.FindFirst("CenterId")?.Value;
        var idClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (int.TryParse(centerClaim, out var centerId) && int.TryParse(idClaim, out var userId)
            && _onlineTracker.UserDisconnected(CenterUserIdProvider.For(centerId, userId)))
        {
            await Clients.OthersInGroup(CenterGroup(centerId)).SendAsync("UserOffline", userId.ToString());
        }

        await base.OnDisconnectedAsync(exception);
    }
}
