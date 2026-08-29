using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Infrastructure.Services.AppointmentService;
using System.Security.Claims;

namespace Sehatak.API.Controllers.PatientController.AppointmentController
{
    [ApiController]
    [Route("api/[Controller]")]
    public class Appointmentcontroller : ControllerBase
    {
        private readonly IAppointment slotService;
        public Appointmentcontroller(IAppointment slotService)
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
        [Authorize(Policy = "Patient")]
        public async Task<IActionResult> BookAppointment(int centerId, int doctorId, [FromBody] BookAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.BookAppointmentAsync(centerId, doctorId, userId, request);
            return Ok(result);
        }

        [HttpPost("cancel-appointment/{centerId}/{doctorId}")]
        [Authorize(Policy = "Patient")]
        public async Task<IActionResult> CancelAppointment(int centerId, int doctorId, [FromBody] CancelAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.CancelAppointmentAsync(centerId, doctorId, userId, request);
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

        [HttpPost("reschedule-appointment/{centerId}/{doctorId}")]
        [Authorize(Policy = "Patient")]
        public async Task<IActionResult> RescheduleAppointment(int centerId, int doctorId, [FromBody] RescheduleAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.RescheduleAppointmentAsync(centerId, doctorId, userId, request);
            return Ok(result);
        }

        [HttpPost("join-waitlist/{centerId}/{doctorId}")]
        [Authorize(Policy = "Patient")]
        public async Task<IActionResult> JoinWaitLis(int centerId, int doctorId, [FromBody] DateOnly date)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.JoinWaitListAsync(centerId, doctorId, userId, date);
            return Ok(result);
        }

        [HttpGet("view-waitlist/{centerId}/{doctorId}")]
        [Authorize(Policy = "WaitListViewrs")]
        public async Task<IActionResult> ViewPatientsWaitList(int centerId, int doctorId,DateOnly date)
        {
            var result = await slotService.GetPatientsWaitListsAsync(centerId, doctorId, date);
            return Ok(result);
        }

        [HttpGet("view-my-waitlist/{centerId}/{doctorId}")]
        [Authorize(Policy = "PatientOnly")]
        public async Task<IActionResult> ViewWaitLis(int centerId, int doctorId, DateOnly date)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.GetPatientWaitListsAsync(centerId, doctorId,userId ,date);
            return Ok(result);
        }


    }
}
