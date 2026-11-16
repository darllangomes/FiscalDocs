using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Application.Documents;

/// <summary>Filtros já validados. Os dois limites de data são inclusivos e interpretados como dias UTC.</summary>
public sealed record ListDocumentsQuery(
    int Page,
    int PageSize,
    DocumentType? Type = null,
    DocumentStatus? Status = null,
    Cnpj? IssuerCnpj = null,
    string? IssuerState = null,
    DateOnly? IssuedFrom = null,
    DateOnly? IssuedTo = null);
