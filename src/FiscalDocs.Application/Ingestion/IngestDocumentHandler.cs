using FiscalDocs.Application.Abstractions;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Domain.Entities;

namespace FiscalDocs.Application.Ingestion;

public sealed class IngestDocumentHandler
{
    private readonly IXmlDocumentLoader _loader;
    private readonly DocumentParserResolver _resolver;
    private readonly IFiscalDocumentRepository _repository;
    private readonly TimeProvider _clock;

    public IngestDocumentHandler(
        IXmlDocumentLoader loader,
        DocumentParserResolver resolver,
        IFiscalDocumentRepository repository,
        TimeProvider clock)
    {
        _loader = loader;
        _resolver = resolver;
        _repository = repository;
        _clock = clock;
    }

    public async Task<IngestResult> HandleAsync(string xml, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new InvalidDocumentException("The XML document is empty.");
        }

        var xmlDocument = _loader.Load(xml);
        var parsed = _resolver.Resolve(xmlDocument).Parse(xmlDocument);

        var document = CreateDocument(parsed, xml);

        // Insert first and let the unique index decide: no check-then-insert race.
        if (await _repository.TryAddAsync(document, cancellationToken))
        {
            return new IngestResult(IngestOutcome.Created, document.Id);
        }

        var existing = await _repository.FindByAccessKeyAsync(document.AccessKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Access key {document.AccessKey.Value} was reported as duplicate but no document was found.");

        var outcome = existing.IsDeleted
            ? IngestOutcome.DeletedDocument
            : existing.XmlHash == document.XmlHash
                ? IngestOutcome.AlreadyExists
                : IngestOutcome.ContentMismatch;

        return new IngestResult(outcome, existing.Id);
    }

    private FiscalDocument CreateDocument(ParsedDocument parsed, string xml)
    {
        try
        {
            return FiscalDocument.Create(
                parsed.Type,
                parsed.AccessKey,
                parsed.Number,
                parsed.Series,
                parsed.IssueDate,
                parsed.IssuerCnpj,
                parsed.IssuerName,
                parsed.IssuerState,
                parsed.RecipientDocument,
                parsed.RecipientName,
                parsed.RecipientState,
                parsed.TotalAmount,
                xml,
                _clock.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            // The entity rejected values the XML parser accepted (e.g. a state with 3 letters).
            // The message stays generic because it can reach the client.
            throw new InvalidDocumentException("Document data is invalid.", ex);
        }
    }
}