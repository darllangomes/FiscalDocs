using Microsoft.AspNetCore.Hosting;
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

    public ApiFactory(string apiKey = TestApiKey, int permitLimit = DefaultPermitLimit, int windowSeconds = 60)
    {
        _apiKey = apiKey;
        _permitLimit = permitLimit;
        _windowSeconds = windowSeconds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", PostgresFixture.ConnectionString);
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