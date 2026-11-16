using FiscalDocs.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FiscalDocs.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{

    public const string TestApiKey = "test-api-key-not-a-secret";

    public const string ApiKeyHeader = "X-Api-Key";

    private const int DefaultPermitLimit = 10_000;

    private readonly string _apiKey;
    private readonly int _permitLimit;
    private readonly int _windowSeconds;

    private readonly string? _connectionString;
    private readonly bool _traceLogs;
    private readonly string? _rabbitMqUri;
    private readonly bool _migrateOnStartup;

    public ApiFactory(
        string apiKey = TestApiKey,
        int permitLimit = DefaultPermitLimit,
        int windowSeconds = 60,
        string? connectionString = null,
        bool traceLogs = true,
        string? rabbitMqUri = null,
        bool migrateOnStartup = false)
    {
        _apiKey = apiKey;
        _permitLimit = permitLimit;
        _windowSeconds = windowSeconds;
        _connectionString = connectionString;
        _traceLogs = traceLogs;
        _rabbitMqUri = rabbitMqUri;
        _migrateOnStartup = migrateOnStartup;
    }

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            if (_traceLogs)
            {
                // Trace só para este provider: a saída de console dos outros testes não muda.
                logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace);
            }
        });

        builder.UseSetting("ConnectionStrings:Default", _connectionString ?? InfrastructureFixture.ConnectionString);
        builder.UseSetting("RabbitMq:Uri", _rabbitMqUri ?? InfrastructureFixture.RabbitMqUri);
        builder.UseSetting("Auth:ApiKey", _apiKey);
        builder.UseSetting("Database:MigrateOnStartup", _migrateOnStartup.ToString());
        builder.UseSetting("RateLimiting:PermitLimit", _permitLimit.ToString());
        builder.UseSetting("RateLimiting:WindowSeconds", _windowSeconds.ToString());
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeader, TestApiKey);
        return client;
    }

}
