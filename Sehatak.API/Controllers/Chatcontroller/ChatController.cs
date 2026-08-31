using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.ChatInterface;
using Sehatak.Infrastructure.Services;

namespace Sehatak.API.Controllers.Chat
{
    [ApiController]
    [Route("[Controller]")]
    [Authorize(Policy = "MedicalStaff")]
    public class ChatController : ControllerBase
    {
        private readonly IChatHub _chatHistoryService;

        public ChatController(IChatHub chatHistoryService)
        {
            _chatHistoryService = chatHistoryService;
        }

        [HttpGet("conversation/{centerId}/{otherUserId}")]
        public async Task<IActionResult> GetConversation(int centerId, int otherUserId)
        {
            var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var messages = await _chatHistoryService.GetConversationAsync(centerId, currentUserId, otherUserId);
            return Ok(messages);
        }

        [HttpGet("inbox/{centerId}")]
        public async Task<IActionResult> GetInbox(int centerId)
        {
            var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var inbox = await _chatHistoryService.GetInboxAsync(centerId, currentUserId);
            return Ok(inbox);
        }

        [HttpGet("search/{centerId}")]
        public async Task<IActionResult> SearchUsers(int centerId, [FromQuery] string term)
        {
            var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var results = await _chatHistoryService.SearchUsersAsync(centerId, currentUserId, term);
            return Ok(results);
        }
    }
}
