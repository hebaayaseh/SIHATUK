using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.GetSttafInterFace;

namespace Sehatak.API.Controllers.AdminController.GetStaffcontroller
{
    [ApiController]
    [Route("api/[Controller]")]
    public class GetDoctorsWithDepartmentController : ControllerBase
    {
        private readonly IGetStaff getStaff;
        public GetDoctorsWithDepartmentController(IGetStaff getStaff)
        {
            this.getStaff = getStaff;
        }
        [Authorize("AdminOnly")]
        [HttpGet("get-doctors/{centerId}")]
        public async Task<IActionResult> GetDoctorsWithDepartments(int centerId)
        {
            var result = await getStaff.GetDoctorsAsync(centerId);
            return Ok(result);
        }

        [Authorize("AdminOnly")]
        [HttpGet("get-doctor/{centerId}/{doctorId}")]
        public async Task<IActionResult> GetDoctor(int centerId,int doctorId)
        {
            var result = await getStaff.GetDoctorAsync(centerId,doctorId);
            return Ok(result);
        }


    }
}
