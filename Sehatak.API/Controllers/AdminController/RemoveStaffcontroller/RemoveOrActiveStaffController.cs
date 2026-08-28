using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.Interfaces.DepartmentInterface;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.RemoveStaffcontroller
{
    [ApiController]
    [Route("api/[Controller]")]
    public class RemoveOrActiveStaffController : ControllerBase
    {
        private readonly IRemoveStaff removeStaff;
        public RemoveOrActiveStaffController(IRemoveStaff removeStaff)
        {
            this.removeStaff = removeStaff;
        }
        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("remove-staff-from-center/{centerId}")]
        public async Task<IActionResult> RmeoveStaff(int centerId, [FromBody] RemoveStaffRequestDto request)
        {
            var result = await removeStaff.RemoveStaffAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("active-staff-from-center/{centerId}")]
        public async Task<IActionResult> ActiveStaff(int centerId, [FromBody] RemoveStaffRequestDto request)
        {
            var result = await removeStaff.ActiveStaffAsync(centerId, request);
            return Ok(result);
        }
    }
}
