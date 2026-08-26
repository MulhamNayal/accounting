using Accounting.Api.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Middleware;

/// <summary>
/// Maps exception type to HTTP response for every endpoint, so controllers never catch.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var status = MapStatusCode(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation(
                "Rejected {Path}: {Message}", httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            await httpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = status,
                    Title = "The request could not be completed.",
                    Detail = exception.Message,
                },
                cancellationToken);
        }
        else
        {
            // 400/404/409 bodies are a raw JSON string, so the frontend can read
            // err.response.data directly without unwrapping an object.
            await httpContext.Response.WriteAsJsonAsync(exception.Message, cancellationToken);
        }

        return true;
    }

    public static int MapStatusCode(Exception exception) => exception switch
    {
        AuthenticationFailedException => StatusCodes.Status401Unauthorized,
        NotFoundException => StatusCodes.Status404NotFound,
        PostingValidationException => StatusCodes.Status400BadRequest,
        LedgerIntegrityException => StatusCodes.Status409Conflict,
        InvalidOperationException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status502BadGateway,
    };
}
