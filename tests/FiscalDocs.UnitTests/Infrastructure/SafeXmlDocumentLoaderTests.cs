using FiscalDocs.Application.Exceptions;
using FiscalDocs.Infrastructure.Xml;
using FiscalDocs.UnitTests.Support;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Infrastructure;

[TestFixture]
public class SafeXmlDocumentLoaderTests
{
    private readonly SafeXmlDocumentLoader _loader = new();

    [Test]
    public void Load_ValidSample_ReturnsDocument()
    {
        // Act
        var document = _loader.Load(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"));

        // Assert
        document.Root!.Name.LocalName.Should().Be("nfeProc");
    }

    [Test]
    public void Load_MalformedXml_ThrowsInvalidDocumentException()
    {
        // Act
        var act = () => _loader.Load(SampleFiles.ReadAllText("invalido.xml"));

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Load_XmlWithDoctypeAndExternalEntity_ThrowsInvalidDocumentException()
    {
        // Act
        var act = () => _loader.Load(SampleFiles.ReadAllText("malicioso-xxe.xml"));

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Load_DeclaredEncodingDifferentFromStringEncoding_StillReadsAccents()
    {
        // Act
        var document = _loader.Load("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a>ação</a>");

        // Assert
        document.Root!.Value.Should().Be("ação");
    }
}
