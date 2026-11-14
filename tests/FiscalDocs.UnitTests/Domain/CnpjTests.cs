using FiscalDocs.Domain.ValueObjects;
using FluentAssertions;

namespace FiscalDocs.UnitTests.Domain;

[TestFixture]
public class CnpjTests
{
    [TestCase("11222333000181")]   // numérico (legado)
    [TestCase("12ABC34501DE35")]   // alfanumérico (exemplo oficial da Receita)
    [TestCase("44555666000181")]
    [TestCase("7K9X2M4P000144")]
    [TestCase("A1B2C3D4000193")]
    public void TryParse_ValidCnpj_ReturnsTrueAndKeepsValue(string input)
    {
        var ok = Cnpj.TryParse(input, out var cnpj);
        ok.Should().BeTrue();
        cnpj!.Value.Should().Be(input);
    }
    [TestCase("11.222.333/0001-81", "11222333000181")]
    [TestCase("12.ABC.345/01DE-35", "12ABC34501DE35")]
    [TestCase("12abc34501de35", "12ABC34501DE35")]
    public void TryParse_MaskedOrLowerCase_NormalizesValue(string input, string expected)
    {
        var ok = Cnpj.TryParse(input, out var cnpj);
        ok.Should().BeTrue();
        cnpj!.Value.Should().Be(expected);
    }
    [TestCase("11222333000182")]    // DV errado (numérico)
    [TestCase("12ABC34501DE36")]    // DV errado (alfanumérico)
    [TestCase("1122233300018")]     // 13 caracteres
    [TestCase("112223330001811")]   // 15 caracteres
    [TestCase("12ABC34501DE3A")]    // letra nas posições do DV
    [TestCase("1122233300018!")]    // caractere inválido
    [TestCase("11,222,333/0001-81")] // separador não aceito
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]

    public void TryParse_Invalid_Cnpj_ReturnsFalse(string? input)
    {
        Cnpj.TryParse(input, out _).Should().BeFalse();
    }

    [TestCase("11222333000182")]
    [TestCase("")]
    [TestCase(null)]

    public void Parse_Invalid_Cnpj_ThrowsFormatException(string? input)
    {
        var act = () => Cnpj.Parse(input);
        act.Should().Throw<FormatException>();
    }
    [Test]
    public void Equality_SameCnpjWithAndWithoutMask_AreEqual()
    {
        var masked = Cnpj.Parse("11.222.333/0001-81");
        var plain = Cnpj.Parse("11222333000181");
        masked.Should().Be(plain);
    }
    [Test]
    public void Formatted_AlphanumericCnpj_AppliedMask()
    {
        Cnpj.Parse("12ABC34501DE35").Formatted.Should().Be("12.ABC.345/01DE-35");
        
    }
}