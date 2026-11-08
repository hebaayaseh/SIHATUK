using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sehatak.Application.DTOs.Auth;
using Sehatak.Application.Interfaces.IAuth;

namespace Sehatak.API.Controllers.tokenController
{
    [ApiController]
    [Route("[Controller]")]
    public class TokenController : ControllerBase
    {
        private readonly ITokenService _tokenService;

        public TokenController(ITokenService tokenService)
        {
            _tokenService = tokenService;
        }
        [EnableRateLimiting("GeneralPolicy")]
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request)
        {
            var result = await _tokenService.RefreshAsync(request.RefreshToken);
            return Ok(result);
        }
        [EnableRateLimiting("GeneralPolicy")]
        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody] LogoutRequestDto request)
        {
            await _tokenService.LogoutAsync(request.RefreshToken);
            return Ok(new { message = "تم تسجيل الخروج بنجاح" });
        }
    }
}
