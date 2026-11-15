namespace FiscalDocs.Application.Documents;

public enum UpdateDocumentOutcome
{
    Updated,
    NotFound,
    PreconditionFailed,
    Invalid
}

public sealed record UpdateDocumentResult(
    UpdateDocumentOutcome Outcome, DocumentDetails? Details = null, string? Error = null);
