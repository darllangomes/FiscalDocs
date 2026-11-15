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
public class CTeParserTests
{
    private readonly CTeParser _parser = new();

    private static XDocument Load(string file)
    {
        using var stream = SampleFiles.Open(file);
        return SafeXmlLoader.Load(stream);
    }

    [TestCase("cte-cnpj-numerico.xml", true)]
    [TestCase("cte-cnpj-alfanumerico.xml", true)]
    [TestCase("nfe-cnpj-numerico.xml", false)]
    [TestCase("nfe-cnpj-alfanumerico.xml", false)]
    public void CanParse_ReturnsTrueOnlyForCTe(string file, bool expected)
    {
        // Act & Assert
        _parser.CanParse(Load(file)).Should().Be(expected);
    }

    [Test]
    public void Parse_CTeWithNumericCnpj_ExtractsAllFields()
    {
        // Act
        var result = _parser.Parse(Load("cte-cnpj-numerico.xml"));

        // Assert
        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.CTe,
            AccessKey.Parse("35260844555666000181570010000030031345678900"),
            "3003",
            "1",
            new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.FromHours(-3)),
            Cnpj.Parse("44555666000181"),
            "TRANSPORTADORA FICTICIA NUMERICA LTDA",
            "SP",
            "52998224725",
            "DESTINATARIO FICTICIO DE SOUZA",
            "RJ",
            2300.00m));
    }

    [Test]
    public void Parse_CTeWithAlphanumericCnpj_ExtractsAllFields()
    {
        // Act
        var result = _parser.Parse(Load("cte-cnpj-alfanumerico.xml"));

        // Assert
        result.Should().BeEquivalentTo(new ParsedDocument(
            DocumentType.CTe,
            AccessKey.Parse("3526087K9X2M4P000144570010000040041456789018"),
            "4004",
            "1",
            new DateTimeOffset(2026, 8, 22, 16, 45, 0, TimeSpan.FromHours(-3)),
            Cnpj.Parse("7K9X2M4P000144"),
            "TRANSPORTADORA FICTICIA ALFANUMERICA LTDA",
            "SP",
            "99888777000100",
            "DESTINATARIO FICTICIO S/A",
            "MG",
            4120.75m));
    }

    [Test]
    public void Parse_RootWithoutProtocolEnvelope_Works()
    {
        // Arrange
        var withProtocol = Load("cte-cnpj-numerico.xml");

        var cteOnly = new XDocument(new XElement(
            withProtocol.Root!.Elements().First(e => e.Name.LocalName == "CTe")));

        // Act & Assert
        _parser.CanParse(cteOnly).Should().BeTrue();
        _parser.Parse(cteOnly).Number.Should().Be("3003");
    }

    [Test]
    public void Parse_MissingIssuer_ThrowsInvalidDocumentException()
    {
        // Arrange
        var document = Load("cte-cnpj-numerico.xml");
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
        var document = Load("cte-cnpj-numerico.xml");
        document.Descendants().Single(e => e.Name.LocalName == "vTPrest").Value = "abc";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Parse_WithoutRecipient_ReturnsNullRecipientFields()
    {
        // Arrange
        var document = Load("cte-cnpj-numerico.xml");
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
        var document = Load("cte-cnpj-numerico.xml");
        document.Descendants().Single(e => e.Name.LocalName == "emit")
            .Elements().Single(e => e.Name.LocalName == "CNPJ").Value = "11222333000181";

        // Act
        var act = () => _parser.Parse(document);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }
}
