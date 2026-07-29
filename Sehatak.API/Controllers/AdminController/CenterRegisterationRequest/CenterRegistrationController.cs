using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Interfaces.CenterRegistrationRequest;

namespace Sehatak.API.Controllers.CenterRegistration
{
    [ApiController]
    [Route("api/center-registration")]
    public class CenterRegistrationController : ControllerBase
    {
        private readonly ICenterRegistration _registrationService;

        public CenterRegistrationController(ICenterRegistration registrationService)
        {
            _registrationService = registrationService;
        }


        [HttpPost("request")]
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
        [HttpGet("pending-payments")]
        public async Task<IActionResult> GetPendingRegistrationPayments()
        {
            var result = await _registrationService.GetPendingRegistrationPaymentsAsync();
            return Ok(result);
        }

        // 4️⃣ السوبر أدمن يأكد الدفعة (بعد ما يتحقق من صورة الحوالة يدويًا)
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("payments/{paymentId}/confirm")]
        public async Task<IActionResult> ConfirmRegistrationPayment(int paymentId)
        {
            var superAdminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await _registrationService.ConfirmRegistrationPaymentAsync(paymentId, superAdminId);
            return Ok(new { success = result });
        }

        // 5️⃣ السوبر أدمن يشوف كل الطلبات (Pending)
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("requests")]
        public async Task<IActionResult> GetCentersRegisteration()
        {
            var result = await _registrationService.GetCentersRegisterationAsync();
            return Ok(result);
        }

        // 6️⃣ السوبر أدمن يشوف تفاصيل طلب معيّن
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("requests/{requestId}")]
        public async Task<IActionResult> GetCenterRegistration(int requestId)
        {
            var result = await _registrationService.GetCenterRegistrationAsync(requestId);
            return Ok(result);
        }

        // 7️⃣ السوبر أدمن يوافق (بس لو فيه دفعة مؤكدة مسبقًا)
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("requests/{requestId}/approve")]
        public async Task<IActionResult> ApproveCenterRequest(int requestId)
        {
            var superAdminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await _registrationService.ApproveCenterRequest(requestId, superAdminId);
            return Ok(new { success = result });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("requests/{requestId}/reject")]
        public async Task<IActionResult> RejectRequest(int requestId, [FromBody] string rejectionReason)
        {
            var superAdminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await _registrationService.RejectAsync(requestId, superAdminId, rejectionReason);
            return Ok(new { success = result });
        }
    }
}