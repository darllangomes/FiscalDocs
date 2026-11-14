using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Infrastructure.Xml;
using FiscalDocs.UnitTests.Support;
using FluentAssertions;
using NSubstitute;

namespace FiscalDocs.UnitTests.Application;

[TestFixture]
public class DocumentParserResolverTests
{
    private readonly DocumentParserResolver _resolver = new([new NFeParser(), new CTeParser()]);

    private static XDocument Load(string file)
    {
        using var stream = SampleFiles.Open(file);
        return SafeXmlLoader.Load(stream);
    }

    [TestCase("nfe-cnpj-numerico.xml")]
    [TestCase("nfe-cnpj-alfanumerico.xml")]
    public void Resolve_NFeDocument_ReturnsNFeParser(string file)
    {
        _resolver.Resolve(Load(file)).Should().BeOfType<NFeParser>();
    }

    [TestCase("cte-cnpj-numerico.xml")]
    [TestCase("cte-cnpj-alfanumerico.xml")]
    public void Resolve_CTeDocument_ReturnsCTeParser(string file)
    {
        _resolver.Resolve(Load(file)).Should().BeOfType<CTeParser>();
    }

    [Test]
    public void Resolve_UnknownRoot_ThrowsInvalidDocumentException()
    {
        var document = new XDocument(new XElement("CompNfse"));

        var act = () => _resolver.Resolve(document);

        act.Should().Throw<InvalidDocumentException>();
    }

    [Test]
    public void Resolve_NewParserRegistered_IsPickedWithoutChangingResolver()
    {
        var nfse = Substitute.For<IDocumentParser>();
        nfse.CanParse(Arg.Any<XDocument>()).Returns(true);
        var resolver = new DocumentParserResolver([new NFeParser(), new CTeParser(), nfse]);

        resolver.Resolve(new XDocument(new XElement("CompNfse"))).Should().BeSameAs(nfse);
    }

    [Test]
    public void Resolve_MoreThanOneParserAccepts_ThrowsInvalidOperationException()
    {
        var first = Substitute.For<IDocumentParser>();
        var second = Substitute.For<IDocumentParser>();
        first.CanParse(Arg.Any<XDocument>()).Returns(true);
        second.CanParse(Arg.Any<XDocument>()).Returns(true);
        var resolver = new DocumentParserResolver([first, second]);

        var act = () => resolver.Resolve(new XDocument(new XElement("x")));

        act.Should().Throw<InvalidOperationException>();
    }
}