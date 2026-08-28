using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Sehatak.API.Security
{
    public class CenterAccessFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (!(user.Identity?.IsAuthenticated ?? false))
                return Task.CompletedTask; 

            if (user.IsInRole("SuperAdmin"))
                return Task.CompletedTask; 

            if (!context.RouteData.Values.TryGetValue("centerId", out var routeValue))
                return Task.CompletedTask; 

            if (!int.TryParse(routeValue?.ToString(), out var routeCenterId))
                return Task.CompletedTask;

            var claim = user.FindFirst("CenterId");
            if (claim == null || !int.TryParse(claim.Value, out var tokenCenterId) || tokenCenterId != routeCenterId)
            {
                context.Result = new ObjectResult(new { error = "Auth.CenterMismatch" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            return Task.CompletedTask;
        }
    }
}