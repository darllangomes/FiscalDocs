using RabbitMQ.Client;

namespace FiscalDocs.Infrastructure.Messaging;

public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options { get; }

    public RabbitMqConnectionProvider(RabbitMqOptions options)
    {
        Options = options;
        _factory = new ConnectionFactory
        {
            Uri = new Uri(options.Uri),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(options.ConnectionTimeoutSeconds)
        };
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        // Conexão fechada não é substituída: o cliente a recupera sozinho, junto com seus
        // canais e consumidores. Substituí-la descartaria o canal do consumidor para sempre.
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _connection ??= await _factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _gate.Dispose();
    }
}
