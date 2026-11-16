using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class HttpsRedirectionTests
{
    private ApiFactory _baseFactory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _baseFactory = new ApiFactory();
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.UseSetting("https_port", "8443"));
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        _baseFactory.Dispose();
    }

    [Test]
    public async Task OtherRoutes_AreRedirectedToHttps()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/documents");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location!.ToString().Should().StartWith("https://localhost:8443/");
    }

    [TestCase("/health/live")]
    [TestCase("/health/ready")]
    public async Task HealthEndpoints_AreNotRedirected(string path)
    {
        // Arrange
        (await _client.GetAsync("/api/v1/documents")).StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);

        // Act
        var response = await _client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Location.Should().BeNull();
    }
}
