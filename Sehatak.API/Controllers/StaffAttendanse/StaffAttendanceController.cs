using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Application.Interfaces.IStaffAttendance;
using System.Security.Claims;

namespace Sehatak.API.Controllers.StaffAttendanse
{
    [ApiController]
    [Route("api/StaffShift")]
    public class StaffAttendanceController : ControllerBase
    {
        private readonly IStaffAttendance staffAttendance;
        public StaffAttendanceController(IStaffAttendance staffAttendance)
        {
            this.staffAttendance = staffAttendance;
        }

        [Authorize(Policy = "StaffShift")]
        [HttpPost("chickin-time{centerId}")]
        public async Task<IActionResult> ChickInTimeAsync(int centerId , [FromBody] StaffAttendanceCheckInRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await staffAttendance.CheckInTimeAsync(centerId,userId, request);
            return Ok(result);
        }

    }
}
