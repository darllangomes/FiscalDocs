using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class LogPrivacyTests
{
    private const string Route = "/api/v1/documents";
    private const string Cpf = "12345678909";
    private const string FormattedCpf = "123.456.789-09";

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

    private static string SampleWithCpf()
    {
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        xml.Should().Contain($"<CPF>{Cpf}</CPF>", "o teste precisa de um sample que realmente tenha CPF");
        return xml;
    }

    private Task<HttpResponseMessage> PostAsync(string xml) =>
        _client.PostAsync(Route, new StringContent(xml, Encoding.UTF8, "application/xml"));

    [Test]
    public async Task RejectedDocument_IsLoggedOnceWithoutStackTrace()
    {
        // Arrange
        using var factory = new ApiFactory(traceLogs: false);
        using var client = factory.CreateAuthenticatedClient();
        var content = new StringContent(SampleWithCpf() + "<oops>", Encoding.UTF8, "application/xml");

        // Act
        var response = await client.PostAsync(Route, content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        factory.Logs.Entries.Should().ContainSingle(e => e.Contains("Request rejected"));
        factory.Logs.Entries.Should().NotContain(e => e.Contains("ExceptionHandlerMiddleware"));
        factory.Logs.Entries.Should().NotContain(e => e.Contains("   at "));
    }

    private void AssertNothingSensitiveWasLogged()
    {
        var entries = _factory.Logs.Entries.ToArray();

        entries.Should().NotBeEmpty("uma captura vazia faria as verificações abaixo passarem sem provar nada");
        entries.Should().NotContain(e => e.Contains(Cpf), "o CPF completo nunca deve ser logado");
        entries.Should().NotContain(e => e.Contains(FormattedCpf), "o CPF formatado nunca deve ser logado");
        entries.Should().NotContain(
            e => e.Contains("<?xml") || e.Contains("<CPF>") || e.Contains("<infNFe"),
            "o corpo XML nunca deve ser logado");
    }

    [Test]
    public async Task SuccessfulIngestion_DoesNotLogCpfOrXml()
    {
        // Act
        var response = await PostAsync(SampleWithCpf());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        AssertNothingSensitiveWasLogged();
    }

    [Test]
    public async Task IngestionOfAnIndividualIssuer_DoesNotLogTheIssuerCpf()
    {
        // Act
        var response = await PostAsync(SampleFiles.ReadAllText("nfe-emitente-cpf.xml"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var entries = _factory.Logs.Entries.ToArray();
        entries.Should().NotBeEmpty("uma captura vazia faria as verificações abaixo passarem sem provar nada");
        entries.Should().NotContain(e => e.Contains("52998224725"), "o CPF do emitente nunca deve ser logado");
        entries.Should().NotContain(e => e.Contains("529.982.247-25"));
    }

    [Test]
    public async Task XmlDownload_DoesNotLogCpfOrXml()
    {
        // Arrange
        var created = await PostAsync(SampleWithCpf());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Act
        var response = await _client.GetAsync($"{Route}/{id}/xml");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain($"<CPF>{Cpf}</CPF>");
        AssertNothingSensitiveWasLogged();
    }

    [Test]
    public async Task DuplicateIngestion_DoesNotLogCpfOrXml()
    {
        // Arrange
        var xml = SampleWithCpf();

        // Act
        var first = await PostAsync(xml);
        var second = await PostAsync(xml);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertNothingSensitiveWasLogged();
    }

    [Test]
    public async Task RejectedDocument_DoesNotLogCpfOrXml()
    {
        // XML malformado (dois elementos raiz), mas ainda com o CPF no corpo.
        // Act
        var response = await PostAsync(SampleWithCpf() + "<oops>");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _factory.Logs.Entries.Should().Contain(e => e.Contains("Request rejected"),
            "prova que a captura enxerga os logs da própria aplicação");
        AssertNothingSensitiveWasLogged();
    }
}
