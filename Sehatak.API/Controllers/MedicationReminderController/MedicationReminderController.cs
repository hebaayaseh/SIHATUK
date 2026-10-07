using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.MedicationReminderDto;
using Sehatak.Application.Interfaces.IMedicationReminder;

namespace Sehatak.API.Controllers.MedicationReminderController
{
    [ApiController]
    [Route("[Controller]")]
    public class MedicationReminderController : ControllerBase
    {
        private readonly IMedicationReminder medication;
        public MedicationReminderController(IMedicationReminder medication)
        {
            this.medication = medication;
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-add-medication-reminder/{centerId}")]
        public async Task<IActionResult> AddMedicationReminderAsync(int centerId , MedicationReminderRequestDto request , [FromQuery] int? subPatientId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await medication.AddMedicationReminderAsync(centerId, userId, request, subPatientId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("get-medications-reminders/{centerId}")]
        public async Task<IActionResult> GetMedicationReminderAsync(int centerId ,PagedRequest request, [FromQuery] int? subPatientId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await medication.GetMedicationRemindersAsync(centerId, userId,request, subPatientId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("delete-medication-reminder/{centerId}")]
        public async Task<IActionResult> DeleteMedicationReminderAsync(int centerId, [FromQuery] int reminderId ,[FromQuery] int? subPatientId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await medication.DeleteMedicationReminderAsync(centerId, userId, reminderId, subPatientId);
            return Ok(result);
        }

    }
}
