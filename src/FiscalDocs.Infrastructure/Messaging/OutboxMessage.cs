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

    // O id da linha é o MessageId do evento: o consumidor vê um id estável, não importa quantas vezes ele seja publicado.
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
