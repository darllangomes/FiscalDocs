using FiscalDocs.Domain.Enums;

namespace FiscalDocs.Application.Messaging;

public sealed record DocumentIngestedEvent(
    Guid MessageId,
    Guid DocumentId,
    DocumentType Type,
    string IssuerDocument,
    string IssuerState,
    DateTimeOffset IssueDate,
    decimal TotalAmount,
    DateTimeOffset OccurredAt);
