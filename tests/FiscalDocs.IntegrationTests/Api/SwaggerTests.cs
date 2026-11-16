using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class SwaggerTests
{
    private const string DocumentPath = "/swagger/v1/swagger.json";

    private ApiFactory _baseFactory = null!;
    private HttpClient _client = null!;
    private JsonElement _document;

    [OneTimeSetUp]
    public async Task LoadDocument()
    {
        _baseFactory = new ApiFactory();
        var development = _baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _client = development.CreateClient();

        var response = await _client.GetAsync(DocumentPath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        _document = parsed.RootElement.Clone();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _client.Dispose();
        _baseFactory.Dispose();
    }

    [Test]
    public void Swagger_DescribesTheApi()
    {
        // Act
        var info = _document.GetProperty("info");

        // Assert
        info.GetProperty("title").GetString().Should().Be("FiscalDocs API");
        info.GetProperty("version").GetString().Should().Be("v1");
        info.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void Swagger_DeclaresTheApiKeyHeaderScheme()
    {
        // Act
        var scheme = _document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");

        // Assert
        scheme.GetProperty("type").GetString().Should().Be("apiKey");
        scheme.GetProperty("in").GetString().Should().Be("header");
        scheme.GetProperty("name").GetString().Should().Be("X-Api-Key");
    }

    [Test]
    public void Swagger_RequiresTheApiKeyOnEveryOperation()
    {
        // Act
        // No OpenAPI a exigência da raiz vale para toda operação que não declara a sua.
        var documentSecurity = _document.TryGetProperty("security", out var root)
            ? root.EnumerateArray().ToList()
            : [];
        var operations = _document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(op => (Path: path.Name, Method: op.Name, Operation: op.Value)))
            .ToList();

        // Assert
        operations.Should().NotBeEmpty();
        foreach (var (path, method, operation) in operations)
        {
            var effective = operation.TryGetProperty("security", out var security)
                ? security.EnumerateArray().ToList()
                : documentSecurity;
            var requirements = effective.Where(r => r.TryGetProperty("ApiKey", out _)).ToList();
            requirements.Should().NotBeEmpty($"{method.ToUpperInvariant()} {path} must require the API key");
        }
    }

    [Test]
    public void Swagger_ListsTheDocumentEndpointsButNotTheHealthChecks()
    {
        // Act
        var paths = _document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        // Assert
        paths.Should().Contain(["/api/v1/documents", "/api/v1/documents/{id}"]);
        paths.Should().NotContain(p => p.StartsWith("/health"));
    }

    [Test]
    public async Task Swagger_OutsideDevelopment_IsNotExposed()
    {
        // Arrange
        using var production = _baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();

        // Act
        var response = await client.GetAsync(DocumentPath);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("openapi");
    }
}
