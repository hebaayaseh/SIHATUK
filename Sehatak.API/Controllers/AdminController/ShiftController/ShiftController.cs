using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.Interfaces.IShiftSchedule;
using Sehatak.Domain.Enums;
using System.Security.Claims;

namespace Sehatak.API.Controllers.AdminController.ShiftController
{
    [ApiController]
    [Route("[Controller]")]
    public class ShiftController : ControllerBase
    {
        private readonly IShift shiftSchedule;
        public ShiftController(IShift shiftSchedule)
        {
            this.shiftSchedule = shiftSchedule;
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpPost("admin-add-shif-schedule/{centerId}")]
        public async Task<IActionResult> AddShifSchedule(int centerId , [FromBody] ShiftScheduleRequest request)
        {
            var result = await shiftSchedule.AddShiftSchedule(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("admin-add-staff-shift/{centerId}")]
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
        [HttpPost("admin-update-shift-schedule/{centerId}")]
        public async Task<IActionResult> UpdateShift(int centerId, [FromBody] UpdateShiftSchedualRequestDto request)
        {
            var result = await shiftSchedule.UpdateShiftScheduleAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpDelete("admin-delete-shift-schedule/{centerId}/{shiftId}")]
        public async Task<IActionResult> DeleteShift(int centerId,int shiftId)
        {
            var result = await shiftSchedule.DeleteShiftSchedualeAsync(centerId, shiftId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin-get-staffs-schedule/{centerId}")]
        public async Task<IActionResult> GetStaffsWithShift(int centerId, ShiftGroup shift,[FromQuery]PagedRequest request, int? year = null, int? month = null)
        {
            var result = await shiftSchedule.GetStaffsWithShiftAsync(centerId, shift,request,year,month);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin-get-all-staffs/{centerId}")]
        public async Task<IActionResult> GetAllSTaffs(int centerId,[FromQuery]PagedRequest request)
        {
            var result = await shiftSchedule.GetAllStaffAsync(centerId,request);
            return Ok(result);
        }

    }
}
