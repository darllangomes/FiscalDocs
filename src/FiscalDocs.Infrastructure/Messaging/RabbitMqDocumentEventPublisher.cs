using System.Text.Json;
using System.Text.Json.Serialization;
using FiscalDocs.Application.Abstractions;
using FiscalDocs.Application.Messaging;
using RabbitMQ.Client;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class RabbitMqDocumentEventPublisher : IDocumentEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RabbitMqConnectionProvider _connections;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public RabbitMqDocumentEventPublisher(RabbitMqConnectionProvider connections) => _connections = connections;

    public async Task PublishAsync(DocumentIngestedEvent @event, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions);
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            MessageId = @event.MessageId.ToString(),
            Type = RabbitMqTopology.DocumentIngestedType,
            Timestamp = new AmqpTimestamp(@event.OccurredAt.ToUnixTimeSeconds())
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(
                RabbitMqTopology.Exchange,
                RabbitMqTopology.DocumentIngestedRoutingKey,
                mandatory: true,
                properties,
                body,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        _gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        var connection = await _connections.GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
        _channel = channel;
        return channel;
    }
}
