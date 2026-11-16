using System.Net;
using System.Text;
using FluentAssertions;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class ApiKeyAuthenticationTests
{
    private const string Route = "/api/v1/documents";

    private ApiFactory _factory = null!;

    [OneTimeSetUp]
    public void Start() => _factory = new ApiFactory();

    [OneTimeTearDown]
    public void Stop() => _factory.Dispose();

    private static StringContent EmptyXml() => new("", Encoding.UTF8, "application/xml");

    [Test]
    public async Task MissingKey_Returns401ProblemDetails()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync(Route, EmptyXml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [TestCase("wrong-key")]
    [TestCase("")]
    [TestCase("test-api-key-not-a-secret ")] // espaço no fim é outra chave
    [TestCase("TEST-API-KEY-NOT-A-SECRET")] // a comparação diferencia maiúsculas
    public async Task InvalidKey_Returns401(string key)
    {
        // Arrange
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(ApiFactory.ApiKeyHeader, key);

        // Act
        var response = await client.PostAsync(Route, EmptyXml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task InvalidKey_ResponseNeverContainsTheExpectedKey()
    {
        // Arrange
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiFactory.ApiKeyHeader, "wrong-key");

        // Act
        var response = await client.PostAsync(Route, EmptyXml());

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(ApiFactory.TestApiKey);
    }

    [Test]
    public async Task ValidKey_PassesAuthentication()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        // Corpo vazio: chega ao handler e falha na validação (422), sem tocar no banco.
        var response = await client.PostAsync(Route, EmptyXml());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void MissingConfiguration_FailsAtStartup(string configuredKey)
    {
        // Arrange
        using var factory = new ApiFactory(configuredKey);

        // Act
        var act = () => factory.CreateClient();

        // Assert
        act.Should().Throw<Exception>().Which.Message.Should().Contain("Auth:ApiKey");
    }
}
