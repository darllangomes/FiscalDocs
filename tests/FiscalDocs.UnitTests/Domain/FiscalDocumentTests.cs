using FiscalDocs.Domain.Entities;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class FiscalDocumentTests
{
    private const string NFeKey = "26260811222333000181550010000010011123456782";
    private const string CTeKey = "35260844555666000181570010000030031345678900";
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private static FiscalDocument Build(
        DocumentType type = DocumentType.NFe,
        string key = NFeKey,
        string? issuerCnpj = null,
        string number = "1001",
        string series = "1",
        string issuerName = "EMPRESA FICTICIA NUMERICA LTDA",
        string issuerState = "PE",
        string? recipientDocument = "12345678909",
        string? recipientName = "CLIENTE FICTICIO DA SILVA",
        string? recipientState = "SP",
        decimal totalAmount = 1500.00m,
        string rawXml = "<a/>")
    {
        var accessKey = AccessKey.Parse(key);
        var cnpj = issuerCnpj is null ? accessKey.IssuerCnpj : Cnpj.Parse(issuerCnpj);

        return FiscalDocument.Create(
            type, accessKey, number, series,
            new DateTimeOffset(2026, 8, 14, 10, 30, 0, TimeSpan.FromHours(-3)),
            cnpj, issuerName, issuerState,
            recipientDocument, recipientName, recipientState,
            totalAmount, rawXml, Now);
    }

    // ---------- Create: estado inicial ----------

    [Test]
    public void Create_ValidData_SetsInitialState()
    {
        // Act
        var doc = Build();

        // Assert
        doc.Id.Should().NotBeEmpty();
        doc.Type.Should().Be(DocumentType.NFe);
        doc.AccessKey.Value.Should().Be(NFeKey);
        doc.Number.Should().Be("1001");
        doc.Series.Should().Be("1");
        doc.IssuerCnpj.Value.Should().Be("11222333000181");
        doc.IssuerName.Should().Be("EMPRESA FICTICIA NUMERICA LTDA");
        doc.IssuerState.Should().Be("PE");
        doc.RecipientDocument.Should().Be("12345678909");
        doc.TotalAmount.Should().Be(1500.00m);
        doc.Status.Should().Be(DocumentStatus.Received);
        doc.Notes.Should().BeNull();
        doc.IsDeleted.Should().BeFalse();
        doc.CreatedAt.Should().Be(Now);
        doc.UpdatedAt.Should().Be(Now);
    }

    [Test]
    public void Create_TwoDocuments_HaveDifferentIds()
    {
        // Act & Assert
        Build().Id.Should().NotBe(Build().Id);
    }

    [Test]
    public void Create_WithoutRecipient_KeepsRecipientFieldsNull()
    {
        // Act
        var doc = Build(recipientDocument: null, recipientName: null, recipientState: null);

        // Assert
        doc.RecipientDocument.Should().BeNull();
        doc.RecipientName.Should().BeNull();
        doc.RecipientState.Should().BeNull();
    }

    [Test]
    public void Create_LowercaseIssuerState_NormalizesToUppercase()
    {
        // Act & Assert
        Build(issuerState: "pe").IssuerState.Should().Be("PE");
    }

    // ---------- Create: hash ----------

    [Test]
    public void Create_XmlHash_IsLowercaseSha256OfRawXml()
    {
        // Act & Assert
        // SHA-256 of the UTF-8 bytes of "<a/>"
        Build(rawXml: "<a/>").XmlHash.Should()
            .Be("29114363f749a0226b6988dda3ca2492a954117ab6b5f382706c20300dabc079");
    }

    [Test]
    public void Create_DifferentXml_ProducesDifferentHash()
    {
        // Act & Assert
        Build(rawXml: "<a/>").XmlHash.Should().NotBe(Build(rawXml: "<b/>").XmlHash);
    }

    // ---------- Create: invariantes ----------

    [TestCase(DocumentType.NFe, NFeKey)]
    [TestCase(DocumentType.CTe, CTeKey)]
    public void Create_TypeMatchesAccessKeyModel_Succeeds(DocumentType type, string key)
    {
        // Act
        var act = () => Build(type: type, key: key);

        // Assert
        act.Should().NotThrow();
    }

    [TestCase(DocumentType.NFe, CTeKey)]
    [TestCase(DocumentType.CTe, NFeKey)]
    public void Create_TypeDoesNotMatchAccessKeyModel_ThrowsArgumentException(DocumentType type, string key)
    {
        // Act
        var act = () => Build(type: type, key: key);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Create_IssuerCnpjDifferentFromAccessKey_ThrowsArgumentException()
    {
        // Act
        var act = () => Build(issuerCnpj: "44555666000181");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static IEnumerable<TestCaseData> BlankRequiredFields()
    {
        foreach (var blank in new[] { "", "   " })
        {
            yield return new TestCaseData((Func<FiscalDocument>)(() => Build(number: blank)))
                .SetName($"Number_\"{blank}\"");
            yield return new TestCaseData((Func<FiscalDocument>)(() => Build(series: blank)))
                .SetName($"Series_\"{blank}\"");
            yield return new TestCaseData((Func<FiscalDocument>)(() => Build(issuerName: blank)))
                .SetName($"IssuerName_\"{blank}\"");
            yield return new TestCaseData((Func<FiscalDocument>)(() => Build(rawXml: blank)))
                .SetName($"RawXml_\"{blank}\"");
        }
    }

    [TestCaseSource(nameof(BlankRequiredFields))]
    public void Create_BlankRequiredField_ThrowsArgumentException(Func<FiscalDocument> create)
    {
        // Act
        var act = () => create();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [TestCase("")]
    [TestCase("P")]
    [TestCase("PER")]
    public void Create_InvalidIssuerState_ThrowsArgumentException(string state)
    {
        // Act
        var act = () => Build(issuerState: state);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Create_NegativeTotal_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => Build(totalAmount: -0.01m);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Create_ZeroTotal_Succeeds()
    {
        // Act
        var act = () => Build(totalAmount: 0m);

        // Assert
        act.Should().NotThrow();
    }

    // ---------- UpdateManagement ----------

    [Test]
    public void UpdateManagement_ValidData_ChangesOnlyManagementFields()
    {
        // Arrange
        var doc = Build();

        // Act
        doc.UpdateManagement(DocumentStatus.Reviewed, "checked by finance", Later);

        // Assert
        doc.Status.Should().Be(DocumentStatus.Reviewed);
        doc.Notes.Should().Be("checked by finance");
        doc.UpdatedAt.Should().Be(Later);
        doc.CreatedAt.Should().Be(Now);
        doc.AccessKey.Value.Should().Be(NFeKey);
        doc.TotalAmount.Should().Be(1500.00m);
        doc.XmlHash.Should().Be(Build().XmlHash);
    }

    [Test]
    public void UpdateManagement_NotesWithSurroundingSpaces_AreTrimmed()
    {
        // Arrange
        var doc = Build();

        // Act
        doc.UpdateManagement(DocumentStatus.Reviewed, "  ok  ", Later);

        // Assert
        doc.Notes.Should().Be("ok");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void UpdateManagement_BlankNotes_BecomeNull(string? notes)
    {
        // Arrange
        var doc = Build();
        doc.UpdateManagement(DocumentStatus.Reviewed, "something", Later);

        // Act
        doc.UpdateManagement(DocumentStatus.Reviewed, notes, Later);

        // Assert
        doc.Notes.Should().BeNull();
    }

    [Test]
    public void UpdateManagement_NotesWithMaxLength_Succeeds()
    {
        // Act
        var act = () => Build().UpdateManagement(DocumentStatus.Reviewed, new string('x', 500), Later);

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public void UpdateManagement_NotesTooLong_ThrowsArgumentException()
    {
        // Act
        var act = () => Build().UpdateManagement(DocumentStatus.Reviewed, new string('x', 501), Later);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void UpdateManagement_UndefinedStatus_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => Build().UpdateManagement((DocumentStatus)99, null, Later);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void UpdateManagement_DeletedDocument_ThrowsInvalidOperationException()
    {
        // Arrange
        var doc = Build();
        doc.MarkAsDeleted(Later);

        // Act
        var act = () => doc.UpdateManagement(DocumentStatus.Reviewed, null, Later.AddMinutes(1));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // ---------- MarkAsDeleted ----------

    [Test]
    public void MarkAsDeleted_SetsFlagAndUpdatedAt()
    {
        // Arrange
        var doc = Build();

        // Act
        doc.MarkAsDeleted(Later);

        // Assert
        doc.IsDeleted.Should().BeTrue();
        doc.UpdatedAt.Should().Be(Later);
    }

    [Test]
    public void MarkAsDeleted_CalledTwice_IsIdempotent()
    {
        // Arrange
        var doc = Build();
        doc.MarkAsDeleted(Later);

        // Act
        var act = () => doc.MarkAsDeleted(Later.AddHours(1));

        // Assert
        act.Should().NotThrow();
        doc.IsDeleted.Should().BeTrue();
        doc.UpdatedAt.Should().Be(Later);
    }

    // ---------- Imutabilidade (D9) ----------

    [Test]
    public void Entity_HasNoPublicSetters()
    {
        // Act
        var publicSetters = typeof(FiscalDocument)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        // Assert
        publicSetters.Should().BeEmpty();
    }
}
