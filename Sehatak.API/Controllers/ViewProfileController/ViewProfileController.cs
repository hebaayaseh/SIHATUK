using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.IEmerngency;
using Sehatak.Application.Interfaces.IViewProfile;

namespace Sehatak.API.Controllers.ViewProfileController
{
    [ApiController]
    [Route("[Controller]")]
    public class ViewProfileController : ControllerBase
    {
        private readonly IViewProfile viewProfile;
        public ViewProfileController(IViewProfile viewProfile)
        {
            this.viewProfile = viewProfile;
        }

        [HttpGet("patient-view-profile/{centerId}")]
        public async Task<IActionResult> PatientViewProfile(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await viewProfile.ViewPatientProfileAsync(centerId, userId);
            return Ok(result);
        }

        [HttpPut("patient-deactive-profile/{centerId}")]
        public async Task<IActionResult> PatientDeactiveProfile(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await viewProfile.PatientDeactiveProfileAsync(centerId, userId);
            return Ok(result);
        }
    }
}
