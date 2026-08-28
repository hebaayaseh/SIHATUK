using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.Auth;
using Sehatak.Application.Interfaces.IAuth;

namespace Sehatak.API.Controllers.tokenController
{
    [ApiController]
    [Route("api/[Controller]")]
    public class TokenController : ControllerBase
    {
        private readonly ITokenService _tokenService;

        public TokenController(ITokenService tokenService)
        {
            _tokenService = tokenService;
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request)
        {
            var result = await _tokenService.RefreshAsync(request.RefreshToken);
            return Ok(result);
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequestDto request)
        {
            await _tokenService.LogoutAsync(request.RefreshToken);
            return Ok(new { message = "تم تسجيل الخروج بنجاح" });
        }
    }
}
