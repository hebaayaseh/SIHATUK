using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Application.Interfaces.AddDoctorDailyHours;
using System.Security.Claims;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.AddDoctorDaiktcontroller
{
    [ApiController]
    [Route("api/[Controller]")]
    public class DoctorDailyController : ControllerBase
    {
        private readonly IDoctorDailyHours addHours;
        public DoctorDailyController(IDoctorDailyHours addHours)
        {
            this.addHours = addHours;
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-doctor-hours/{centerId}/{doctorId}")]
        public async Task<IActionResult> AddDoctorDailyHours(int centerId , int doctorId,
            [FromBody] AddDoctorDailyHoursRequest request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.AddDoctorDailyHoursAsync(centerId,userId, doctorId,request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("update-doctor-hours/{centerId}/{doctorId}")]
        public async Task<IActionResult> UpdateDoctorDailyHours(int centerId, int doctorId,
            [FromBody] UpdateDoctorDailyHousrRequest request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.UpdateDoctorDailyHoursAsync(centerId, userId, doctorId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("Cancel-doctor-day/{centerId}")]
        public async Task<IActionResult> CancelDoctorDay(int centerId, DateOnly date)
        {
            var doctorId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await addHours.CancleDailyHoursAsync(centerId, doctorId, date);
            return Ok(result);
        }

    }
}
