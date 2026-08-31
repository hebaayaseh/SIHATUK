using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Interfaces.IMedicalRecord;
using Sehatak.Domain.Entities.TenantEntities;
using System.Security.Claims;

namespace Sehatak.API.Controllers.MedicalRecordController
{
    [ApiController]
    [Route("[Controller]")]
    public class MedicalRecordController :ControllerBase
    {
        private readonly IMedicalRecord medical;
        public MedicalRecordController(IMedicalRecord medical)
        {
            this.medical = medical;
        }

        [Authorize("DoctorOnly")]
        [HttpPost("doctor-add-medicalrecord/{centerId}")]
        public async Task<IActionResult> AddMedicakRecor(int centerId,[FromBody]MedicalRecordDetailRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await medical.AddMedicalRecordAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize("DoctorOnly")]
        [HttpPost("doctor-update-medicalrecord/{centerId}")]
        public async Task<IActionResult> UpdateMedicakRecor(int centerId, [FromBody] UpdateMedicalRecordRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await medical.EditMedicalRecordAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("doctor-patient-history/{centerId}/{patientId}")]
        public async Task<IActionResult> GetPatientMedicalHistory(int centerId, int patientId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await medical.GetPatientMedicalHistoryAsync(centerId, userId, patientId);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("doctor-record/{centerId}/{medicalRecordId}")]
        public async Task<IActionResult> GetMedicalRecordById(int centerId, int medicalRecordId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await medical.GetMedicalRecordByIdAsync(centerId, userId, medicalRecordId);
            return Ok(result);
        }
    }
}
