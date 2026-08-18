using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.Interfaces.IShiftSchedule;
using System.Security.Claims;

namespace Sehatak.API.Controllers.AdminController.ShiftController
{
    [ApiController]
    [Route("api/AdminOnly")]
    public class ShiftController : ControllerBase
    {
        private readonly IShift shiftSchedule;
        public ShiftController(IShift shiftSchedule)
        {
            this.shiftSchedule = shiftSchedule;
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-shif-schedule/{centerId}")]
        public async Task<IActionResult> AddShifSchedule(int centerId , [FromBody] ShiftScheduleRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await shiftSchedule.AddShiftSchedule(userId, centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-staff-shift/{centerId}")]
        public async Task<IActionResult> AddStaffShift(int centerId, [FromBody] AssignShiftToStaffRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await shiftSchedule.AssignShiftToStaffAsync(userId, centerId, request);
            return Ok(result);
        }
    }
}
