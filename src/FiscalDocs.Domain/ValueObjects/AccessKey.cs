using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FiscalDocs.Domain.ValueObjects;

public sealed record AccessKey
{
    // Um CPF ocupa as 11 últimas posições do campo do emitente, que tem 14.
    private const string CpfPadding = "000";

    public string Value { get; }
    public string Model => Value[20..22];

    // Posições 7 a 20: CNPJ do emitente ou CPF com zeros à esquerda. Um CNPJ também pode começar com 000,
    // então a chave sozinha não diz o tipo: quem decide é o elemento do XML (IsIssuedBy).
    private string IssuerField => Value[6..20];

    private AccessKey(string value) => Value = value;

    public bool IsIssuedBy(TaxId issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        return issuer.IsCpf
            ? IssuerField == CpfPadding + issuer.Value
            : IssuerField == issuer.Value;
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

        var issuerField = normalized[6..20];
        var validIssuer = Cnpj.TryParse(issuerField, out _) ||
                          (issuerField.StartsWith(CpfPadding, StringComparison.Ordinal) &&
                           Cpf.TryParse(issuerField[CpfPadding.Length..], out _));
        if (!validIssuer)
            return false;

        key = new AccessKey(normalized);
        return true;
    }
}
