using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class DocumentsEndpointTests
{
    private const string Route = "/api/v1/documents";
    private const string ProblemJson = "application/problem+json";

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

    [SetUp]
    public async Task CleanTable()
    {
        await using var context = InfrastructureFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
    }

    private Task<HttpResponseMessage> PostAsync(string xml, string mediaType = "application/xml") =>
        _client.PostAsync(Route, new StringContent(xml, Encoding.UTF8, mediaType));

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<int> CountAllAsync()
    {
        await using var context = InfrastructureFixture.CreateContext();
        return await context.Documents.IgnoreQueryFilters().CountAsync();
    }

    // ---------- Success ----------

    [TestCase("nfe-cnpj-numerico.xml")]
    [TestCase("nfe-cnpj-alfanumerico.xml")]
    [TestCase("cte-cnpj-numerico.xml")]
    [TestCase("cte-cnpj-alfanumerico.xml")]
    public async Task Post_NewDocument_Returns201WithLocationAndId(string file)
    {
        // Act
        var response = await PostAsync(SampleFiles.ReadAllText(file));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOfAsync(response);
        response.Headers.Location!.ToString().Should().Be($"/api/v1/documents/{id}");
        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task Post_XmlWithUtf8Bom_Returns201()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml")))
            .ToArray();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        // Act
        var response = await _client.PostAsync(Route, content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---------- Idempotency ----------

    [Test]
    public async Task Post_SameDocumentTwice_Returns200WithSameId()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var first = await PostAsync(xml);

        // Act
        var second = await PostAsync(xml);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IdOfAsync(second)).Should().Be(await IdOfAsync(first));
        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task Post_SameKeyWithDifferentContent_Returns409()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        await PostAsync(xml);

        // Act
        var response = await PostAsync(xml + "\n");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ProblemJson);
        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task Post_SameKeyAsDeletedDocument_Returns409()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var id = await IdOfAsync(await PostAsync(xml));
        await using (var context = InfrastructureFixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(d => d.Id == id);
            document.MarkAsDeleted(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        // Act
        var response = await PostAsync(xml);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CountAllAsync()).Should().Be(1);
    }

    [Test]
    public async Task Post_TwentyConcurrentUploadsOfTheSameDocument_CreatesExactlyOne()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(xml)));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(19);

        // Act
        var ids = await Task.WhenAll(responses.Select(IdOfAsync));

        // Assert
        ids.Distinct().Should().ContainSingle();
        (await CountAllAsync()).Should().Be(1);
    }

    // ---------- Invalid input ----------

    [TestCase("invalido.xml")]
    [TestCase("malicioso-xxe.xml")]
    public async Task Post_MalformedOrMaliciousXml_Returns422(string file)
    {
        // Act
        var response = await PostAsync(SampleFiles.ReadAllText(file));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be(ProblemJson);
        (await CountAllAsync()).Should().Be(0);
    }

    [Test]
    public async Task Post_UnsupportedDocumentType_Returns422()
    {
        // Act
        var response = await PostAsync("<CompNfse/>");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task Post_EmptyBody_Returns422()
    {
        // Act
        var response = await PostAsync("");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task Post_UnsupportedContentType_Returns415()
    {
        // Act
        var response = await PostAsync("{}", "application/json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Test]
    public async Task Post_BodyOverTheSizeLimit_Returns413()
    {
        // Act
        var response = await PostAsync("<a>" + new string('x', 2_100_000) + "</a>");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await CountAllAsync()).Should().Be(0);
    }
}
