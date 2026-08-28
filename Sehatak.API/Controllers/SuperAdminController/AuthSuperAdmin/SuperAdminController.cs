using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sehatak.Application.DTOs.SuperAdminDto;
using Sehatak.Application.Interfaces.SuperAdminInterface;

namespace Sehatak.API.Controllers.SuperAdminController.AuthSuperAdmin
{
    [ApiController]
    [Route("api/[Controller]")]
    public class SuperAdminController : ControllerBase
    {
        private readonly ISuperAdminAuthService superAdminAuthService;
        public SuperAdminController(ISuperAdminAuthService superAdminAuthService)
        {
            this.superAdminAuthService = superAdminAuthService;
        }
        [AllowAnonymous]
        [HttpPost("Register_SuperAdmin")]
        public async Task<IActionResult> RegisterSuperAdmin([FromForm]RegisterSuperAdminRequestDto superAdminDto)
        {
            var result = await superAdminAuthService.RegisterAsync(superAdminDto);
            return Ok(result);
        }
        [HttpPost("SuperAdmin_Login")]
        public async Task<IActionResult> SuperAdminLogin(SuperAdminLoginRequestDto superAdminDto)
        {
            var result = await superAdminAuthService.LoginAsync(superAdminDto);
            if (result == null)
                throw new UnauthorizedAccessException();

            return Ok(result);
        }
    }
}
