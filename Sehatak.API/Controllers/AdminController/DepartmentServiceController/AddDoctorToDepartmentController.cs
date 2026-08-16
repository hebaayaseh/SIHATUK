using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Interfaces.DepartmentInterface;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.DepartmentServiceController
{
    [ApiController]
    [Route("api/AdminOnly")]
    public class AddDoctorToDepartmentController : ControllerBase
    {
        private readonly IAddDoctorToDepartment addDoctor;
        public AddDoctorToDepartmentController(IAddDoctorToDepartment addDoctor)
        {
            this.addDoctor = addDoctor;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("add-doctor-to-department/{centerId}")]
        public async Task<IActionResult> AddDoctorToDepartment(int centerId,[FromForm] DoctorRequestDto request)
        {
            var result = await addDoctor.RegisterDoctorAsync(centerId, request);
            return Ok(result);
        }
    }
}
