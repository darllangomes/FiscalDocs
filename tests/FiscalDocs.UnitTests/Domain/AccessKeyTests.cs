using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class AccessKeyTests
{
    // Keys from samples/ (check digit: modulo 11, character value = ASCII - 48)
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

    [TestCase("2626 0811 2223 3300 0181 5500 1000 0010 0111 2345 6782", NFeNumericCnpj)] // as printed on the DANFE
    [TestCase("26260812abc34501de35550010000020021234567895", NFeAlphanumericCnpj)]       // lowercase
    public void TryParse_SpacesOrLowercase_NormalizesValue(string input, string expected)
    {
        // Act
        var ok = AccessKey.TryParse(input, out var key);

        // Assert
        ok.Should().BeTrue();
        key!.Value.Should().Be(expected);
    }

    [TestCase("26260811222333000181550010000010011123456783")]  // wrong check digit (numeric)
    [TestCase("26260812ABC34501DE35550010000020021234567896")]  // wrong check digit (alphanumeric)
    [TestCase("2626081122233300018155001000001001112345678")]   // 43 characters
    [TestCase("262608112223330001815500100000100111234567820")] // 45 characters
    [TestCase("A6260811222333000181550010000010011123456782")]  // letter in the state code position
    [TestCase("26260811222333000181550010000010011A23456782")]  // letter in the numeric tail
    [TestCase("2626081122233300018155001000001001112345678A")]  // letter in the check digit position
    [TestCase("26260811222333000181550010000010011123456-82")]  // unsupported separator
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
    public void Parse_ValidKey_ExtractsModelAndIssuerCnpj(string input, string model, string issuerCnpj)
    {
        // Act
        var key = AccessKey.Parse(input);

        // Assert
        key.Model.Should().Be(model);
        key.IssuerCnpj.Value.Should().Be(issuerCnpj);
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
