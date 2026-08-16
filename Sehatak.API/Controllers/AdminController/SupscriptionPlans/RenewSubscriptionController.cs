using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.RenewSubscription;
using Sehatak.Application.Interfaces.RenewSubscription;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.SupscriptionPlans
{
    [ApiController]
    [Route("api/AdminOrAbove")]
    public class RenewSubscriptionController : ControllerBase
    {
        private readonly IRenewSubscription renewSubscriptionService;
        public RenewSubscriptionController(IRenewSubscription renewSubscriptionService)
        {
            this.renewSubscriptionService = renewSubscriptionService;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("renew-active-subscription/{centerId}")]
        public async Task<IActionResult> RenewActiveSubscription(int centerId, [FromBody] RenewSubscriptionRequest request)
        {
            var result = await renewSubscriptionService.RenewActiveSubscriptionAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("renew-expird-subscription/{centerId}")]
        public async Task<IActionResult> RenewExpiredSubscription(int centerId, [FromBody] RenewSubscriptionRequest request)
        {
            var result = await renewSubscriptionService.RenewExpiredSubscriptionAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("cancle-subscription/{centerId}")]
        public async Task<IActionResult> CancleSubscription(int centerId, [FromBody] CancleSubcsriptionRequest request)
        {
            var result = await renewSubscriptionService.CancleSubscriptionAsync(centerId, request);
            return Ok(result);
        }
    }
}
