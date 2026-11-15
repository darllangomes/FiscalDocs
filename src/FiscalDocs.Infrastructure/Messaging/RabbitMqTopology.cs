using RabbitMQ.Client;

namespace FiscalDocs.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public const string Exchange = "fiscaldocs.events";
    public const string DocumentIngestedQueue = "fiscaldocs.document-ingested";
    public const string DocumentIngestedRoutingKey = "document.ingested";
    public const string DocumentIngestedType = "DocumentIngested";

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            DocumentIngestedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            DocumentIngestedQueue, Exchange, DocumentIngestedRoutingKey, cancellationToken: cancellationToken);
    }
}
