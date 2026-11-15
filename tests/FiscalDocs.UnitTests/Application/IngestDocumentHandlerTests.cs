using System.Xml.Linq;
using FiscalDocs.Application.Exceptions;
using FiscalDocs.Application.Ingestion;
using FiscalDocs.Application.Messaging;
using FiscalDocs.Application.Parsing;
using FiscalDocs.Application.Abstractions;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.ValueObjects;
using FiscalDocs.Infrastructure.Xml;
using FiscalDocs.UnitTests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FiscalDocs.UnitTests.Application;

[TestFixture]
public class IngestDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DocumentParserResolver Resolver = new([new NFeParser(), new CTeParser()]);

    private IXmlDocumentLoader _loader = null!;
    private IFiscalDocumentRepository _repository = null!;
    private IDocumentEventPublisher _publisher = null!;
    private CapturingLogger<IngestDocumentHandler> _logger = null!;
    private TimeProvider _clock = null!;
    private IngestDocumentHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _loader = Substitute.For<IXmlDocumentLoader>();
        _loader.Load(Arg.Any<string>()).Returns(call => XDocument.Parse(call.Arg<string>()));

        _repository = Substitute.For<IFiscalDocumentRepository>();
        _repository.TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>()).Returns(true);

        _publisher = Substitute.For<IDocumentEventPublisher>();
        _logger = new CapturingLogger<IngestDocumentHandler>();

        _clock = Substitute.For<TimeProvider>();
        _clock.GetUtcNow().Returns(Now);

        _handler = new IngestDocumentHandler(_loader, Resolver, _repository, _publisher, _clock, _logger);
    }

    // Builds the document that "already exists" in the repository, from the same XML.
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

    // ---------- New document ----------

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

    // ---------- Event publishing ----------

    [Test]
    public async Task Handle_NewDocument_PublishesEventDescribingTheDocument()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        DocumentIngestedEvent? published = null;
        await _publisher.PublishAsync(Arg.Do<DocumentIngestedEvent>(e => published = e), Arg.Any<CancellationToken>());

        // Act
        var result = await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        published.Should().NotBeNull();
        published!.MessageId.Should().NotBeEmpty();
        published.DocumentId.Should().Be(result.DocumentId);
        published.Type.Should().Be(FiscalDocs.Domain.Enums.DocumentType.NFe);
        published.IssuerCnpj.Should().Be("11222333000181");
        published.IssuerState.Should().Be("PE");
        published.IssueDate.Should().Be(new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)));
        published.TotalAmount.Should().Be(1500.00m);
        published.OccurredAt.Should().Be(Now);
    }

    [Test]
    public async Task Handle_NewDocument_PublishesExactlyOnce()
    {
        // Act
        await _handler.HandleAsync(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), CancellationToken.None);

        // Assert
        await _publisher.Received(1).PublishAsync(Arg.Any<DocumentIngestedEvent>(), Arg.Any<CancellationToken>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Handle_Duplicate_DoesNotPublish(bool deleted)
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        var existing = ExistingFrom(xml);
        if (deleted)
        {
            existing.MarkAsDeleted(Now.AddMinutes(1));
        }
        DuplicateOf(existing);

        // Act
        await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<DocumentIngestedEvent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_ContentMismatch_DoesNotPublish()
    {
        // Arrange
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml");
        DuplicateOf(ExistingFrom(xml, rawXml: xml + "\n"));

        // Act
        await _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<DocumentIngestedEvent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_PublisherFails_StillReturnsCreatedAndLogsTheError()
    {
        // Arrange
        var failure = new InvalidOperationException("broker down");
        _publisher.PublishAsync(Arg.Any<DocumentIngestedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));

        // Act
        var result = await _handler.HandleAsync(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(IngestOutcome.Created);
        var entry = _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        entry.Exception.Should().BeSameAs(failure);
        entry.Message.Should().Contain("Failed to publish").And.Contain(result.DocumentId.ToString());
    }

    [Test]
    public async Task Handle_PublisherFails_LogDoesNotContainDocumentData()
    {
        // Arrange
        _publisher.PublishAsync(Arg.Any<DocumentIngestedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("broker down")));

        // Act
        await _handler.HandleAsync(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), CancellationToken.None);

        // Assert
        _logger.Entries.Should().NotContain(e =>
            e.Message.Contains("12345678909") || e.Message.Contains("11222333000181") || e.Message.Contains("<"));
    }

    [Test]
    public async Task Handle_CancellationRequested_Propagates()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _publisher.PublishAsync(Arg.Any<DocumentIngestedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled(new CancellationToken(true)));

        // Act
        var act = () => _handler.HandleAsync(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- Duplicates ----------

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

    // ---------- Invalid input ----------

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
        // The parser accepts "PEX", but the entity requires a two-letter state.
        var xml = SampleFiles.ReadAllText("nfe-cnpj-numerico.xml").Replace("<UF>PE</UF>", "<UF>PEX</UF>");

        // Act
        var act = () => _handler.HandleAsync(xml, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidDocumentException>();
        await _repository.DidNotReceive()
            .TryAddAsync(Arg.Any<FiscalDocument>(), Arg.Any<CancellationToken>());
    }
}
