using Sehatak.Application.DTOs.ChatHubDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.ChatInterface
{
    public interface IChatHub
    {
        Task<List<ChatMessageDto>> GetConversationAsync(int centerId, int userId1, int userId2);
        Task<List<ConversationSummaryDto>> GetInboxAsync(int centerId, int currentUserId);
        Task<List<UserSearchResultDto>> SearchUsersAsync(int centerId, int currentUserId, string searchTerm);
    }
}
