using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Interfaces.CenterRegistrationRequest;

namespace Sehatak.API.Controllers.CenterRegistration
{
    [ApiController]
    [Route("[Controller]")]
    public class CenterRegistrationController : ControllerBase
    {
        private readonly ICenterRegistration _registrationService;

        public CenterRegistrationController(ICenterRegistration registrationService)
        {
            _registrationService = registrationService;
        }


        [HttpPost("center-registration")]
        public async Task<IActionResult> CenterRequest([FromForm] CenterRegistrationRequestDto request)
        {
            var result = await _registrationService.CenterRequestAsync(request);
            return Ok(result);
        }

        [HttpPost("{requestId}/record-payment")]
        public async Task<IActionResult> RecordRegistrationPayment(int requestId, [FromForm] recordPaymentRequestDto request)
        {
            var result = await _registrationService.RecordRegistrationPaymentAsync(requestId, request);
            return Ok(new { success = result });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-pending-payments")]
        public async Task<IActionResult> GetPendingRegistrationPayments([FromQuery] PagedRequest request)
        {
            var result = await _registrationService.GetPendingRegistrationPaymentsAsync(request);
            return Ok(result);
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-payments/{paymentId}/confirm")]
        public async Task<IActionResult> ConfirmRegistrationPayment(int paymentId)
        {
            var superAdminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await _registrationService.ConfirmRegistrationPaymentAsync(paymentId, superAdminId);
            return Ok(new { success = result });
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-requests")]
        public async Task<IActionResult> GetCentersRegisteration([FromQuery] PagedRequest request)
        {
            var result = await _registrationService.GetCentersRegisterationAsync(request);
            return Ok(result);
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-requests/{requestId}")]
        public async Task<IActionResult> GetCenterRegistration(int requestId)
        {
            var result = await _registrationService.GetCenterRegistrationAsync(requestId);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-requests/{requestId}/approve")]
        public async Task<IActionResult> ApproveCenterRequest(int requestId)
        {
            var superAdminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await _registrationService.ApproveCenterRequest(requestId, superAdminId);
            return Ok(new { success = result });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-requests/{requestId}/reject")]
        public async Task<IActionResult> RejectRequest(int requestId, [FromBody] string rejectionReason)
        {
            var superAdminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await _registrationService.RejectAsync(requestId, superAdminId, rejectionReason);
            return Ok(new { success = result });
        }
    }
}