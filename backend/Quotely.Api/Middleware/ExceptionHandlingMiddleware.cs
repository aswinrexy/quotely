using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Quotely.Api.Middleware;

/// <summary>Turns every unhandled exception into a consistent JSON envelope, never a stack trace.</summary>
public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var (status, message) = Map(ex);

            if (status == HttpStatusCode.InternalServerError)
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                _logger.LogInformation("Request failed ({Status}) on {Path}: {Message}", (int)status, context.Request.Path, ex.Message);

            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { message, status = (int)status }, JsonOptions));
        }
    }

    private static (HttpStatusCode, string) Map(Exception ex) => ex switch
    {
        ApiException api => (api.StatusCode, api.Message),
        UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "You are not signed in."),
        DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "The record was modified by another request. Please retry."),
        DbUpdateException => (HttpStatusCode.BadRequest, "The change could not be saved. Please check the submitted values."),
        _ => (HttpStatusCode.InternalServerError, "Something went wrong. Please try again.")
    };
}
