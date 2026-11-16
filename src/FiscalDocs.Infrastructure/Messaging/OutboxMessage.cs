using System.Text.Json;
using FiscalDocs.Application.Messaging;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    // The row id is the event's MessageId, so consumers see a stable id however often it is published.
    public static OutboxMessage From(DocumentIngestedEvent @event) => new()
    {
        Id = @event.MessageId,
        Type = RabbitMqTopology.DocumentIngestedType,
        Payload = JsonSerializer.Serialize(@event, MessagingJson.Options),
        OccurredAt = @event.OccurredAt
    };

    public DocumentIngestedEvent ToEvent() =>
        JsonSerializer.Deserialize<DocumentIngestedEvent>(Payload, MessagingJson.Options)
        ?? throw new InvalidOperationException($"Outbox message {Id} has an empty payload.");
}
