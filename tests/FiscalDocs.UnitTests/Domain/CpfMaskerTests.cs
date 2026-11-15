using FiscalDocs.Domain.Privacy;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class CpfMaskerTests
{
    [TestCase("11122233344", "***.222.333-**")]    // digits only
    [TestCase("111.222.333-44", "***.222.333-**")] // already formatted
    [TestCase(" 111.222.333-44 ", "***.222.333-**")] // surrounding whitespace
    [TestCase("123.456.789-09", "***.456.789-**")]
    public void Mask_ValidCpf_KeepsOnlyMiddleSixDigits(string input, string expected)
    {
        // Act & Assert
        CpfMasker.Mask(input).Should().Be(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("123")]              // too short
    [TestCase("1112223334")]       // 10 digits
    [TestCase("111222333445")]     // 12 digits
    [TestCase("111.222.333-4X")]   // letter: only 10 digits remain
    [TestCase("abc")]
    [TestCase("11222333000181")]   // CNPJ is not a CPF
    public void Mask_NotACpf_ReturnsFullMaskAndNeverEchoesInput(string? input)
    {
        // Act
        var result = CpfMasker.Mask(input);

        // Assert
        result.Should().Be("***");
    }
}
