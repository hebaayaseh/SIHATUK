using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ChatHubDto;
using Sehatak.Application.Interfaces.ChatInterface;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services
{
    public class ChatHistoryService : IChatHub
    {
        private readonly TenantDbContextFactory _tenantFactory;

        public ChatHistoryService(TenantDbContextFactory tenantFactory)
        {
            _tenantFactory = tenantFactory;
        }

        public async Task<List<ChatMessageDto>> GetConversationAsync(int centerId, int userId1, int userId2)
        {
            using var db = _tenantFactory.CreateForCenter(centerId);

            return await db.Chats
                .Where(c => (c.SenderId == userId1 && c.ReceiverId == userId2)
                         || (c.SenderId == userId2 && c.ReceiverId == userId1))
                .OrderBy(c => c.SentAt)
                .Select(c => new ChatMessageDto
                {
                    Id = c.Id,
                    SenterId = c.SenderId,
                    ReceiverId = c.ReceiverId,
                    Message = c.IsDeleted ? "تم حذف هذه الرسالة" : c.Message,
                    isRead = c.IsRead,
                    IsDeleted = c.IsDeleted,
                    SendAt = c.SentAt
                })
                .ToListAsync();
        }

        public async Task<List<ConversationSummaryDto>> GetInboxAsync(int centerId, int currentUserId)
        {
            using var db = _tenantFactory.CreateForCenter(centerId);


            var allMessages = await db.Chats
                .Where(c => c.SenderId == currentUserId || c.ReceiverId == currentUserId)
                .OrderByDescending(c => c.SentAt)
                .ToListAsync();


            var grouped = allMessages
                .GroupBy(c => c.SenderId == currentUserId ? c.ReceiverId : c.SenderId)
                .Select(g => new
                {
                    OtherUserId = g.Key,
                    LastMessage = g.First(), 
                    UnreadCount = g.Count(m => m.ReceiverId == currentUserId && !m.IsRead && !m.IsDeleted)
                })
                .OrderByDescending(x => x.LastMessage.SentAt)
                .ToList();

            var otherUserIds = grouped.Select(g => g.OtherUserId).ToList();

            var users = await db.Users
                .Where(u => otherUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u);

            var result = new List<ConversationSummaryDto>();

            foreach (var item in grouped)
            {
                if (!users.TryGetValue(item.OtherUserId, out var otherUser))
                    continue; 

                result.Add(new ConversationSummaryDto
                {
                    OtherUserId = otherUser.Id,
                    OtherUserName = $"{otherUser.firstName} {otherUser.lastName}",
                    OtherUserProfileImage = otherUser.ProfileImageUrl,
                    LastMessage = item.LastMessage.IsDeleted ? "تم حذف هذه الرسالة" : item.LastMessage.Message,
                    LastMessageAt = item.LastMessage.SentAt,
                    LastMessageIsRead = item.LastMessage.IsRead,
                    LastMessageSenderId = item.LastMessage.SenderId,
                    UnreadCount = item.UnreadCount
                });
            }

            return result;
        }

        public async Task<List<UserSearchResultDto>> SearchUsersAsync(int centerId, int currentUserId, string searchTerm)
        {
            using var db = _tenantFactory.CreateForCenter(centerId);

            var currentUser = await db.Users.FindAsync(currentUserId);
            if (currentUser == null)
                return new List<UserSearchResultDto>();

            var query = db.Users.Where(u => u.Id != currentUserId && u.isActive);


            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(u =>
                    (u.firstName + " " + u.lastName).Contains(searchTerm)
                    || u.email.Contains(searchTerm));
            }

            return await query
                .Take(20)
                .Select(u => new UserSearchResultDto
                {
                    UserId = u.Id,
                    FullName = u.firstName + " " + u.lastName,
                    Role = u.role.ToString(),
                    ProfileImage = u.ProfileImageUrl
                })
                .ToListAsync();
        }
    }
}