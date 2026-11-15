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
        _client = _factory.CreateAuthenticatedClient();
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
        // Act
        var response = await _client.GetAsync("/does-not-exist");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public void IngestHandler_ResolvesFromTheContainer()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();

        // Act
        var handler = scope.ServiceProvider.GetService<IngestDocumentHandler>();

        // Assert
        handler.Should().NotBeNull();
    }
}
