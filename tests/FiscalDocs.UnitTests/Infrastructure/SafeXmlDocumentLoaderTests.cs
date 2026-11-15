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
        var document = _loader.Load(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"));

        document.Root!.Name.LocalName.Should().Be("nfeProc");
    }

    [Test]
    public void Load_MalformedXml_ThrowsInvalidDocumentException()
    {
        var act = () => _loader.Load(SampleFiles.ReadAllText("invalido.xml"));

        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Load_XmlWithDoctypeAndExternalEntity_ThrowsInvalidDocumentException()
    {
        var act = () => _loader.Load(SampleFiles.ReadAllText("malicioso-xxe.xml"));

        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Load_DeclaredEncodingDifferentFromStringEncoding_StillReadsAccents()
    {
        var document = _loader.Load("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a>ação</a>");

        document.Root!.Value.Should().Be("ação");
    }
}
