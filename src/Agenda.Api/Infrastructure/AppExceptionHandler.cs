using Agenda.Api.GoogleApi;
using Google;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Agenda.Api.Infrastructure;

/// <summary>
/// Converte exceções conhecidas em respostas HTTP com um "code" estável,
/// que o front usa para decidir o que mostrar (ex.: botão "Reconectar Google").
/// </summary>
public sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        (int status, string code, string title)? mapped = exception switch
        {
            GoogleReconnectRequiredException =>
                (StatusCodes.Status409Conflict, "google_reconnect_required", "Reconecte sua conta Google."),
            GoogleApiException =>
                (StatusCodes.Status502BadGateway, "google_error", "O Google não respondeu como esperado. Tente novamente."),
            _ => null,
        };

        if (mapped is not { } m) return false; // deixa o handler padrão gerar um 500

        if (exception is GoogleApiException)
            logger.LogWarning(exception, "Erro na API do Google");

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
