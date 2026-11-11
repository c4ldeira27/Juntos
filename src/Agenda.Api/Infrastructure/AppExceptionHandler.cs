using Agenda.Api.GoogleApi;
using Google;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Agenda.Api.Infrastructure;

/// <summary>
/// Turns known exceptions into HTTP responses with a stable "code", which the front end
/// uses to decide what to show (e.g. a "Reconnect Google" button) in the user's language.
/// </summary>
public sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        (int status, string code, string title)? mapped = exception switch
        {
            GoogleReconnectRequiredException =>
                (StatusCodes.Status409Conflict, "google_reconnect_required", "Reconnect your Google account."),
            GoogleApiException =>
                (StatusCodes.Status502BadGateway, "google_error", "Google did not respond as expected. Try again."),
            _ => null,
        };

        if (mapped is not { } m) return false; // let the default handler produce a 500

        if (exception is GoogleApiException)
            logger.LogWarning(exception, "Google API error");

        httpContext.Response.StatusCode = m.status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = m.status,
                Title = m.title,
                Extensions = { ["code"] = m.code },
            },
        });
    }
}
