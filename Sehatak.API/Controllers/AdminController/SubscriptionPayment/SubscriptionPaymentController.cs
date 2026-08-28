using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Interfaces.ISubscriptionPaymentService;
using System.Security.Claims;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.SubscriptionPayment
{
    [ApiController]
    [Route("api/[Controller]")]
    public class SubscriptionPaymentController : ControllerBase
    {
        private readonly ISubscriptionPayment subscriptionPayment;
        public SubscriptionPaymentController(ISubscriptionPayment subscriptionPayment)
        {
            this.subscriptionPayment = subscriptionPayment;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("record-payment/{centerId}")]
        public async Task<IActionResult> RecordPayment(int centerId, [FromForm] PaymentRequestExist request)
        {
            var result = await subscriptionPayment.RecordPaymentAsync(request, centerId);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("confirm-payment/{paymentId}")]
        public async Task<IActionResult> ConfirmPayment(int paymentId)
        {
            var superAdminId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var result = await subscriptionPayment.ConfirmPaymentAsync(paymentId, superAdminId);
            return Ok(new { message = "Payment confirmed successfully" });

        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("center-payment/{centerId}")]
        public async Task<IActionResult> GetCenterPayment(int centerId)
        {
            var result = await subscriptionPayment.GetCenterPaymentsAsync(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("pending-payment")]
        public async Task<IActionResult> GetPendingPayment()
        {
            var result = await subscriptionPayment.GetPendingPaymentsAsync();
            return Ok(result);
        }

    }
}

