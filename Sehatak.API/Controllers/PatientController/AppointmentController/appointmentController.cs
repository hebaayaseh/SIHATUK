using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Infrastructure.Services.AppointmentService;
using System.Security.Claims;

namespace Sehatak.API.Controllers.PatientController.AppointmentController
{
    [ApiController]
    [Route("[Controller]")]
    public class Appointmentcontroller : ControllerBase
    {
        private readonly IAppointment slotService;
        public Appointmentcontroller(IAppointment slotService)
        {
            this.slotService = slotService;
        }

        [HttpPost("patient-available-doctor-slot/{centerId}/{doctorId}")]
        public async Task<IActionResult> AvailableDoctorSlot(int centerId , int doctorId , [FromBody] DateOnly date)
        {
            var result = await slotService.GetAvailableDoctorSlot(centerId , doctorId , date);
            return Ok(result);
        }
        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-book/{centerId}/{doctorId}")]
        public async Task<IActionResult> BookAppointment(int centerId, int doctorId, [FromBody] BookAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.BookAppointmentAsync(centerId, doctorId, userId, request);
            return Ok(result);
        }
        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-cancel-appointment/{centerId}/{doctorId}")]
        public async Task<IActionResult> CancelAppointment(int centerId, int doctorId, [FromBody] CancelAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.CancelAppointmentAsync(centerId, doctorId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("doctor-cancel-slot/{centerId}")]
        public async Task<IActionResult> CancelSlotAsync(int centerId , DeleteDoctorSlotRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.DeleteDoctorSlotAsync(centerId, userId, request);
            return Ok(result);
        }
        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-reschedule-appointment/{centerId}/{doctorId}")]
        public async Task<IActionResult> RescheduleAppointment(int centerId, int doctorId, [FromBody] RescheduleAppointmentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.RescheduleAppointmentAsync(centerId, doctorId, userId, request);
            return Ok(result);
        }
        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-join-waitlist/{centerId}/{doctorId}")]
        public async Task<IActionResult> JoinWaitLis(int centerId, int doctorId, [FromBody] DateOnly date)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.JoinWaitListAsync(centerId, doctorId, userId, date);
            return Ok(result);
        }

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpGet("Receptionist-view-waitlist/{centerId}/{doctorId}")]
        public async Task<IActionResult> ViewPatientsWaitList(int centerId, int doctorId,DateOnly date)
        {
            var result = await slotService.GetPatientsWaitListsAsync(centerId, doctorId, date);
            return Ok(result);
        }
        [Authorize(Policy = "PatientOnly")]
        [HttpGet("patient-view-my-waitlist/{centerId}/{doctorId}")]
        public async Task<IActionResult> ViewWaitLis(int centerId, int doctorId, DateOnly date)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.GetPatientWaitListsAsync(centerId, doctorId,userId ,date);
            return Ok(result);
        }

        [HttpGet("patient-get-doctor/{centerId}/{doctorId}")]
        public async Task<IActionResult> GetDoctor(int centerId, int doctorId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.GetDoctorAsync(centerId, doctorId);
            return Ok(result);
        }

        [HttpGet("patient-get-doctors/{centerId}")]
        public async Task<IActionResult> GetDoctors(int centerId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await slotService.GetDoctorsAsync(centerId);
            return Ok(result);
        }



    }
}
