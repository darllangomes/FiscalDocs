using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FiscalDocs.Domain.ValueObjects;

public sealed record AccessKey
{
    public string Value { get; }
    public string Model => Value[20..22];

    public Cnpj IssuerCnpj { get; }

    private AccessKey(string value, Cnpj issuerCnpj)
    {
        Value = value;
        IssuerCnpj = issuerCnpj;
    }

    public static AccessKey Parse(string? input) =>
    TryParse(input, out var key) ? key
    : throw new FormatException("Invalid Access Key.");

    public static bool TryParse(string? input, [NotNullWhen(true)] out AccessKey? key)
    {
        key = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        var stripped = input.Replace(" ", "");
        if (!Regex.IsMatch(stripped, @"\A[0-9]{6}[0-9A-Za-z]{12}[0-9]{26}\z"))
        {
            return false;
        }
        var normalized = stripped.ToUpperInvariant();
        if (normalized[^1] - '0' != Mod11.ComputeCheckDigit(normalized.AsSpan(0, 43)))
        {
            return false;
        }
        if (!Cnpj.TryParse(normalized[6..20], out var issuerCnpj))
            return false;

        key = new AccessKey(normalized, issuerCnpj);
        return true;
    }
}
