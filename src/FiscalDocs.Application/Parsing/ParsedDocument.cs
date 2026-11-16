using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;


namespace FiscalDocs.Application.Parsing;

public sealed record ParsedDocument(
    DocumentType Type,
    AccessKey AccessKey,
    string Number,
    string Series,
    DateTimeOffset IssueDate,
    TaxId IssuerDocument,
    string IssuerName,
    string IssuerState,
    string? RecipientDocument,
    string? RecipientName,
    string? RecipientState,
    decimal TotalAmount

);
