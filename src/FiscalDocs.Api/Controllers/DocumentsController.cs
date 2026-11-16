

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
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IngestDocumentHandler _ingestHandler;
    private readonly GetDocumentHandler _getHandler;
    private readonly GetDocumentXmlHandler _getXmlHandler;
    private readonly ListDocumentsHandler _listHandler;
    private readonly UpdateDocumentHandler _updateHandler;
    private readonly DeleteDocumentHandler _deleteHandler;

    public DocumentsController(
        IngestDocumentHandler ingestHandler,
        GetDocumentHandler getHandler,
        GetDocumentXmlHandler getXmlHandler,
        ListDocumentsHandler listHandler,
        UpdateDocumentHandler updateHandler,
        DeleteDocumentHandler deleteHandler)
    {
        _ingestHandler = ingestHandler;
        _getHandler = getHandler;
        _getXmlHandler = getXmlHandler;
        _listHandler = listHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
    }

    /// <summary>Lista documentos, com filtros e paginação.</summary>
    /// <remarks>
    /// Os filtros se combinam com E. A ordem é da emissão mais recente para a mais antiga, e documentos apagados
    /// não aparecem. Cada item é um resumo, sem o XML e sem as notas: use `GET /api/v1/documents/{id}` para o detalhe.
    /// </remarks>
    /// <response code="200">Uma página de resumos, da emissão mais recente para a mais antiga, com o total de itens.</response>
    /// <response code="400">Algum parâmetro é inválido, como um documento com dígito verificador errado. A resposta lista os campos com erro.</response>
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

    /// <summary>Mostra os dados de um documento.</summary>
    /// <remarks>
    /// O header `ETag` da resposta é a versão atual do documento: envie-o em `If-Match` no `PATCH` (obrigatório)
    /// e no `DELETE` (opcional). O CPF de um emitente pessoa física vem mascarado.
    /// </remarks>
    /// <param name="id">Id do documento, o mesmo devolvido pelo `POST` e pela listagem.</param>
    /// <response code="200">O documento, com o ETag da versão atual no header da resposta.</response>
    /// <response code="404">Não existe documento com esse id, ou ele foi apagado.</response>
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

    /// <summary>Baixa o XML original do documento.</summary>
    /// <remarks>
    /// O XML sai em UTF-8, sem BOM, como anexo nomeado com a chave de acesso. **Não é mascarado**: o CPF do
    /// emitente e o do destinatário saem por inteiro. A resposta leva `Cache-Control: no-store`.
    /// </remarks>
    /// <param name="id">Id do documento.</param>
    /// <response code="200">O XML guardado, em UTF-8, como anexo nomeado com a chave de acesso.</response>
    /// <response code="404">Não existe documento com esse id, ou ele foi apagado.</response>
    [HttpGet("{id:guid}/xml")]
    // Sem [Produces]: ele trocaria o tipo de conteúdo do ProblemDetails do 404 por application/xml e daria 406.
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "application/xml")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetXml(Guid id, CancellationToken cancellationToken)
    {
        var document = await _getXmlHandler.HandleAsync(id, cancellationToken);
        if (document is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Document not found.");
        }

        // O XML tem o CPF do destinatário: nenhum cache, do cliente ou de intermediários, deve guardá-lo.
        Response.Headers.CacheControl = "no-store";

        return File(Utf8WithoutBom.GetBytes(document.Xml), "application/xml; charset=utf-8", $"{document.AccessKey}.xml");
    }

    /// <summary>Altera o status e/ou as notas de um documento.</summary>
    /// <remarks>
    /// Só `status` e `notes` podem mudar; qualquer outro campo no corpo dá `400`. O header `If-Match` é obrigatório
    /// e deve trazer o `ETag` lido em `GET /api/v1/documents/{id}`, entre aspas (por exemplo, `"748"`). Se outra
    /// alteração aconteceu depois da sua leitura, a resposta é `412`: leia de novo e repita.
    /// </remarks>
    /// <param name="id">Id do documento.</param>
    /// <response code="200">Documento atualizado, com o novo ETag no header da resposta.</response>
    /// <response code="400">Corpo inválido: status desconhecido, notas com mais de 500 caracteres, campo não permitido ou corpo vazio.</response>
    /// <response code="404">Não existe documento com esse id, ou ele foi apagado.</response>
    /// <response code="412">O ETag enviado em If-Match não é o da versão atual do documento. Leia de novo e repita.</response>
    /// <response code="415">O corpo precisa ser enviado como application/json.</response>
    /// <response code="428">O header If-Match não foi enviado, e ele é obrigatório neste endpoint.</response>
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

    /// <summary>Apaga um documento (exclusão lógica).</summary>
    /// <remarks>
    /// O documento some das consultas e **não pode ser enviado de novo**: reenviar o mesmo XML dá `409`.
    /// O header `If-Match` é opcional; quando enviado, precisa ser o `ETag` atual.
    /// </remarks>
    /// <param name="id">Id do documento.</param>
    /// <response code="204">Documento apagado. Ele some das consultas e não pode ser reenviado.</response>
    /// <response code="404">Não existe documento com esse id, ou ele já foi apagado.</response>
    /// <response code="412">O ETag enviado em If-Match não é o da versão atual do documento.</response>
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

    /// <summary>Recebe o XML de uma NFe ou de um CTe.</summary>
    /// <remarks>
    /// Envie o XML inteiro no corpo, com `Content-Type: application/xml` (ou `text/xml`), até 2 MiB. A chave de
    /// acesso decide a identidade: reenviar o mesmo XML devolve `200` com o id existente, sem duplicar. O mesmo
    /// XML com conteúdo diferente, ou o de um documento apagado, dá `409`. XML com DTD é rejeitado (`422`).
    /// </remarks>
    /// <response code="201">Documento novo gravado. O id vem no corpo e o header Location aponta para ele.</response>
    /// <response code="200">Esse mesmo XML já tinha sido enviado: nada foi duplicado e o id existente é devolvido.</response>
    /// <response code="409">Já existe um documento com essa chave de acesso e conteúdo diferente, ou ele foi apagado.</response>
    /// <response code="413">O corpo da requisição passa do limite de 2 MiB.</response>
    /// <response code="415">O Content-Type precisa ser application/xml ou text/xml.</response>
    /// <response code="422">O XML é inválido, não é NFe nem CTe, tem DTD ou o emitente não bate com a chave de acesso.</response>
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
