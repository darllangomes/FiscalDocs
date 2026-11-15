namespace FiscalDocs.Application.Ingestion;

public sealed record IngestResult(IngestOutcome Outcome, Guid DocumentId);