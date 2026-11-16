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
    private const string AttemptHeader = "x-attempt";

    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _retryGate = new(1, 1);
    private IReadOnlyList<TimeSpan> _retryDelays = [];
    private IChannel? _channel;
    private IChannel? _retryChannel;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _retryDelays = connections.Options.RetryDelays;
        var connection = await connections.GetConnectionAsync(cancellationToken);

        var retryChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(retryChannel, cancellationToken);
        await RabbitMqTopology.DeclareRetryAsync(retryChannel, _retryDelays, cancellationToken);
        _retryChannel = retryChannel;

        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, PrefetchCount, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery);

        await channel.BasicConsumeAsync(
            RabbitMqTopology.DocumentIngestedQueue, autoAck: false, consumer, cancellationToken);

        _channel = channel;
    }

    // Nunca pode lançar exceção: uma exceção que escapasse do handler derrubaria o canal.
    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        try
        {
            var @event = TryDeserialize(delivery.Body);
            if (@event is null)
            {
                logger.LogError(
                    "Dead-lettering malformed message (delivery tag {DeliveryTag}).", delivery.DeliveryTag);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IDocumentIngestedProcessor>();
                await processor.ProcessAsync(@event, _stopping.Token);
            }
            catch (Exception ex) when (!_stopping.IsCancellationRequested)
            {
                await HandleFailureAsync(channel, delivery, @event, ex);
                return;
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error handling delivery tag {DeliveryTag}.", delivery.DeliveryTag);
            await TryNackAsync(channel, delivery.DeliveryTag, requeue: true);
        }
    }

    private async Task HandleFailureAsync(
        IChannel channel, BasicDeliverEventArgs delivery, DocumentIngestedEvent @event, Exception exception)
    {
        var attempt = ReadAttempt(delivery.BasicProperties);

        if (attempt >= _retryDelays.Count)
        {
            logger.LogError(
                exception,
                "Dead-lettering message {MessageId} after {Deliveries} failed deliveries.",
                @event.MessageId,
                attempt + 1);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        logger.LogWarning(
            exception,
            "Failed to process message {MessageId} (attempt {Attempt}); retrying in {Delay}.",
            @event.MessageId,
            attempt + 1,
            _retryDelays[attempt]);

        try
        {
            await PublishRetryAsync(delivery, attempt + 1);
        }
        catch (Exception publishException)
        {
            logger.LogError(
                publishException, "Could not schedule a retry for message {MessageId}; requeueing.", @event.MessageId);
            await TryNackAsync(channel, delivery.DeliveryTag, requeue: true);
            return;
        }

        // O ack só sai depois que o broker confirmar a cópia do retry.
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
    }

    private async Task PublishRetryAsync(BasicDeliverEventArgs delivery, int attempt)
    {
        var source = delivery.BasicProperties;
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = source.ContentType,
            MessageId = source.MessageId,
            Type = source.Type,
            Headers = new Dictionary<string, object?> { [AttemptHeader] = attempt }
        };
        if (source.Timestamp.UnixTime != 0)
        {
            properties.Timestamp = source.Timestamp;
        }

        await _retryGate.WaitAsync(_stopping.Token);
        try
        {
            await _retryChannel!.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: RabbitMqTopology.RetryQueueName(attempt),
                mandatory: true,
                properties,
                delivery.Body,
                _stopping.Token);
        }
        finally
        {
            _retryGate.Release();
        }
    }

    private static int ReadAttempt(IReadOnlyBasicProperties properties) =>
        properties.Headers is not null
        && properties.Headers.TryGetValue(AttemptHeader, out var value)
        && value is not null
            ? Convert.ToInt32(value)
            : 0;

    private static async Task TryNackAsync(IChannel channel, ulong deliveryTag, bool requeue)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue);
        }
        catch
        {
            // Canal já fechado: o broker recoloca na fila as mensagens sem ack por conta própria.
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

        if (_retryChannel is not null)
        {
            await _retryChannel.DisposeAsync();
            _retryChannel = null;
        }

        _stopping.Dispose();
        _retryGate.Dispose();
    }
}
