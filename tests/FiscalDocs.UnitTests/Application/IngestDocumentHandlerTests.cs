using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Ingestion;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Application.Abstractions;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.ValueObjects;
using FiscalDocs.Infrastructure.Xml;
using FiscalDocs.UnitTests.Support;
using FluentAssertions;
using NSubstitute;

namespace FiscalDocs.UnitTests.Application;

[TestFixture]
public class IngestDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DocumentParserResolver Resolver = new([new NFeParser(), new CTeParser()]);

    private IXmlDocumentLoader _loader = null!;
    private IFiscalDocumentRepository _repository = null!;
    private TimeProvider _clock = null!;
    private IngestDocumentHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _loader = Substitute.For<IXmlDocumentLoader>();
        _loader.Load(Arg.Any<string>()).Returns(call => XDocument.Parse(call.Arg<string>()));

        _repository = Substitute.For<IFiscalDocumentRepository>();
        _repository.TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>()).Returns(true);

        _clock = Substitute.For<TimeProvider>();
        _clock.GetUtcNow().Returns(Now);

        _handler = new IngestDocumentHandler(_loader, Resolver, _repository, _clock);
    }

    // Monta o documento que "já existe" no repositório, a partir do mesmo XML.
    private static FiscalDocument ExistingFrom(string xml, string? rawXml = null)
    {
        var document = XDocument.Parse(xml);
        var p = Resolver.Resolve(document).Parse(document);

        return FiscalDocument.Create(
            p.Type, p.AccessKey, p.Number, p.Series, p.IssueDate,
            p.IssuerCnpj, p.IssuerName, p.IssuerState,
            p.RecipientDocument, p.RecipientName, p.RecipientState,
            p.TotalAmount, rawXml ?? xml, Now);
    }

    private void DuplicateOf(FiscalDocument existing)
    {
        _repository.TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.FindByAccessKeyAsync(existing.AccessKey, Arg.Any<CancellationToken>()).Returns(existing);
    }

    // ---------- Documento novo ----------

    [TestCase("nfe-cnpj-numerico.xml")]
    [TestCase("nfe-cnpj-alfanumerico.xml")]
    [TestCase("cte-cnpj-numerico.xml")]
    [TestCase("cte-cnpj-alfanumerico.xml")]
    public async Task Handle_NewDocument_PersistsAndReturnsCreated(string file)
    {
        // Arrange
        var xml = SampleFiles.ReadAllText(file);
        FiscalDocument? saved = null;
        _repository.TryAddAsync(Arg.Do<FiscalDocument>(d => saved = d), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(IngestOutcome.Created);
        saved.Should().NotBeNull();
        result.DocumentId.Should().Be(saved!.Id);
        saved.RawXml.Should().Be(xml);
        saved.CreatedAt.Should().Be(Now);
    }

    [Test]
    public async Task Handle_NFeSample_MapsParsedFieldsToTheEntity()
    {
        // Arrange
        FiscalDocument? saved = null;
        _repository.TryAddAsync(Arg.Do<FiscalDocument>(d => saved = d), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.HandleAsync(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), CancellationToken.None);

        // Assert
        saved!.Type.Should().Be(FiscalDocs.Domain.Enums.DocumentType.NFe);
        saved.AccessKey.Value.Should().Be("26260811222333000181550010000010011123456782");
        saved.Number.Should().Be("1001");
        saved.IssuerCnpj.Value.Should().Be("11222333000181");
        saved.IssuerState.Should().Be("PE");
        saved.RecipientDocument.Should().Be("12345678909");
        saved.TotalAmount.Should().Be(1500.00m);
    }

    [Test]
    public async Task Handle_NewDocument_DoesNotQueryBeforeInserting()
    {
        // Act
        await _handler.HandleAsync(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), CancellationToken.None);

        // Assert
        await _repository.DidNotReceive()
            .FindByAccessKeyAsync(Arg.Any<AccessKey>(), Arg.Any<CancellationToken>());
    }

    // ---------- Duplicatas ----------

    [Test]
    public async Task Handle_SameKeyAndSameContent_ReturnsAlreadyExistsWithExistingId()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var existing = ExistingFrom(xml);
        DuplicateOf(existing);

        // Act
        var result = await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(IngestOutcome.AlreadyExists);
        result.DocumentId.Should().Be(existing.Id);
    }

    [Test]
    public async Task Handle_SameKeyAndDifferentContent_ReturnsContentMismatch()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var existing = ExistingFrom(xml, rawXml: xml + "\n");
        DuplicateOf(existing);

        // Act
        var result = await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(IngestOutcome.ContentMismatch);
        result.DocumentId.Should().Be(existing.Id);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Handle_SameKeyAsDeletedDocument_ReturnsDeletedDocument(bool sameContent)
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var existing = ExistingFrom(xml, rawXml: sameContent ? xml : xml + "\n");
        existing.MarkAsDeleted(Now.AddMinutes(1));
        DuplicateOf(existing);

        // Act
        var result = await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(IngestOutcome.DeletedDocument);
        result.DocumentId.Should().Be(existing.Id);
    }

    [Test]
    public async Task Handle_DuplicateReportedButNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _repository.TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.FindByAccessKeyAsync(Arg.Any<AccessKey>(), Arg.Any<CancellationToken>())
            .Returns((FiscalDocument?)null);

        // Act
        var act = () => _handler.HandleAsync(
            SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------- Entrada inválida ----------

    [TestCase("")]
    [TestCase("   ")]
    public async Task Handle_BlankXml_ThrowsInvalidDocumentException(string xml)
    {
        // Act
        var act = () => _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDocumentException>();
        _loader.DidNotReceive().Load(Arg.Any<string>());
    }

    [Test]
    public async Task Handle_LoaderRejectsXml_PropagatesAndNeverPersists()
    {
        // Arrange
        _loader.Load(Arg.Any<string>()).Returns(_ => throw new InvalidDocumentException("bad xml"));

        // Act
        var act = () => _handler.HandleAsync("<x/>", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDocumentException>();
        await _repository.DidNotReceive()
            .TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_UnsupportedDocumentType_ThrowsInvalidDocumentException()
    {
        // Act
        var act = () => _handler.HandleAsync("<CompNfse/>", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDocumentException>();
        await _repository.DidNotReceive()
            .TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_XmlViolatingEntityRules_ThrowsInvalidDocumentException()
    {
        // Arrange
        // O parser aceita "PEX", mas a entidade exige UF com duas letras.
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml").Replace("<UF>PE</UF>", "<UF>PEX</UF>");

        // Act
        var act = () => _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDocumentException>();
        await _repository.DidNotReceive()
            .TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>());
    }
}
