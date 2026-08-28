using Microsoft.Extensions.Localization;
using Sehatak.Application.DTOs.Exceptions;

namespace Sehatak.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IStringLocalizerFactory _localizerFactory;

    public ExceptionMiddleware(RequestDelegate next,ILogger<ExceptionMiddleware> logger,IStringLocalizerFactory localizerFactory)
    {
        _next = next;
        _logger = logger;
        _localizerFactory = localizerFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {

            _logger.LogError(ex,
             "Unhandled exception occurred. Path: {Path}, Method: {Method}",
             context.Request.Path,
             context.Request.Method);

            await HandleExceptionAsync(context, ex, _localizerFactory);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context,Exception ex,IStringLocalizerFactory localizerFactory)
    {
        var localizer = localizerFactory.Create("Messages",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!
        );
        
        context.Response.ContentType = "application/json";


        var (statusCode, messageKey) = ex switch
        {
            BusinessException be => (StatusCodes.Status400BadRequest, be.Message), 
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Auth.Unauthorized"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "General.NotFound"),
            ArgumentException => (StatusCodes.Status400BadRequest, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "General.ServerError")
        };

        context.Response.StatusCode = statusCode;

        var message = (statusCode == 400 && ex is ArgumentException)
        ? ex.Message
    :   localizer[messageKey].Value;

        await context.Response.WriteAsJsonAsync(new
        {
            status = statusCode,
            message = message
        });

    }
}
