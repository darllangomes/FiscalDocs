namespace FiscalDocs.Infrastructure.Messaging;

public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
