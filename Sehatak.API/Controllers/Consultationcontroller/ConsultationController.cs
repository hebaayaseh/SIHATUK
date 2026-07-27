using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.ConsultaionInterface;

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
    }
}
