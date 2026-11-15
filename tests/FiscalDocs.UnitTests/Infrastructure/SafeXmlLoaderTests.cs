using FiscalDocs.Application.Exceptions;
using FiscalDocs.Infrastructure.Xml;
using FiscalDocs.UnitTests.Support;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Infrastructure;

[TestFixture]
public class SafeXmlLoaderTests
{
    [TestCase("nfe-cnpj-numerico.xml", "nfeProc")]
    [TestCase("nfe-cnpj-alfanumerico.xml", "nfeProc")]
    [TestCase("cte-cnpj-numerico.xml", "cteProc")]
    [TestCase("cte-cnpj-alfanumerico.xml", "cteProc")]
    public void Load_ValidSample_ReturnsDocumentWithExpectedRoot(string file, string rootName)
    {
        // Arrange
        using var stream = SampleFiles.Open(file);

        // Act
        var document = SafeXmlLoader.Load(stream);

        // Assert
        document.Root!.Name.LocalName.Should().Be(rootName);
    }

    [Test]
    public void Load_MalformedXml_ThrowsInvalidDocumentException()
    {
        // Arrange
        using var stream = SampleFiles.Open("invalido.xml");

        // Act
        var act = () => SafeXmlLoader.Load(stream);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Load_DocumentWithDtd_ThrowsAndDoesNotLeakFileContents()
    {
        // Arrange
        using var stream = SampleFiles.Open("malicioso-xxe.xml");

        // Act
        var act = () => SafeXmlLoader.Load(stream);

        // Assert
        act.Should().Throw<InvalidDocumentException>()
            .Which.Message.Should().NotContain("fonts"); // [fonts] is a section of win.ini
    }

    [Test]
    public void Load_DocumentLargerThanLimit_ThrowsInvalidDocumentException()
    {
        // Arrange
        using var stream = SampleFiles.Open("nfe-cnpj-numerico.xml");

        // Act
        var act = () => SafeXmlLoader.Load(stream, maxCharacters: 100);

        // Assert
        act.Should().Throw<InvalidDocumentException>();
    }
}
