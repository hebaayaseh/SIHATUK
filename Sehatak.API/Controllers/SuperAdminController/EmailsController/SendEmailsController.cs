using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sehatak.Application.DTOs.Email;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Services;
using Sehatak.Infrastructure.Services.SuperAdminService.Background;

namespace Sehatak.API.Controllers.SuperAdminController.Emails
{
    [ApiController]
    [Route("[Controller]")]
    public class SendEmailsController : ControllerBase
    {
        private readonly IAdminBulkEmailService _adminBulkEmailService;
        public SendEmailsController(IAdminBulkEmailService adminBulkEmailService)
        {
            _adminBulkEmailService = adminBulkEmailService;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("send-email")]
        public async Task<IActionResult> SendEmail([FromBody] SendEmailDto request)
        {
            var count = await _adminBulkEmailService.SendAsync(request);
            return Ok(new { sentTo = count });
        }
    }
}
