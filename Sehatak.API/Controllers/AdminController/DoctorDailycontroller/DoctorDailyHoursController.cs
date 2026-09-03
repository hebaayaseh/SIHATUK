using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Application.Interfaces.AddDoctorDailyHours;
using Sehatak.Domain.Entities.TenantEntities;
using System.Security.Claims;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.AddDoctorDaiktcontroller
{
    [ApiController]
    [Route("[Controller]")]
    public class DoctorDailyHoursController : ControllerBase
    {
        private readonly IDoctorDailyHours addHours;
        public DoctorDailyHoursController(IDoctorDailyHours addHours)
        {
            this.addHours = addHours;
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("admin-add-doctor-hours/{centerId}/{doctorId}")]
        public async Task<IActionResult> AddDoctorDailyHours(int centerId , int doctorId,
            [FromBody] AddDoctorDailyHoursRequest request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.AddDoctorDailyHoursAsync(centerId,userId, doctorId,request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("admin-update-doctor-hours/{centerId}/{doctorId}")]
        public async Task<IActionResult> UpdateDoctorDailyHours(int centerId, int doctorId,
            [FromBody] UpdateDoctorDailyHousrRequest request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.UpdateDoctorDailyHoursAsync(centerId, userId, doctorId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("doctor-Cancel-doctor-day/{centerId}")]
        public async Task<IActionResult> CancelDoctorDay(int centerId, DateOnly date)
        {
            var doctorId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.CancleDailyHoursAsync(centerId, doctorId, date);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin-get-doctor-hours/{centerId}/{doctorId}")]
        public async Task<IActionResult> GetDoctorDailyHours(int centerId, int doctorId, [FromQuery] PagedRequest request)
        {

            var result = await addHours.GetDoctorDailyHoursAsync(centerId, doctorId,request);
            return Ok(result);
        }
        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("doctor-get-appointments-for-day{centerId}")]
        public async Task<IActionResult> GetDoctorAppointmentsForDay(int centerId, [FromBody] DateOnly date)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.GetDoctorAppointmentsForDayAsync(centerId, userId, date);
            return Ok(result);
        }
    }
}
