using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Application.Interfaces.IStaffAttendance;
using System.Security.Claims;

namespace Sehatak.API.Controllers.StaffAttendanse
{
    [ApiController]
    [Route("api/[Controller]")]
    public class StaffAttendanceController : ControllerBase
    {
        private readonly IStaffAttendance staffAttendance;
        public StaffAttendanceController(IStaffAttendance staffAttendance)
        {
            this.staffAttendance = staffAttendance;
        }

        [Authorize(Policy = "StaffShift")]
        [HttpPost("chickin-time/{centerId}")]
        public async Task<IActionResult> ChickInTimeAsync(int centerId , [FromBody] StaffAttendanceCheckInRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await staffAttendance.CheckInTimeAsync(centerId,userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "StaffShift")]
        [HttpPost("chickout-time/{centerId}")]
        public async Task<IActionResult> ChickOutTimeAsync(int centerId, [FromBody] StaffAttendanceCheckInRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await staffAttendance.CheckOutTimeAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "StaffShift")]
        [HttpPost("onleave/{centerId}")]
        public async Task<IActionResult> OnLeaveAsync(int centerId, [FromBody] StaffOnLeaveRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await staffAttendance.OnLeaveAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("absent/{centerId}")]
        public async Task<IActionResult> StaffAbsentAsync(int centerId, [FromBody] StaffAbsentRequestDto request)
        {
            var result = await staffAttendance.AbsentStaffAsync(centerId, request);
            return Ok(result);
        }

    }
}
