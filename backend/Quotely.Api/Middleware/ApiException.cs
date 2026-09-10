using System.Net;

namespace Quotely.Api.Middleware;

/// <summary>Expected, user-facing failures. Anything else becomes a generic 500.</summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public ApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;

    public static ApiException NotFound(string what = "Resource") =>
        new(HttpStatusCode.NotFound, $"{what} was not found.");

    public static ApiException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);

    public static ApiException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}
