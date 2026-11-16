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
    [TestCase("nfe-emitente-cpf.xml", true)]
    [TestCase("cte-cnpj-numerico.xml", false)]
    [TestCase("cte-cnpj-alfanumerico.xml", false)]
    public void CanParse_ReturnsTrueOnlyForNFe(string file, bool expected)
    {
        // Act & Assert
        _parser.CanParse(Load(file)).Should().Be(expected);
    }

    [Test]
    public void Parse_NFeWithNumericCnpj_ExtractsAllFields()
    {
        // Act
        var result = _parser.Parse(Load("nfe-cnpj-numerico.xml"));

        // Assert
        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.NFe,
            AccessKey.Parse("26260811222333000181550010000010011123456782"),
            "1001",
            "1",
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
            TaxId.Parse("11222333000181"),
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
        // Act
        var result = _parser.Parse(Load("nfe-cnpj-alfanumerico.xml"));

        // Assert
        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.NFe,
            AccessKey.Parse("26260812ABC34501DE35550010000020021234567895"),
            "2002",
            "1",
            new DateTimeOffset(2026, 8, 20, 14, 15, 0, TimeSpan.FromHours(-3)),
            TaxId.Parse("12ABC34501DE35"),
            "EMPRESA FICTICIA ALFANUMERICA LTDA",
            "PE",
            "99888777000100",
            "COMERCIAL DESTINO FICTICIA S/A",
            "BA",
            8750.50m));
    }

    // XMLs no formato real: minificados, com itens, impostos, transporte, pagamento,
    // assinatura digital e protocolo. Os samples antigos não tinham nada disso.
    [Test]
    public void Parse_RealisticNFeWithSignatureAndItems_ExtractsAllFields()
    {
        // Act
        var result = _parser.Parse(Load("nfe-realista-3itens.xml"));

        // Assert
        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.NFe,
            AccessKey.Parse("26260811222333000181550010000050051784057835"),
            "5005",
            "1",
            new DateTimeOffset(2026, 8, 16, 9, 12, 44, TimeSpan.FromHours(-3)),
            TaxId.Parse("11222333000181"),
            "EMPRESA FICTICIA NUMERICA LTDA",
            "PE",
            "12ABC34501DE35",
            "EMPRESA FICTICIA ALFANUMERICA LTDA",
            "SP",
            1029.39m));
    }

    [Test]
    public void Parse_RealisticNFeWithManyItems_UsesTheDocumentTotalNotAnItem()
    {
        // Act
        var result = _parser.Parse(Load("nfe-realista-250itens.xml"));

        // Assert
        result.Number.Should().Be("6006");
        result.TotalAmount.Should().Be(64445.17m);
        result.RecipientDocument.Should().Be("12345678909");
    }

    [Test]
    public void Parse_NFeWithTheMaximumNumberOfItems_ExtractsTheDocumentTotal()
    {
        // Act
        var result = _parser.Parse(Load("nfe-realista-990itens.xml"));

        // Assert
        result.Number.Should().Be("7007");
        result.TotalAmount.Should().Be(258283.36m);
    }

    [TestCase("nfe-realista-3itens.xml")]
    [TestCase("nfe-realista-250itens.xml")]
    [TestCase("nfe-realista-990itens.xml")]
    public void CanParse_RealisticNFe_ReturnsTrue(string file)
    {
        // Act & Assert
        _parser.CanParse(Load(file)).Should().BeTrue();
    }

    // ---------- Emitente pessoa física (produtor rural): <emit> traz <CPF> no lugar de <CNPJ> ----------

    private static XElement Emit(XDocument document) =>
        document.Descendants().Single(e => e.Name.LocalName == "emit");

    [Test]
    public void Parse_NFeFromAnIndividualIssuer_ExtractsTheCpfAsIssuerDocument()
    {
        // Act
        var result = _parser.Parse(Load("nfe-emitente-cpf.xml"));

        // Assert
        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.NFe,
            AccessKey.Parse("51260800052998224725559200000000421876543214"),
            "42",
            "920",
            new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.FromHours(-4)),
            TaxId.Parse("52998224725"),
            "PRODUTOR RURAL FICTICIO",
            "MT",
            "11222333000181",
            "COOPERATIVA FICTICIA DE GRAOS",
            "SP",
            3200.00m));
    }

    [Test]
    public void Parse_IssuerCpfDifferentFromAccessKey_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("nfe-emitente-cpf.xml");
        Emit(document).Elements().Single(e => e.Name.LocalName == "CPF").Value = "12345678909";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_IssuerWithInvalidCpf_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("nfe-emitente-cpf.xml");
        Emit(document).Elements().Single(e => e.Name.LocalName == "CPF").Value = "52998224726";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_IssuerWithoutCnpjOrCpf_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("nfe-emitente-cpf.xml");
        Emit(document).Elements().Single(e => e.Name.LocalName == "CPF").Remove();

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_CnpjElementHoldingACpf_ThrowsInvalidDocumentException()
    {
        // Arrange: um valor de 11 dígitos dentro de <CNPJ> não vira CPF por adivinhação.
        var document = Load("nfe-emitente-cpf.xml");
        Emit(document).Elements().Single(e => e.Name.LocalName == "CPF").Name =
            document.Root!.Name.Namespace + "CNPJ";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_RootWithoutProtocolEnvelope_Works()
    {
        // Arrange
        var withProtocol = Load("nfe-cnpj-numerico.xml");

        var nfeOnly = new XDocument(new XElement(
            withProtocol.Root!.Elements().First(e => e.Name.LocalName == "NFe")));

        // Act & Assert
        _parser.CanParse(nfeOnly).Should().BeTrue();
        _parser.Parse(nfeOnly).Number.Should().Be("1001");
    }

    [Test]
    public void Parse_MissingIssuer_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Where(e => e.Name.LocalName == "emit").Remove();

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_InvalidTotalAmount_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Single(e => e.Name.LocalName == "vNF").Value = "abc";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_WithoutRecipient_ReturnsNullRecipientFields()
    {
        // Arrange
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Where(e => e.Name.LocalName == "dest").Remove();

        // Act
        var result = _parser.Parse(document);

        // Assert
        result.RecipientDocument.Should().BeNull();
        result.RecipientName.Should().BeNull();
        result.RecipientState.Should().BeNull();
    }

    [Test]
    public void Parse_IssuerCnpjDifferentFromAccessKey_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("nfe-cnpj-numerico.xml");
        document.Descendants().Single(e => e.Name.LocalName == "emit")
            .Elements().Single(e => e.Name.LocalName == "CNPJ").Value = "44555666000181";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }
}
