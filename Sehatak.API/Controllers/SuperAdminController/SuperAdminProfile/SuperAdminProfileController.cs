using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditSuperAdmin;
using Sehatak.Application.Interfaces.IProfileInterface;

namespace Sehatak.API.Controllers.SuperAdminController.SuperAdminProfile
{
    [ApiController]
    [Route("api/[Controller]")]
    public class SuperAdminProfileController : ControllerBase
    {
        private readonly IProfile profile;
        public SuperAdminProfileController(IProfile profile)
        {
            this.profile = profile;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("super-admin-view-profile/{superAdminId}")]

        public async Task<IActionResult> superAdminViewProfile(int superAdminId)
        {
            var reault = await profile.ViewProfile(superAdminId);
            return Ok(reault);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("super-admin-edit-email/{superAdminId}")]
        public async Task<IActionResult> RequestEditEmail(int superAdminId, [FromBody] EditEmailRequest request)
        {
            await profile.RequestEditEmail(superAdminId, request);
            return Ok(new { message = "Verification code sent" });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("super-admin-confirm-edit-email/{superAdminId}")]
        public async Task<IActionResult> ConfirmEditEmail(int superAdminId, [FromBody] ConfirmEditEmailRequest request)
        {
            var result = await profile.ConfirmEditEmail(superAdminId, request);
            return Ok(result);
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("super-admin-edit-password/{superAdminId}")]
        public async Task<IActionResult> RequestEditPassword(int superAdminId, [FromBody] EditPasswordRequest request)
        {
            await profile.RequestEditPassword(superAdminId, request);
            return Ok(new { message = "Verification code sent" });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("super-admin-confirm-edit-password/{superAdminId}")]
        public async Task<IActionResult> ConfirmEditPassword(int superAdminId, [FromBody] ConfirmEditPasswordRequest request)
        {
            var result = await profile.ConfirmEditPassword(superAdminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPut("super-admin-edit-name/{superAdminId}")]
        public async Task<IActionResult> EditName(int superAdminId, [FromBody] EditNameRequest request)
        {
            var result = await profile.EditName(superAdminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPut("super-admin-edit-profile-image/{superAdminId}")]
        public async Task<IActionResult> EditProfileImage(int superAdminId, [FromForm] EditProfileImageRequest request)
        {
            var result = await profile.EditProfileImage(superAdminId, request);
            return Ok(result);
        }

    }
}
