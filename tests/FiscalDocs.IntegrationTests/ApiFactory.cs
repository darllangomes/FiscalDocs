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

    public ApiFactory(
        string apiKey = TestApiKey,
        int permitLimit = DefaultPermitLimit,
        int windowSeconds = 60,
        string? connectionString = null,
        bool traceLogs = true)
    {
        _apiKey = apiKey;
        _permitLimit = permitLimit;
        _windowSeconds = windowSeconds;
        _connectionString = connectionString;
        _traceLogs = traceLogs;
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

        builder.UseSetting("ConnectionStrings:Default", _connectionString ?? PostgresFixture.ConnectionString);
        builder.UseSetting("Auth:ApiKey", _apiKey);
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
