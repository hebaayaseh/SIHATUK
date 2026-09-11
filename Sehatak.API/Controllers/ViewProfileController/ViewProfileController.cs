using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.AspNetCore.Authorization;
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

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("patient-view-profile/{centerId}")]
        public async Task<IActionResult> PatientViewProfile(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await viewProfile.ViewPatientProfileAsync(centerId, userId);
            return Ok(result);
        }
        

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("doctor-view-profile/{centerId}")]
        public async Task<IActionResult> DoctorViewProfileAsync(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await viewProfile.ViewDoctorProfiAsync(centerId, userId);
            return Ok(result);
        }

        [Authorize(Policy = "MedicalStaff")]
        [HttpGet("staff-view-profile/{centerId}")]
        public async Task<IActionResult> StaffViewProfileAsync(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await viewProfile.ViewStaffProfileAsync(centerId, userId);
            return Ok(result);
        }

    }
}
