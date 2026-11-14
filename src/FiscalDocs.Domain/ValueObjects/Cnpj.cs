using System.Diagnostics.CodeAnalysis;

namespace FiscalDocs.Domain.ValueObjects;

using System.Text.RegularExpressions;


public sealed record Cnpj
{
    public string Value { get; }

    private Cnpj(string value) => Value = value;

    public string Formatted => $"{Value[..2]}.{Value[2..5]}.{Value[5..8]}/{Value[8..12]}-{Value[12..]}";

    public static bool TryParse(string? input, [NotNullWhen(true)] out Cnpj? cnpj)
    {

        cnpj = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        var stripped = input.Replace(".", "").Replace("/", "").Replace("-", "");
        if (!Regex.IsMatch(stripped, @"\A[0-9A-Za-z]{12}[0-9]{2}\z"))
            return false;

        var normalized = stripped.ToUpperInvariant();
        if (normalized[12] - '0' != Mod11.ComputeCheckDigit(normalized.AsSpan(0, 12)))
            return false;
        if (normalized[13] - '0' != Mod11.ComputeCheckDigit(normalized.AsSpan(0, 13)))
            return false;
        cnpj = new Cnpj(normalized);
        return true;
    }

    public static Cnpj Parse(string? input) =>
     TryParse(input, out var cnpj)
         ? cnpj
         : throw new FormatException("CNPJ inválido.");

}