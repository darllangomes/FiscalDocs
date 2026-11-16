using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;

namespace FiscalDocs.Application.Documents;

/// <summary>Linha da listagem: sem XML bruto e sem notas.</summary>
public sealed record DocumentSummary(
    Guid Id, DocumentType Type, DocumentStatus Status, string AccessKey,
    string Number, string Series, DateTimeOffset IssueDate,
    TaxId IssuerDocument, string IssuerName, string IssuerState,
    decimal TotalAmount, DateTimeOffset UpdatedAt);
