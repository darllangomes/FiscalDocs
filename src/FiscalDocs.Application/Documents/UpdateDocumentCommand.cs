using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Application.Documents;

/// <param name="Status">null = mantém o valor atual.</param>
/// <param name="NotesProvided">false = mantém as notas; true com Notes nulo ou em branco = limpa.</param>
public sealed record UpdateDocumentCommand(
    Guid Id, uint ExpectedVersion, DocumentStatus? Status, bool NotesProvided, string? Notes);
