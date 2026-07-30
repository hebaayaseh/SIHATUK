using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Sehatak.Domain.Entities.General;
using Sehatak.Infrastructure.Data;

namespace Sehatak.API.Hubs;

[Authorize(Policy = "MedicalStaff")]
public class ChatHubs : Hub
{
    private readonly TenantDbContextFactory _tenantFactory;
    private readonly OnlineUserTracker _onlineTracker;

    public ChatHubs(TenantDbContextFactory tenantFactory, OnlineUserTracker onlineTracker)
    {
        _tenantFactory = tenantFactory;
        _onlineTracker = onlineTracker;
    }
    private int GetCenterIdFromClaims()
    {
        var centerIdClaim = Context.User?.FindFirst("CenterId")?.Value;

        if (string.IsNullOrEmpty(centerIdClaim) || !int.TryParse(centerIdClaim, out var centerId))
            throw new HubException("Center context missing.");
        return centerId;
    }
    public async Task SendMessage(int receiverId, string message)
    {

        var senderIdStr = Context.UserIdentifier;
        if (string.IsNullOrEmpty(senderIdStr) || !int.TryParse(senderIdStr, out var senderId))
            throw new HubException("Unauthorized.");

        var centerId = GetCenterIdFromClaims();

        using var db = _tenantFactory.CreateForCenter(centerId);
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

        await Clients.User(receiverId.ToString()).SendAsync("ReceiveMessage", payload);


        await Clients.User(senderId.ToString()).SendAsync("MessageSent", payload);
    }

    public async Task Typing(int reciverId)
    {
        var senderId = Context.UserIdentifier;
        await Clients.User(reciverId.ToString()).SendAsync("UserTyping", new { UserId = senderId });
    }

    public async Task StopTyping(int reciverId)
    {
        var senderId = Context.UserIdentifier;
        await Clients.User(reciverId.ToString()).SendAsync("UserStoppedTyping", new { UserId = senderId });
    }

    public async Task MarkAsRead(int messageId)
    {
        var centerId = GetCenterIdFromClaims();
        using var db = _tenantFactory.CreateForCenter(centerId);
        var message = await db.Chats
            .FindAsync(messageId);

        if (message == null) throw new HubException("Message not found.");

        message.IsRead = true;
        await db.SaveChangesAsync();

        await Clients.User(message.SenderId.ToString())
           .SendAsync("MessageRead", new { MessageId = messageId });
    }

    public async Task DeleteMessage(int messageId)
    {
        var centerId = GetCenterIdFromClaims();
        using var db = _tenantFactory.CreateForCenter(centerId);
        var message = await db.Chats
            .FindAsync(messageId);

        if (message == null) throw new HubException("Message not found.");

        message.IsDeleted= true;
        await db.SaveChangesAsync();

        await Clients.User(message.SenderId.ToString()).SendAsync("MessageDeleted", new { MessageId = messageId });
        await Clients.User(message.ReceiverId.ToString()).SendAsync("MessageDeleted", new { MessageId = messageId });


    }
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            var wasOffline = _onlineTracker.UserConnected(userId);
            if (wasOffline)
            {
                await Clients.Others.SendAsync("UserOnline", userId);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            var wentOffline = _onlineTracker.UserDisconnected(userId);
            if (wentOffline)
            {
                await Clients.Others.SendAsync("UserOffline", userId);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
}

