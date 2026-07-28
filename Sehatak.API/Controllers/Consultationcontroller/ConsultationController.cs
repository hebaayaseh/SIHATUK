using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.ConsultaionInterface;
using System.Security.Claims;

namespace Sehatak.API.Controllers.Consultationcontroller
{
    [ApiController]
    [Route("Consultation")]
    public class ConsultationController : ControllerBase
    {
        private readonly IConsultation consultation;
        public ConsultationController(IConsultation consultation)
        {
            this.consultation = consultation;
        }

        [HttpGet("get-doctors/{centerId}")]
        public async Task<IActionResult> GetDoctors(int centerId)
        {
            var result = await consultation.GetDoctorEnableConsultation(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("request/{centerId}/{doctorId}")]
        public async Task<IActionResult> RequestConsultation(int centerId , int doctorId)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ConsultationRequest(centerId, doctorId, userId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("get-consultation/{centerId}/{doctorId}")]
        public async Task<IActionResult> GetConsultation(int centerId, int doctorId)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ViewConsultation(centerId, doctorId, userId);
            return Ok(result);
        }
    }
}
