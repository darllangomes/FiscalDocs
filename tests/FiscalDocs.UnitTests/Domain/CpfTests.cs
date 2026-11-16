using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class CpfTests
{
    [TestCase("52998224725")]
    [TestCase("12345678909")]
    [TestCase("11144477735")]
    public void TryParse_ValidCpf_ReturnsTrueAndKeepsValue(string input)
    {
        // Act
        var ok = Cpf.TryParse(input, out var cpf);

        // Assert
        ok.Should().BeTrue();
        cpf!.Value.Should().Be(input);
    }

    [Test]
    public void TryParse_MaskedCpf_NormalizesValue()
    {
        // Act
        var ok = Cpf.TryParse("529.982.247-25", out var cpf);

        // Assert
        ok.Should().BeTrue();
        cpf!.Value.Should().Be("52998224725");
    }

    [TestCase("52998224726")]       // DV errado
    [TestCase("52998224735")]       // DV errado
    [TestCase("11111111111")]       // repetido: o cálculo do DV aceita, a Receita não
    [TestCase("00000000000")]
    [TestCase("5299822472")]        // curto demais
    [TestCase("529982247255")]      // longo demais
    [TestCase("5299822472A")]       // letra
    [TestCase("529,982,247-25")]    // separador não aceito
    [TestCase("٥٢٩٩٨٢٢٤٧٢٥")]       // dígitos de outro alfabeto (char.IsDigit os aceitaria)
    [TestCase("11222333000181")]    // um CNPJ não é um CPF
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void TryParse_InvalidCpf_ReturnsFalse(string? input)
    {
        // Act & Assert
        Cpf.TryParse(input, out _).Should().BeFalse();
    }

    [TestCase("52998224726")]
    [TestCase("")]
    [TestCase(null)]
    public void Parse_InvalidCpf_ThrowsFormatException(string? input)
    {
        // Act
        var act = () => Cpf.Parse(input);

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Test]
    public void Formatted_AppliesTheMask()
    {
        // Act & Assert
        Cpf.Parse("52998224725").Formatted.Should().Be("529.982.247-25");
    }

    [Test]
    public void Equality_SameCpfWithAndWithoutMask_AreEqual()
    {
        // Arrange
        var masked = Cpf.Parse("529.982.247-25");
        var plain = Cpf.Parse("52998224725");

        // Act & Assert
        masked.Should().Be(plain);
    }
}
