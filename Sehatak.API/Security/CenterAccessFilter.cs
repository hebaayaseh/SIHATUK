using Microsoft.AspNetCore.Http;
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
                // ما في centerId بالـ route. سابقاً كان هذا بيمر بصمت — بيفتح باب
                // لأي endpoint جديد ينسى المطوّر يحط centerId بالـ route فيصير
                // بدون فحص عزل بين المراكز إطلاقاً. هلأ نرفض افتراضياً، ونسمح
                // فقط للـ endpoints المعلّمة صراحة بـ [CenterAgnostic] (بيانات
                // عامة مشتركة بين كل المراكز، زي كتالوج الخطط والمزايا).
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