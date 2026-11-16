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
public class GetDocumentXmlEndpointTests
{
    private const string Route = "/api/v1/documents";
    private const string NFeNumericKey = "26260811222333000181550010000010011123456782";

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

    private async Task<Guid> IngestAsync(string xml)
    {
        var response = await _client.PostAsync(Route, new StringContent(xml, Encoding.UTF8, "application/xml"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private Task<Guid> IngestSampleAsync(string file = "nfe-cnpj-numerico.xml") =>
        IngestAsync(SampleFiles.ReadAllText(file));

    // ---------- Conteúdo ----------

    [TestCase("nfe-cnpj-numerico.xml")]
    [TestCase("nfe-cnpj-alfanumerico.xml")]
    [TestCase("cte-cnpj-alfanumerico.xml")]
    [TestCase("nfe-realista-3itens.xml")]
    [TestCase("nfe-realista-990itens.xml")]   // 731 KB: o maior documento que a API aceita na prática
    public async Task ExistingDocument_ReturnsTheOriginalXmlByteForByte(string file)
    {
        // Arrange
        var xml = SampleFiles.ReadAllText(file);
        var id = await IngestAsync(xml);

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(Encoding.UTF8.GetBytes(xml), "sem BOM e sem reformatar nada");
    }

    [Test]
    public async Task NonAsciiCharacters_SurviveTheRoundTripAsUtf8()
    {
        // Arrange
        var original = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        original.Should().Contain("EMPRESA FICTICIA NUMERICA LTDA");
        var xml = original.Replace("EMPRESA FICTICIA NUMERICA LTDA", "EMPRESA FICTÍCIA NUMÉRICA AÇÃO LTDA");
        var id = await IngestAsync(xml);

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes).Should().Contain("EMPRESA FICTÍCIA NUMÉRICA AÇÃO LTDA");
        response.Content.Headers.ContentType!.CharSet.Should().Be("utf-8");
    }

    [Test]
    public async Task IndividualIssuer_ReturnsTheOriginalXmlWithTheFullCpf()
    {
        // O XML é a fonte legal e não é mascarado; a máscara vale só para os campos JSON (ADR 0011).
        // Arrange
        var id = await IngestSampleAsync("nfe-emitente-cpf.xml");

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<CPF>52998224725</CPF>");
    }

    // ---------- Cabeçalhos ----------

    [Test]
    public async Task Response_IsXmlInUtf8()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
    }

    [Test]
    public async Task Response_IsAnAttachmentNamedAfterTheAccessKey()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        var disposition = response.Content.Headers.ContentDisposition!;
        disposition.DispositionType.Should().Be("attachment");
        (disposition.FileNameStar ?? disposition.FileName)!.Trim('"').Should().Be($"{NFeNumericKey}.xml");
    }

    [Test]
    public async Task Response_IsNeverCached()
    {
        // O XML tem o CPF do destinatário: nenhum cache intermediário deve guardá-lo.
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    // ---------- Erros ----------

    [Test]
    public async Task UnknownId_Returns404ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync($"{Route}/{Guid.NewGuid()}/xml");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public async Task UnknownId_WhenTheClientAsksForXml_StillReturns404ProblemDetailsNotA406()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Route}/{Guid.NewGuid()}/xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Test]
    public async Task SoftDeletedDocument_Returns404()
    {
        // Arrange
        var id = await IngestSampleAsync();
        await using (var context = InfrastructureFixture.CreateContext())
        {
            var document = await context.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == id);
            document.MarkAsDeleted(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestCase("not-a-guid")]
    [TestCase("123")]
    public async Task MalformedId_Returns404(string id)
    {
        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

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
        var response = await anonymous.GetAsync($"{Route}/{id}/xml");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- O endpoint de detalhe continua sem o XML ----------

    [Test]
    public async Task DetailEndpoint_StillDoesNotExposeTheXml()
    {
        // Arrange
        var id = await IngestSampleAsync();

        // Act
        var body = await (await _client.GetAsync($"{Route}/{id}")).Content.ReadAsStringAsync();

        // Assert
        body.Should().NotContain("<?xml");
        body.Should().NotContain("<infNFe");
    }
}
