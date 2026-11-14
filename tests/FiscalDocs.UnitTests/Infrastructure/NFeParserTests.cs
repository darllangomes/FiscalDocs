using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using FiscalDocs.Infrastructure.Xml;
using FiscalDocs.UnitTests.Support;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Infrastructure;

[TestFixture]
public class NFeParserTests
{
    private readonly NFeParser _parser = new();

    private static XDocument Load(string file)
    {
        using var stream = SampleFiles.Open(file);
        return SafeXmlLoader.Load(stream);
    }

    [TestCase("nfe-cnpj-numerico.xml", true)]
    [TestCase("nfe-cnpj-alfanumerico.xml", true)]
    [TestCase("cte-cnpj-numerico.xml", false)]
    [TestCase("cte-cnpj-alfanumerico.xml", false)]
    public void CanParse_ReturnsTrueOnlyForNFe(string file, bool expected)
    {
        _parser.CanParse(Load(file)).Should().Be(expected);
    }

    [Test]
    public void Parse_NFeWithNumericCnpj_ExtractsAllFields()
    {
        var result = _parser.Parse(Load("nfe-cnpj-numerico.xml"));

        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.NFe,
            AccessKey.Parse("26260811222333000181550010000010011123456782"),
            "1001",
            "1",
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
            Cnpj.Parse("11222333000181"),
            "EMPRESA FICTICIA NUMERICA LTDA",
            "PE",
            "12345678909",
            "CLIENTE FICTICIO DA SILVA",
            "SP",
            1500.00m));
    }

    [Test]
    public void Parse_NFeWithAlphanumericCnpj_ExtractsAllFields()
    {
        var result = _parser.Parse(Load("nfe-cnpj-alfanumerico.xml"));

        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.NFe,
            AccessKey.Parse("26260812ABC34501DE35550010000020021234567895"),
            "2002",
            "1",
            new DateTimeOffset(2026, 8, 20, 14, 15, 0, TimeSpan.FromHours(-3)),
            Cnpj.Parse("12ABC34501DE35"),
            "EMPRESA FICTICIA ALFANUMERICA LTDA",
            "PE",
            "99888777000100",
            "COMERCIAL DESTINO FICTICIA S/A",
            "BA",
            8750.50m));
    }

    [Test]
    public void Parse_RootWithoutProtocolEnvelope_Works()
    {
        var withProtocol = Load("nfe-cnpj-numerico.xml");
        var nfeOnly = new XDocument(new XElement(
            withProtocol.Root!.Elements().First(e => e.Name.LocalName == "NFe")));

        _parser.CanParse(nfeOnly).Should().BeTrue();
        _parser.Parse(nfeOnly).Number.Should().Be("1001");
    }

    [Test]
    public void Parse_MissingIssuer_ThrowsInvalidDocumentException()
    {
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Where(e => e.Name.LocalName == "emit").Remove();

        var act = () => _parser.Parse(document);

        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_InvalidTotalAmount_ThrowsInvalidDocumentException()
    {
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Single(e => e.Name.LocalName == "vNF").Value = "abc";

        var act = () => _parser.Parse(document);

        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_WithoutRecipient_ReturnsNullRecipientFields()
    {
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Where(e => e.Name.LocalName == "dest").Remove();

        var result = _parser.Parse(document);

        result.RecipientDocument.Should().BeNull();
        result.RecipientName.Should().BeNull();
        result.RecipientState.Should().BeNull();
    }

    [Test]
    public void Parse_IssuerCnpjDifferentFromAccessKey_ThrowsInvalidDocumentException()
    {
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Single(e => e.Name.LocalName == "emit")
            .Elements().Single(e => e.Name.LocalName == "CNPJ").Value = "44555666000181";

        var act = () => _parser.Parse(document);

        act.Should().Throw<InvalidDocumentException>();
    }
}