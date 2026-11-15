using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Application.Documents;

/// <summary>Validated filters. Both date bounds are inclusive and interpreted as UTC days.</summary>
public sealed record ListDocumentsQuery(
    int Page,
    int PageSize,
    DocumentType? Type = null,
    DocumentStatus? Status = null,
    Cnpj? IssuerCnpj = null,
    string? IssuerState = null,
    DateOnly? IssuedFrom = null,
    DateOnly? IssuedTo = null);
