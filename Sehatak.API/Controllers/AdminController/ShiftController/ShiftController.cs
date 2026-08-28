using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.Interfaces.IShiftSchedule;
using Sehatak.Domain.Enums;
using System.Security.Claims;

namespace Sehatak.API.Controllers.AdminController.ShiftController
{
    [ApiController]
    [Route("api/[Controller]")]
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
            var result = await shiftSchedule.AddShiftSchedule(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-staff-shift/{centerId}")]
        public async Task<IActionResult> AddStaffShift(int centerId, [FromBody] AssignShiftToStaffRequestDto request)
        {
            var result = await shiftSchedule.AssignShiftToStaffAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin-get-shifts/{centerId}")]
        public async Task<IActionResult> GetShifts(int centerId)
        {
            var result = await shiftSchedule.GetShiftsSchedulesAsync(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("update-shift-schedule/{centerId}")]
        public async Task<IActionResult> UpdateShift(int centerId, [FromBody] UpdateShiftSchedualRequestDto request)
        {
            var result = await shiftSchedule.UpdateShiftScheduleAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpDelete("delete-shift-schedule/{centerId}/{shiftId}")]
        public async Task<IActionResult> DeleteShift(int centerId,int shiftId)
        {
            var result = await shiftSchedule.DeleteShiftSchedualeAsync(centerId, shiftId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("get-staffs-schedule/{centerId}")]
        public async Task<IActionResult> DeleteShift(int centerId, ShiftGroup shift, int? year = null, int? month = null)
        {
            var result = await shiftSchedule.GetStaffsWithShiftAsync(centerId, shift,year,month);
            return Ok(result);
        }

    }
}
