using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.ServicePriceDto;
using Sehatak.Application.Interfaces.ServicePriceInterface;
using System.Runtime.InteropServices;
using System.Security.Claims;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.ServicePricecontroller
{
    [ApiController]
    [Route("api/AdminOnly")]
    public class ServicePriceController : ControllerBase
    {
        private readonly IServicePrice servicePrice;
        public ServicePriceController(IServicePrice servicePrice)
        {
            this.servicePrice = servicePrice;
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("add-service-price/{centerId}")]
        public async Task<IActionResult> AddSrevicePrice(int centerId,[FromBody]ServicePriceRequest request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await servicePrice.AddServicePriceAsync(userId, centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("update-service-price/{centerId}")]
        public async Task<IActionResult> UpdateSrevicePrice(int centerId,[FromForm] UpdateServicePrice request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await servicePrice.updateServicePrice(userId, centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost("remove-service-price/{centerId}/{servicePriceId}")]
        public async Task<IActionResult> RemoveSrevicePrice(int centerId, int servicePriceId)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await servicePrice.RemoveServicePrice(userId, centerId, servicePriceId);
            return Ok(result);
        }

    }
}
