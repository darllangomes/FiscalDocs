

using System.Text;
using FiscalDocs.Api.Contracts;
using FiscalDocs.Application.Documents;
using FiscalDocs.Application.Ingestion;
using Microsoft.AspNetCore.Mvc;
namespace FiscalDocs.Api.Controllers;

[ApiController]
[Route("api/v1/documents")]

public sealed class DocumentsController : ControllerBase
{
    private const int MaxBodyBytes = 2 * 1024 * 1024;

    private readonly IngestDocumentHandler _ingestHandler;
    private readonly GetDocumentHandler _getHandler;
    private readonly ListDocumentsHandler _listHandler;
    private readonly UpdateDocumentHandler _updateHandler;
    private readonly DeleteDocumentHandler _deleteHandler;

    public DocumentsController(
        IngestDocumentHandler ingestHandler,
        GetDocumentHandler getHandler,
        ListDocumentsHandler listHandler,
        UpdateDocumentHandler updateHandler,
        DeleteDocumentHandler deleteHandler)
    {
        _ingestHandler = ingestHandler;
        _getHandler = getHandler;
        _listHandler = listHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
    }

    [HttpGet]
    [ProducesResponseType<ListDocumentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] ListDocumentsRequest request, CancellationToken cancellationToken)
    {
        var query = request.ToQuery(ModelState);
        if (query is null)
        {
            return ValidationProblem(ModelState);
        }

        var page = await _listHandler.HandleAsync(query, cancellationToken);
        return Ok(ListDocumentsResponse.From(page));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var document = await _getHandler.HandleAsync(id, cancellationToken);
        if (document is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Document not found.");
        }

        Response.Headers.ETag = $"\"{document.Version}\"";
        return Ok(DocumentResponse.From(document));
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Patch(
        Guid id, [FromBody] PatchDocumentRequest request, CancellationToken cancellationToken)
    {
        if (await _getHandler.HandleAsync(id, cancellationToken) is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Document not found.");
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(ifMatch))
        {
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Precondition required.",
                detail: "The If-Match header is required.");
        }

        if (!TryParseStrongEtag(ifMatch, out var expectedVersion))
        {
            return PreconditionFailedProblem();
        }

        var command = request.ToCommand(id, expectedVersion, ModelState);
        if (command is null)
        {
            return ValidationProblem(ModelState);
        }

        var result = await _updateHandler.HandleAsync(command, cancellationToken);
        switch (result.Outcome)
        {
            case UpdateDocumentOutcome.Updated:
                Response.Headers.ETag = $"\"{result.Details!.Version}\"";
                return Ok(DocumentResponse.From(result.Details));
            case UpdateDocumentOutcome.NotFound:
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Document not found.");
            case UpdateDocumentOutcome.PreconditionFailed:
                return PreconditionFailedProblem();
            case UpdateDocumentOutcome.Invalid:
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid request.",
                    detail: result.Error);
            default:
                throw new InvalidOperationException($"Unexpected outcome `{result.Outcome}`");
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await _getHandler.HandleAsync(id, cancellationToken) is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Document not found.");
        }

        uint? expectedVersion = null;
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (!string.IsNullOrEmpty(ifMatch))
        {
            if (!TryParseStrongEtag(ifMatch, out var version))
            {
                return PreconditionFailedProblem();
            }

            expectedVersion = version;
        }

        return await _deleteHandler.HandleAsync(id, expectedVersion, cancellationToken) switch
        {
            DeleteDocumentOutcome.Deleted => NoContent(),
            DeleteDocumentOutcome.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Document not found."),
            DeleteDocumentOutcome.PreconditionFailed => PreconditionFailedProblem(),
            _ => throw new InvalidOperationException("Unexpected delete outcome.")
        };
    }

    private ObjectResult PreconditionFailedProblem() => Problem(
        statusCode: StatusCodes.Status412PreconditionFailed,
        title: "Precondition failed.",
        detail: "The If-Match value does not match the current version of the document.");

    private static bool TryParseStrongEtag(string value, out uint version)
    {
        version = 0;
        return value.Length > 2
            && value[0] == '"'
            && value[^1] == '"'
            && uint.TryParse(value.AsSpan(1, value.Length - 2), out version);
    }

    [HttpPost]
    [Consumes("application/xml", "text/xml")]
    [ProducesResponseType<DocumentIngestedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<DocumentIngestedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]

    public async Task<IActionResult> Ingest(CancellationToken cancellationToken)
    {
        var xml = await ReadBodyAsync(cancellationToken);
        var result = await _ingestHandler.HandleAsync(xml, cancellationToken);
        var response = new DocumentIngestedResponse(result.DocumentId);

        return result.Outcome switch
        {
            IngestOutcome.Created => Created($"/api/v1/documents/{result.DocumentId}", response),
            IngestOutcome.AlreadyExists => Ok(response),
            IngestOutcome.ContentMismatch => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Document conflict.",
                detail: "A document with access key already exists with different content."
            ),
            IngestOutcome.DeletedDocument =>
            Problem(
                statusCode: StatusCodes.Status409Conflict,
                 title: "Document conflict.",
                    detail: "A document with this access key was deleted and cannot be sent again."
            ),
            _ => throw new InvalidOperationException($"Unexpected outcome `{result.Outcome}`")
        };

    }

    private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxBodyBytes)
        {
            throw BodyTooLarge();
        }

        using var buffer = new MemoryStream();
        var chunk = new Byte[8192];
        int read;

        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                throw BodyTooLarge();
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;

        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
    private static BadHttpRequestException BodyTooLarge() => new("Request body too large.", StatusCodes.Status413PayloadTooLarge);
}
