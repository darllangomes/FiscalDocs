using System.Diagnostics.CodeAnalysis;

namespace FiscalDocs.Domain.ValueObjects;

public sealed record Cpf
{
    private const int Length = 11;

    public string Value { get; }

    private Cpf(string value) => Value = value;

    public string Formatted => $"{Value[..3]}.{Value[3..6]}.{Value[6..9]}-{Value[9..]}";

    public static bool TryParse(string? input, [NotNullWhen(true)] out Cpf? cpf)
    {
        cpf = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var digits = input.Replace(".", "").Replace("-", "");
        if (digits.Length != Length)
            return false;

        foreach (var c in digits)
        {
            // Não usar char.IsDigit: aceita dígitos Unicode de outros alfabetos.
            if (c is < '0' or > '9')
                return false;
        }

        // O cálculo do DV aceita 111.111.111-11, mas a Receita não emite esses números.
        if (digits.All(c => c == digits[0]))
            return false;

        if (digits[9] - '0' != CheckDigit(digits, 9) || digits[10] - '0' != CheckDigit(digits, 10))
            return false;

        cpf = new Cpf(digits);
        return true;
    }

    public static Cpf Parse(string? input) =>
        TryParse(input, out var cpf)
            ? cpf
            : throw new FormatException("CPF inválido.");

    // Pesos de (count + 1) até 2, da esquerda para a direita.
    private static int CheckDigit(string digits, int count)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
            sum += (digits[i] - '0') * (count + 1 - i);

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
