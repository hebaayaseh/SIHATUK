using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Interfaces;
using System.Security.Claims;

namespace Sehatak.API.Controllers.MedicalRecordController
{
    [ApiController]
    [Route("api/DoctorOnly")]
    public class MedicalRecordController :ControllerBase
    {
        private readonly IMedicalRecord medical;
        public MedicalRecordController(IMedicalRecord medical)
        {
            this.medical = medical;
        }

        [Authorize("DoctorOnly")]
        [HttpPost("add-medicalrecord")]
        public async Task<IActionResult> AddMedicakRecor(int centerId,[FromBody]MedicalReqordRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await medical.AddMedicalRecordAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize("DoctorOnly")]
        [HttpPost("update-medicalrecord")]
        public async Task<IActionResult> UpdateMedicakRecor(int centerId, [FromBody] UpdateMedicalRecordRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await medical.EditMedicalRecordAsync(centerId, userId, request);
            return Ok(result);
        }
    }
}
