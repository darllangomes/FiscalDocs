using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

// O documento do emitente é um CNPJ (14 posições) ou um CPF (11). O tamanho distingue os dois.
[TestFixture]
public class TaxIdTests
{
    [TestCase("11222333000181", "11222333000181")]
    [TestCase("12ABC34501DE35", "12ABC34501DE35")]
    [TestCase("12.abc.345/01de-35", "12ABC34501DE35")]
    public void TryParse_Cnpj_ReturnsACnpj(string input, string expected)
    {
        // Act
        var ok = TaxId.TryParse(input, out var taxId);

        // Assert
        ok.Should().BeTrue();
        taxId!.Value.Should().Be(expected);
        taxId.IsCnpj.Should().BeTrue();
        taxId.IsCpf.Should().BeFalse();
    }

    [TestCase("52998224725")]
    [TestCase("529.982.247-25")]
    public void TryParse_Cpf_ReturnsACpfWithOnlyTheDigits(string input)
    {
        // Act
        var ok = TaxId.TryParse(input, out var taxId);

        // Assert
        ok.Should().BeTrue();
        taxId!.Value.Should().Be("52998224725");
        taxId.IsCpf.Should().BeTrue();
        taxId.IsCnpj.Should().BeFalse();
    }

    [TestCase("123")]
    [TestCase("52998224726")]       // CPF com DV errado
    [TestCase("11111111111")]       // CPF repetido
    [TestCase("11222333000182")]    // CNPJ com DV errado
    [TestCase("0052998224725")]     // 13 posições: nem CPF nem CNPJ
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TryParse_Invalid_ReturnsFalse(string? input)
    {
        // Act & Assert
        TaxId.TryParse(input, out _).Should().BeFalse();
    }

    [Test]
    public void Parse_Invalid_ThrowsFormatException()
    {
        // Act
        var act = () => TaxId.Parse("52998224726");

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Test]
    public void From_Cnpj_KeepsTheCnpjValue()
    {
        // Act
        var taxId = TaxId.From(Cnpj.Parse("12ABC34501DE35"));

        // Assert
        taxId.Should().Be(TaxId.Parse("12.ABC.345/01DE-35"));
        taxId.IsCnpj.Should().BeTrue();
    }

    [Test]
    public void From_Cpf_KeepsTheCpfValue()
    {
        // Act
        var taxId = TaxId.From(Cpf.Parse("529.982.247-25"));

        // Assert
        taxId.Should().Be(TaxId.Parse("52998224725"));
        taxId.IsCpf.Should().BeTrue();
    }

    [Test]
    public void Masked_Cnpj_IsTheCnpjItself()
    {
        // Act & Assert
        TaxId.Parse("12ABC34501DE35").Masked.Should().Be("12ABC34501DE35");
    }

    [Test]
    public void Masked_Cpf_HidesTheFirstThreeAndTheLastTwoDigits()
    {
        // Act & Assert
        TaxId.Parse("52998224725").Masked.Should().Be("***.982.247-**");
    }
}
