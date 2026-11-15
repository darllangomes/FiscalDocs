using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Application.Documents;

/// <param name="Status">null = leave unchanged.</param>
/// <param name="NotesProvided">false = leave notes unchanged; true with null/blank Notes = clear.</param>
public sealed record UpdateDocumentCommand(
    Guid Id, uint ExpectedVersion, DocumentStatus? Status, bool NotesProvided, string? Notes);
