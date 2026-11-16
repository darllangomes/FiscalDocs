using System.Diagnostics.CodeAnalysis;
using FiscalDocs.Domain.Privacy;

namespace FiscalDocs.Domain.ValueObjects;

/// <summary>
/// Documento de um contribuinte: CNPJ (14 posições) ou CPF (11). O tamanho distingue os dois.
/// </summary>
public sealed record TaxId
{
    private const int CpfLength = 11;

    public string Value { get; }

    private TaxId(string value) => Value = value;

    public bool IsCpf => Value.Length == CpfLength;

    public bool IsCnpj => !IsCpf;

    /// <summary>O que pode sair da API: o CNPJ é público, o CPF fica mascarado.</summary>
    public string Masked => IsCpf ? CpfMasker.Mask(Value) : Value;

    public static TaxId From(Cnpj cnpj) => new(cnpj.Value);

    public static TaxId From(Cpf cpf) => new(cpf.Value);

    public static bool TryParse(string? input, [NotNullWhen(true)] out TaxId? taxId)
    {
        taxId = null;

        if (Cnpj.TryParse(input, out var cnpj))
            taxId = From(cnpj);
        else if (Cpf.TryParse(input, out var cpf))
            taxId = From(cpf);

        return taxId is not null;
    }

    public static TaxId Parse(string? input) =>
        TryParse(input, out var taxId)
            ? taxId
            : throw new FormatException("CPF ou CNPJ inválido.");
}
