using System.Diagnostics;
using FiscalDocs.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FiscalDocs.Api.ErrorHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int Status, string Title, string? Detail) error = exception switch
        {
            InvalidDocumentException => (
                StatusCodes.Status422UnprocessableEntity,
                "The document could not be processed.",
                exception.Message),

            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                "The request could not be processed.",
                null),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                null)
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (error.Status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);
        }
        else
        {
            // Só o tipo e o status: nunca o corpo da requisição nem o conteúdo do XML.
            _logger.LogWarning(
                "Request rejected with {Status} ({ExceptionType}). TraceId: {TraceId}",
                error.Status, exception.GetType().Name, traceId);
        }

        httpContext.Response.StatusCode = error.Status;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = error.Status,
                Title = error.Title,
                Detail = error.Detail
            }
        });
    }
}
