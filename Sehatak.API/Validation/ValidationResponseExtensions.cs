using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Reflection;

namespace Sehatak.API.Validation
{
    public static class ValidationResponseExtensions
    {
        /// <summary>
        /// Validators emit resource keys ("Validation.Required"), not sentences.
        /// This turns the ModelState into the same { status, message } envelope
        /// ExceptionMiddleware produces, with every key resolved through
        /// Resources/Messages.{culture}.resx, plus a per-field breakdown.
        /// </summary>
        public static IServiceCollection AddLocalizedValidationResponse(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var factory = context.HttpContext.RequestServices
                        .GetRequiredService<IStringLocalizerFactory>();

                    var localizer = factory.Create(
                        "Messages",
                        Assembly.GetExecutingAssembly().GetName().Name!);

                    var errors = context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(
                            e => e.Key,
                            e => e.Value!.Errors
                                  .Select(err => Localize(localizer, err.ErrorMessage))
                                  .ToArray());

                    return new BadRequestObjectResult(new
                    {
                        status = StatusCodes.Status400BadRequest,
                        message = localizer["Validation.Failed"].Value,
                        errors
                    });
                };
            });

            return services;
        }

        // Model binding failures ("The JSON value could not be converted…") are not
        // resource keys, so pass anything that does not look like one straight through.
        private static string Localize(IStringLocalizer localizer, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || message.Contains(' '))
                return message;

            var localized = localizer[message];
            return localized.ResourceNotFound ? message : localized.Value;
        }
    }
}
