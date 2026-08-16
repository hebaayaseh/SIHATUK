using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Interfaces.SignUp;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.GetLabTechnicianAndReceptionistController
{
    [ApiController]
    [Route("api/AdminOnly")]
    public class AddReceptionistAndLabTechnicianController : ControllerBase
    {
        private readonly ISignup signup;
        public AddReceptionistAndLabTechnicianController(ISignup signup)
        {
            this.signup = signup;
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-Receptionist/{centerId}")]
        public async Task<IActionResult> AddReceptionist(int centerId , [FromForm] ReceptionistRequestDto request)
        {
            var result = await signup.AddReceptionistAsync(centerId, request);
            return Ok(result);
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-LabTechnician/{centerId}")]
        public async Task<IActionResult> AddLabTechnician(int centerId, [FromForm] LabTechnicianRequestDto request)
        {
            var result = await signup.AddLabTechnicianAsync(centerId, request);
            return Ok(result);
        }

    }
}
