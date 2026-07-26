using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.DoctorAppointment;
using System.Security.Claims;

namespace Sehatak.API.Controllers.AdminController.DoctorDailycontroller
{
    [ApiController]
    [Route("get-appointment-day")]
    public class GetDoctorAppointmentForDayController : ControllerBase
    {
        private readonly IDoctorAppointment appointment;
        public GetDoctorAppointmentForDayController(IDoctorAppointment appointment)
        {
            this.appointment = appointment;
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("/{centerId}")]
        public async Task<IActionResult> GetDoctorAppointmentsForDay(int centerId , DateOnly date)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await appointment.GetDoctorAppointmentsForDayAsync(centerId,userId, date);
            return Ok(result);
        }
    }
}
