using FiscalDocs.Domain.Privacy;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class CpfMaskerTests
{
    [TestCase("11122233344", "***.222.333-**")]    // só dígitos
    [TestCase("111.222.333-44", "***.222.333-**")] // já formatado
    [TestCase(" 111.222.333-44 ", "***.222.333-**")] // espaços nas pontas
    [TestCase("123.456.789-09", "***.456.789-**")]
    public void Mask_ValidCpf_KeepsOnlyMiddleSixDigits(string input, string expected)
    {
        // Act & Assert
        CpfMasker.Mask(input).Should().Be(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("123")]              // curto demais
    [TestCase("1112223334")]
    [TestCase("111222333445")]
    [TestCase("111.222.333-4X")]   // letra: sobram só 10 dígitos
    [TestCase("abc")]
    [TestCase("11222333000181")]   // CNPJ não é CPF
    public void Mask_NotACpf_ReturnsFullMaskAndNeverEchoesInput(string? input)
    {
        // Act
        var result = CpfMasker.Mask(input);

        // Assert
        result.Should().Be("***");
    }
}
