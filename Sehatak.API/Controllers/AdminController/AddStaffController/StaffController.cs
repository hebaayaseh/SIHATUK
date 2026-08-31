using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Interfaces.SignUp;
using System.Security.Claims;

namespace Sehatak.API.Controllers.AdminController.AddStaffController
{
    [ApiController]
    [Route("[Controller]")]
    public class StaffController : ControllerBase
    {
        private readonly ISignup signup;
        public StaffController(ISignup signup)
        {
            this.signup = signup;
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpPost("admin-add-staff/{centerId}")]
        public async Task<IActionResult> AddSttaf(int centerId, [FromForm] AddStaffRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await signup.AddStafAsync(userId,centerId,request);
            return Ok(result);
        }
    }
}
