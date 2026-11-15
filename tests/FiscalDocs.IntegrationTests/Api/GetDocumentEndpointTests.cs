using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class GetDocumentEndpointTests
{
    private const string Route = "/api/v1/documents";

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
        await using var context = PostgresFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE fiscal_documents");
    }

    private async Task<Guid> IngestSampleAsync(string file = "nfe-cnpj-numerico.xml")
    {
        var content = new StringContent(SampleFiles.ReadAllText(file), Encoding.UTF8, "application/xml");
        var response = await _client.PostAsync(Route, content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Test]
    public async Task ExistingDocument_Returns200WithDocumentFields()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await _client.GetAsync($"{Route}/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("id").GetGuid().Should().Be(id);
        doc.GetProperty("type").GetString().Should().Be("NFe");
        doc.GetProperty("status").GetString().Should().Be("Received");
        doc.GetProperty("accessKey").GetString().Should().Be("26260811222333000181550010000010011123456782");
        doc.GetProperty("number").GetString().Should().Be("1001");
        doc.GetProperty("series").GetString().Should().Be("1");
        doc.GetProperty("issueDate").GetDateTimeOffset()
            .Should().Be(DateTimeOffset.Parse("2026-08-14T10:30:00-03:00"));
        doc.GetProperty("issuerCnpj").GetString().Should().Be("11222333000181");
        doc.GetProperty("issuerName").GetString().Should().Be("EMPRESA FICTICIA NUMERICA LTDA");
        doc.GetProperty("issuerState").GetString().Should().Be("PE");
        doc.GetProperty("recipientName").GetString().Should().Be("CLIENTE FICTICIO DA SILVA");
        doc.GetProperty("recipientState").GetString().Should().Be("SP");
        doc.GetProperty("totalAmount").GetDecimal().Should().Be(1500.00m);
    }

    [Test]
    public async Task ExistingDocument_DoesNotExposeRawXml()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await _client.GetAsync($"{Route}/{id}");

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("rawXml", out _).Should().BeFalse();
        body.Should().NotContain("<?xml");
    }

    [Test]
    public async Task ExistingDocument_ReturnsStrongQuotedEtag_StableAcrossReads()
    {
        // Arrange
        var id = await IngestSampleAsync();
        var first = await _client.GetAsync($"{Route}/{id}");

        // Act
        var second = await _client.GetAsync($"{Route}/{id}");

        // Assert
        first.Headers.ETag.Should().NotBeNull();
        first.Headers.ETag!.IsWeak.Should().BeFalse();
        first.Headers.ETag.Tag.Should().MatchRegex("^\"[^\"]+\"$");
        second.Headers.ETag.Should().Be(first.Headers.ETag);
    }

    [Test]
    public async Task UnknownId_Returns404ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync($"{Route}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public async Task SoftDeletedDocument_Returns404()
    {
        // Arrange
        var id = await IngestSampleAsync();
        await using (var context = PostgresFixture.CreateContext())
        {
            var document = await context.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == id);
            document.MarkAsDeleted(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"{Route}/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestCase("not-a-guid")]
    [TestCase("123")]
    public async Task MalformedId_Returns404(string id)
    {
        // Act
        var response = await _client.GetAsync($"{Route}/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task WithoutApiKey_Returns401()
    {
        // Arrange
        var id = await IngestSampleAsync();
        using var anonymous = _factory.CreateClient();

        // Act
        var response = await anonymous.GetAsync($"{Route}/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
