using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.Application.Interfaces.StaffLogin;

namespace Sehatak.API.Controllers.StaffLogin
{
    [ApiController]
    [Route("api/auth")]
    public class StaffLoginController : ControllerBase
    {
        private readonly IStaffLogin staffLogin;
        public StaffLoginController(IStaffLogin staffLogin)
        {
            this.staffLogin = staffLogin;
        }

        [HttpPost("staff-login/{centerId}")]
        public async Task<IActionResult> MedicalStaffLogin(int centerId,StaffLoginRequestDto request)
        {
            var result = await staffLogin.StaffLoginAsync(centerId, request);
            return Ok(result);
        }
    }
}
