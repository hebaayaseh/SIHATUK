using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.Interfaces.AuthPatient;

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
    }
}
