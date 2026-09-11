using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.Interfaces.AuthPatient;
using Sehatak.Application.Interfaces.IViewProfile;

namespace Sehatak.API.Controllers.PatientController.Patient
{
    [ApiController]
    [Route("[Controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuth authService;
        public AuthController(IAuth authService)
        {
            this.authService = authService;
            
        }
        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("patient-register-patient/{centerId}")]
        public async Task<IActionResult> RegisterPatient (int centerId,[FromForm] RegisterRequestDto registerRequestDto)
        {
            var result = await authService.RegisterAsync(centerId,registerRequestDto);
            return Ok(result);
        }
        [HttpPost("patient-verify-code/{centerId}")]
        public async Task<IActionResult> VerifyCode(int centerId,[FromBody] VerifyOtpRequestDto request)
        {
            var result = await authService.VerifyOtpAsync(centerId,request);

            if (result == null)
                throw new ArgumentException("الكود غير صحيح أو منتهي الصلاحية.");

            return Ok(result);
        }

        [EnableRateLimiting("LoginPolicy")]
        [AllowAnonymous]
        [HttpPost("patient-login-patient/{centerId}")]
        public async Task<IActionResult> LoginPatient(int centerId, [FromBody] PatientRequestDto registerRequestDto)
        {
            var result = await authService.LoginPatientAsync(centerId, registerRequestDto);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPut("patient-deactive-profile/{centerId}")]
        public async Task<IActionResult> PatientDeactiveProfile(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await authService.PatientDeactiveProfileAsync(centerId, userId);
            return Ok(result);
        }

        [HttpPut("patient-active-profile/{centerId}")]
        public async Task<IActionResult> PatientActiveProfile(int centerId, [FromBody] PatientRequestDto request)
        {
            var result = await authService.PatientActiveProfileAsync(centerId, request);
            return Ok(result);
        }
    }
}
