using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.Interfaces.IProfileInterface.ProfileAdmin;
using System.Security.Claims;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.EditProfilecontroller
{
    [ApiController]
    [Route("[Controller]")]
    public class EditStaffProfileController : ControllerBase
    {
        private readonly IprofileStaff iprofile;
        public EditStaffProfileController(IprofileStaff iprofile)
        {
            this.iprofile = iprofile;
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("admin-edit-center-information/{centerId}")]
        public async Task<IActionResult> EditStaffInformation(int centerId , [FromForm] EditCenterInformationRequest request)
        {
            var result = await iprofile.EditCenterInformation(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "MedicalStaff")]
        [HttpPut("MedicalStaff-edit-staff-information/{centerId}")]
        public async Task<IActionResult> EditStaffInformation(int centerId, [FromForm] EditSttafInformationRequest request)
        {
            var adminId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await iprofile.EditSttafInformation(centerId,adminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "MedicalStaff")]
        [HttpPost("MedicalStaff-edit-staff-email/{centerId}")]
        public async Task<IActionResult> EditStaffEmail(int centerId, [FromBody] EditEmailRequest request)
        {
            var adminId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await iprofile.RequestEditEmail(centerId, adminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "MedicalStaff")]
        [HttpPost("MedicalStaff-edit-staff-confirm-email-code/{centerId}")]
        public async Task<IActionResult> ConfirmEmail(int centerId, [FromBody] ConfirmEditEmailRequest request)
        {
            var adminId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await iprofile.ConfirmEditEmail(centerId, adminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "MedicalStaff")]
        [HttpPost("MedicalStaff-edit-staff-password/{centerId}")]
        public async Task<IActionResult> EditStaffPasswored(int centerId, [FromBody] EditPasswordRequest request)
        {
            var adminId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await iprofile.RequestEditPassword(centerId, adminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "MedicalStaff")]
        [HttpPost("MedicalStaff-edit-staff-confirm-password-code/{centerId}")]
        public async Task<IActionResult> ConfirmPassword(int centerId, [FromBody] ConfirmEditPasswordRequest request)
        {
            var adminId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await iprofile.ConfirmEditPassword(centerId, adminId, request);
            return Ok(result);
        }


    }
}
