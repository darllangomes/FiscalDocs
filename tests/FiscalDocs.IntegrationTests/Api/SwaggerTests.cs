using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

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

    // ---------- Documentação para quem testa à mão ----------

    private JsonElement Operation(string path, string method) =>
        _document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static JsonElement[] ParametersOf(JsonElement operation) =>
        operation.TryGetProperty("parameters", out var parameters) ? parameters.EnumerateArray().ToArray() : [];

    private static string Description(JsonElement element) =>
        element.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

    [Test]
    public void Swagger_EveryOperationHasASummary()
    {
        // Act
        var operations = _document.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(o => (Name: $"{o.Name.ToUpperInvariant()} {p.Name}", o.Value)))
            .ToList();

        // Assert
        operations.Should().NotBeEmpty();
        foreach (var (name, operation) in operations)
        {
            operation.TryGetProperty("summary", out var summary).Should().BeTrue($"{name} needs a summary");
            summary.GetString().Should().NotBeNullOrWhiteSpace($"{name} needs a summary");
        }
    }

    [Test]
    public void Swagger_InfoExplainsHowToAuthenticate()
    {
        // Act
        var description = _document.GetProperty("info").GetProperty("description").GetString();

        // Assert
        description.Should().Contain("X-Api-Key").And.Contain("Authorize");
    }

    [Test]
    public void Swagger_IngestEndpoint_DocumentsTheXmlBody()
    {
        // O corpo é lido direto do fluxo da requisição, então sem esforço extra o Swagger não mostraria campo nenhum.
        // Act
        var body = Operation("/api/v1/documents", "post").GetProperty("requestBody");

        // Assert
        body.GetProperty("required").GetBoolean().Should().BeTrue();
        var content = body.GetProperty("content");
        content.TryGetProperty("application/xml", out _).Should().BeTrue();
        content.TryGetProperty("text/xml", out _).Should().BeTrue();
    }

    [Test]
    public async Task Swagger_IngestExample_IsAValidDocumentTheApiAccepts()
    {
        // Arrange
        var example = Operation("/api/v1/documents", "post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/xml")
            .GetProperty("example").GetString();
        example.Should().NotBeNullOrWhiteSpace();
        var parsed = () => XDocument.Parse(example!);
        parsed.Should().NotThrow("o exemplo precisa ser um XML bem formado");

        // O exemplo tem a mesma chave de acesso do sample nfe-cnpj-numerico, que outros testes deixam no banco
        // (às vezes apagado ou com outro conteúdo, o que daria 409). Começar de uma tabela limpa evita a interferência.
        await using (var context = InfrastructureFixture.CreateContext())
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");

        using var client = _baseFactory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsync(
            "/api/v1/documents", new StringContent(example!, Encoding.UTF8, "application/xml"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public void Swagger_PatchDeclaresARequiredIfMatchHeader_AndDeleteAnOptionalOne()
    {
        // Sem o parâmetro declarado, o Swagger UI não tem onde digitar o ETag e o PATCH sempre daria 428.
        // Act
        var patch = ParametersOf(Operation("/api/v1/documents/{id}", "patch"))
            .Single(p => p.GetProperty("name").GetString() == "If-Match");
        var delete = ParametersOf(Operation("/api/v1/documents/{id}", "delete"))
            .Single(p => p.GetProperty("name").GetString() == "If-Match");

        // Assert
        patch.GetProperty("in").GetString().Should().Be("header");
        patch.GetProperty("required").GetBoolean().Should().BeTrue();
        Description(patch).Should().Contain("ETag");
        delete.GetProperty("in").GetString().Should().Be("header");
        (delete.TryGetProperty("required", out var required) && required.GetBoolean()).Should().BeFalse();
    }

    [Test]
    public void Swagger_ListFilters_AreAllDescribed_AndTheOldNameIsHidden()
    {
        // Act
        var parameters = ParametersOf(Operation("/api/v1/documents", "get"));
        var names = parameters.Select(p => p.GetProperty("name").GetString()).ToList();

        // Assert
        names.Should().BeEquivalentTo(
            ["page", "pageSize", "type", "status", "issuerDocument", "issuerState", "issuedFrom", "issuedTo"]);
        foreach (var parameter in parameters)
            Description(parameter).Should().NotBeNullOrWhiteSpace($"{parameter.GetProperty("name")} needs a description");
    }

    [Test]
    public void Swagger_EveryResponseHasADescriptiveText()
    {
        // O padrão do Swashbuckle é a frase do status ("Not Found"). Aqui exigimos um texto que explique o caso.
        // Act
        var responses = _document.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().SelectMany(o =>
                o.Value.GetProperty("responses").EnumerateObject()
                    .Select(r => (Name: $"{o.Name.ToUpperInvariant()} {p.Name} {r.Name}", Text: Description(r.Value)))))
            .ToList();

        // Assert
        responses.Should().NotBeEmpty();
        foreach (var (name, text) in responses)
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should()
                .BeGreaterThanOrEqualTo(4, $"{name} needs a description that explains the case");
    }

    [TestCase("/api/v1/documents/{id}", "get")]
    [TestCase("/api/v1/documents/{id}", "patch")]
    public void Swagger_DocumentsTheETagHeaderOfTheSuccessResponse(string path, string method)
    {
        // Act
        var ok = Operation(path, method).GetProperty("responses").GetProperty("200");

        // Assert
        ok.GetProperty("headers").TryGetProperty("ETag", out _).Should().BeTrue();
    }

    [Test]
    public void Swagger_XmlDownload_IsDocumentedAsXml()
    {
        // Act
        var ok = Operation("/api/v1/documents/{id}/xml", "get").GetProperty("responses").GetProperty("200");

        // Assert
        ok.GetProperty("content").TryGetProperty("application/xml", out _).Should().BeTrue();
    }

    [TestCase("DocumentResponse")]
    [TestCase("DocumentSummaryResponse")]
    [TestCase("DocumentIngestedResponse")]
    [TestCase("PatchDocumentRequest")]
    public void Swagger_SchemaProperties_HaveDescriptionsAndExamples(string schema)
    {
        // Act
        var properties = _document.GetProperty("components").GetProperty("schemas")
            .GetProperty(schema).GetProperty("properties").EnumerateObject().ToList();

        // Assert
        properties.Should().NotBeEmpty();
        foreach (var property in properties)
        {
            Description(property.Value).Should().NotBeNullOrWhiteSpace($"{schema}.{property.Name} needs a description");
            property.Value.TryGetProperty("example", out _).Should().BeTrue($"{schema}.{property.Name} needs an example");
        }
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
