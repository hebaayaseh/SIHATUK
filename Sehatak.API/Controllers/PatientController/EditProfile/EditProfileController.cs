using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.Interfaces.IProfileInterface;

namespace Sehatak.API.Controllers.PatientController.EditProfile
{
    [ApiController]
    [Route("api/[Controller]")]
    public class EditProfileController : ControllerBase
    {
        private readonly IProfilePatient profilePatient;
        public EditProfileController(IProfilePatient profilePatient)
        {
            this.profilePatient = profilePatient;
        }
        [Authorize(Policy = "Patient")]
        [HttpPut("edit-patient-information/{centerId}")]
        public async Task<IActionResult> EditPatientInformation(int centerId, [FromForm] EditPatientInformationRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await profilePatient.EditPatientInformation(centerId, userId, request);
            return Ok(result);
        }
        [Authorize("Patient")]
        [HttpPost("edit-patient-email/{centerId}")]
        public async Task<IActionResult> EditPatientEmail(int centerId, [FromForm] EditEmailRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await profilePatient.RequestEditEmail(centerId, userId, request);
            return Ok(result);
        }
        [Authorize("Patient")]
        [HttpPost("confirm-edit-patient-email/{centerId}")]
        public async Task<IActionResult> ConfirmEditPatientEmail(int centerId, [FromForm] ConfirmEditEmailRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await profilePatient.ConfirmEditEmail(centerId, userId, request);
            return Ok(result);
        }
        [Authorize("Patient")]
        [HttpPost("edit-patient-password/{centerId}")]
        public async Task<IActionResult> EditPatientPassword(int centerId, [FromForm] EditPasswordRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await profilePatient.RequestEditPassword(centerId, userId, request);
            return Ok(result);
        }
        [Authorize("Patient")]
        [HttpPost("confirm-edit-patient-password/{centerId}")]
        public async Task<IActionResult> ConfirmEditPatientPassword(int centerId, [FromForm] ConfirmEditPasswordRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await profilePatient.ConfirmEditPassword(centerId, userId, request);
            return Ok(result);
        }
        [Authorize("Patient")]
        [HttpGet("view-patient-information/{centerId}")]
        public async Task<IActionResult> ViewPatientInformation(int centerId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await profilePatient.ViewPatientInformation(centerId, userId);
            return Ok(result);
        }
    }
}
