
namespace FiscalDocs.Application.Ingestion;
public enum IngestOutcome
{
    Created,
    AlreadyExists,
    ContentMismatch,
    DeletedDocument
}