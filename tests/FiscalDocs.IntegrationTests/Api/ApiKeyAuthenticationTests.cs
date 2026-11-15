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
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Route, EmptyXml());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [TestCase("wrong-key")]
    [TestCase("")]
    [TestCase("test-api-key-not-a-secret ")] // trailing space is a different key
    [TestCase("TEST-API-KEY-NOT-A-SECRET")] // comparison is case-sensitive
    public async Task InvalidKey_Returns401(string key)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(ApiFactory.ApiKeyHeader, key);

        var response = await client.PostAsync(Route, EmptyXml());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task InvalidKey_ResponseNeverContainsTheExpectedKey()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiFactory.ApiKeyHeader, "wrong-key");

        var response = await client.PostAsync(Route, EmptyXml());
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(ApiFactory.TestApiKey);
    }

    [Test]
    public async Task ValidKey_PassesAuthentication()
    {
        using var client = _factory.CreateAuthenticatedClient();

        // Empty body: reaches the handler and fails validation (422), without touching the database.
        var response = await client.PostAsync(Route, EmptyXml());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void MissingConfiguration_FailsAtStartup(string configuredKey)
    {
        using var factory = new ApiFactory(configuredKey);

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.Message.Should().Contain("Auth:ApiKey");
    }
}