using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
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
            {
                
                var isCenterAgnostic = context.ActionDescriptor is ControllerActionDescriptor cad &&
                    cad.MethodInfo.GetCustomAttributes(typeof(CenterAgnosticAttribute), false).Any();

                if (!isCenterAgnostic)
                {
                    context.Result = new ObjectResult(new { error = "Auth.CenterIdRequired" })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                }

                return Task.CompletedTask;
            }

            if (!int.TryParse(routeValue?.ToString(), out var routeCenterId))
            {
                context.Result = new ObjectResult(new { error = "Auth.CenterIdRequired" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return Task.CompletedTask;
            }

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