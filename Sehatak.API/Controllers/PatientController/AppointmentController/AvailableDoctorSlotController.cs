using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Infrastructure.Services.AppointmentService;
using System.Security.Claims;

namespace Sehatak.API.Controllers.PatientController.AppointmentController
{
    [ApiController]
    [Route("api-get-available-doctor-slot")]
    public class AvailableDoctorSlotController : ControllerBase
    {
        private readonly IAppointment slotService;
        public AvailableDoctorSlotController(IAppointment slotService)
        {
            this.slotService = slotService;
        }

        [HttpPost("available-doctor-slot/{centerId}/{doctorId}")]
        public async Task<IActionResult> AvailableDoctorSlot(int centerId , int doctorId , [FromBody] DateOnly date)
        {
            var result = await slotService.GetAvailableDoctorSlot(centerId , doctorId , date);
            return Ok(result);
        }

        [HttpPost("book/{centerId}/{doctorId}")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> BookAppointment(int centerId, int doctorId, [FromBody] BookAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.BookAppointmentAsync(centerId, doctorId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("cancel-slot/{centerId}")]
        public async Task<IActionResult> CancelSlotAsync(int centerId , DeleteDoctorSlotRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.DeleteDoctorSlotAsync(centerId, userId, request);
            return Ok(result);
        }
    }
}
