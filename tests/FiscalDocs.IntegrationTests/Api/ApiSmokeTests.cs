using System.Net;
using FiscalDocs.Application.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class ApiSmokeTests
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        var response = await _client.GetAsync("/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public void IngestHandler_ResolvesFromTheContainer()
    {
        using var scope = _factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetService<IngestDocumentHandler>();

        handler.Should().NotBeNull();
    }
}