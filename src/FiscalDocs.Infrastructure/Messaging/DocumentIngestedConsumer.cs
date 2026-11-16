using System.Text.Json;
using FiscalDocs.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class DocumentIngestedConsumer(
    RabbitMqConnectionProvider connections,
    IServiceScopeFactory scopes,
    ILogger<DocumentIngestedConsumer> logger) : IAsyncDisposable
{
    private const ushort PrefetchCount = 10;

    private readonly CancellationTokenSource _stopping = new();
    private IChannel? _channel;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        await channel.BasicQosAsync(0, PrefetchCount, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery);

        await channel.BasicConsumeAsync(
            RabbitMqTopology.DocumentIngestedQueue, autoAck: false, consumer, cancellationToken);

        _channel = channel;
    }

    // Must never throw: an exception escaping the handler would kill the channel.
    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        try
        {
            var @event = TryDeserialize(delivery.Body);
            if (@event is null)
            {
                logger.LogError(
                    "Discarding malformed message (delivery tag {DeliveryTag}).", delivery.DeliveryTag);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<DocumentIngestedProcessor>();
            await processor.ProcessAsync(@event, _stopping.Token);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process message (delivery tag {DeliveryTag}).", delivery.DeliveryTag);
            await TryNackAsync(channel, delivery.DeliveryTag);
        }
    }

    // Transient failure: put it back. The retry/DLQ step replaces this with delayed retries.
    private static async Task TryNackAsync(IChannel channel, ulong deliveryTag)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
        }
        catch
        {
            // Channel already closed: the broker requeues unacked messages on its own.
        }
    }

    private static DocumentIngestedEvent? TryDeserialize(ReadOnlyMemory<byte> body)
    {
        try
        {
            var @event = JsonSerializer.Deserialize<DocumentIngestedEvent>(body.Span, MessagingJson.Options);
            return @event is null || @event.MessageId == Guid.Empty ? null : @event;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        _stopping.Dispose();
    }
}
