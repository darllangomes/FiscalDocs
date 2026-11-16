using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class AccessKeyTests
{
    // Chaves de samples/ (dígito verificador: módulo 11, valor do caractere = ASCII - 48)
    private const string NFeNumericCnpj = "26260811222333000181550010000010011123456782";
    private const string NFeAlphanumericCnpj = "26260812ABC34501DE35550010000020021234567895";
    private const string CTeNumericCnpj = "35260844555666000181570010000030031345678900";
    private const string CTeAlphanumericCnpj = "3526087K9X2M4P000144570010000040041456789018";

    [TestCase(NFeNumericCnpj)]
    [TestCase(NFeAlphanumericCnpj)]
    [TestCase(CTeNumericCnpj)]
    [TestCase(CTeAlphanumericCnpj)]
    public void TryParse_ValidKey_ReturnsTrueAndKeepsValue(string input)
    {
        // Act
        var ok = AccessKey.TryParse(input, out var key);

        // Assert
        ok.Should().BeTrue();
        key!.Value.Should().Be(input);
    }

    [TestCase("2626 0811 2223 3300 0181 5500 1000 0010 0111 2345 6782", NFeNumericCnpj)] // como impressa no DANFE
    [TestCase("26260812abc34501de35550010000020021234567895", NFeAlphanumericCnpj)]       // minúsculas
    public void TryParse_SpacesOrLowercase_NormalizesValue(string input, string expected)
    {
        // Act
        var ok = AccessKey.TryParse(input, out var key);

        // Assert
        ok.Should().BeTrue();
        key!.Value.Should().Be(expected);
    }

    [TestCase("26260811222333000181550010000010011123456783")]  // DV errado (numérico)
    [TestCase("26260812ABC34501DE35550010000020021234567896")]  // DV errado (alfanumérico)
    [TestCase("2626081122233300018155001000001001112345678")]
    [TestCase("262608112223330001815500100000100111234567820")]
    [TestCase("A6260811222333000181550010000010011123456782")]  // letra na posição do código da UF
    [TestCase("26260811222333000181550010000010011A23456782")]  // letra na parte numérica final
    [TestCase("2626081122233300018155001000001001112345678A")]  // letra na posição do DV
    [TestCase("26260811222333000181550010000010011123456-82")]  // separador não aceito
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TryParse_InvalidKey_ReturnsFalse(string? input)
    {
        // Act & Assert
        AccessKey.TryParse(input, out _).Should().BeFalse();
    }

    [TestCase("26260811222333000181550010000010011123456783")]
    [TestCase("26260811222333000182550010000010011123456784")]
    [TestCase("")]
    [TestCase(null)]
    public void Parse_InvalidKey_ThrowsFormatException(string? input)
    {
        // Act
        var act = () => AccessKey.Parse(input);

        // Assert
        act.Should().Throw<FormatException>();
    }

    [TestCase(NFeNumericCnpj, "55", "11222333000181")]
    [TestCase(NFeAlphanumericCnpj, "55", "12ABC34501DE35")]
    [TestCase(CTeNumericCnpj, "57", "44555666000181")]
    [TestCase(CTeAlphanumericCnpj, "57", "7K9X2M4P000144")]
    public void Parse_ValidKey_ExtractsModelAndIssuer(string input, string model, string issuer)
    {
        // Act
        var key = AccessKey.Parse(input);

        // Assert
        key.Model.Should().Be(model);
        key.IsIssuedBy(TaxId.Parse(issuer)).Should().BeTrue();
    }

    // ---------- Emitente pessoa física: o CPF ocupa as 14 posições do CNPJ, com zeros à esquerda ----------

    private const string NFeCpfIssuer = "51260800052998224725559200000000421876543214";
    private const string NFeCpfFieldWithInvalidCpf = "51260800052998224726559200000000451876543218";
    private const string NFeCnpjStartingWithZeros = "35260800000000000191550010000000071112233440"; // CNPJ 00.000.000/0001-91

    [Test]
    public void TryParse_KeyOfAnIndividualIssuer_ReturnsTrue()
    {
        // Act
        var ok = AccessKey.TryParse(NFeCpfIssuer, out var key);

        // Assert
        ok.Should().BeTrue();
        key!.Value.Should().Be(NFeCpfIssuer);
        key.Model.Should().Be("55");
    }

    [Test]
    public void TryParse_IssuerFieldWithZerosAndAnInvalidCpf_ReturnsFalse()
    {
        // Act & Assert
        AccessKey.TryParse(NFeCpfFieldWithInvalidCpf, out _).Should().BeFalse();
    }

    [Test]
    public void IsIssuedBy_IndividualIssuer_MatchesOnlyThatCpf()
    {
        // Arrange
        var key = AccessKey.Parse(NFeCpfIssuer);

        // Act & Assert
        key.IsIssuedBy(TaxId.Parse("52998224725")).Should().BeTrue();
        key.IsIssuedBy(TaxId.Parse("12345678909")).Should().BeFalse();
        key.IsIssuedBy(TaxId.Parse("11222333000181")).Should().BeFalse();
    }

    [Test]
    public void IsIssuedBy_CnpjIssuer_MatchesOnlyThatCnpj()
    {
        // Arrange
        var key = AccessKey.Parse(NFeNumericCnpj);

        // Act & Assert
        key.IsIssuedBy(TaxId.Parse("11222333000181")).Should().BeTrue();
        key.IsIssuedBy(TaxId.Parse("44555666000181")).Should().BeFalse();
        key.IsIssuedBy(TaxId.Parse("52998224725")).Should().BeFalse();
    }

    [Test]
    public void IsIssuedBy_CnpjStartingWithZeros_TheKeyAloneCannotTellItFromACpf()
    {
        // Arrange: 00000000000191 é um CNPJ válido e 00000000191 é um CPF válido, e o campo da chave
        // é idêntico nos dois casos. Por isso a chave não infere o tipo: quem o define é o elemento
        // do XML (<CNPJ> ou <CPF>), e a chave só confirma que o número é o mesmo.
        var key = AccessKey.Parse(NFeCnpjStartingWithZeros);

        // Act & Assert
        key.IsIssuedBy(TaxId.Parse("00000000000191")).Should().BeTrue();
        key.IsIssuedBy(TaxId.Parse("00000000191")).Should().BeTrue();
        key.IsIssuedBy(TaxId.Parse("12345678909")).Should().BeFalse();
    }

    [Test]
    public void Equality_SameKeyWithAndWithoutSpaces_AreEqual()
    {
        // Arrange
        var spaced = AccessKey.Parse("2626 0811 2223 3300 0181 5500 1000 0010 0111 2345 6782");
        var plain = AccessKey.Parse(NFeNumericCnpj);

        // Act & Assert
        spaced.Should().Be(plain);
    }
}
