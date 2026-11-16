using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FiscalDocs.Api.Contracts;

public sealed class ListDocumentsRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Número da página, a partir de 1.</summary>
    /// <example>1</example>
    public int Page { get; init; } = 1;

    /// <summary>Itens por página, de 1 a 100.</summary>
    /// <example>20</example>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Tipo do documento: `NFe` ou `CTe`. Não diferencia maiúsculas de minúsculas.</summary>
    /// <example>NFe</example>
    public string? Type { get; init; }

    /// <summary>Status de gestão: `Received`, `Reviewed` ou `Archived`.</summary>
    /// <example>Received</example>
    public string? Status { get; init; }

    /// <summary>
    /// CNPJ ou CPF do emitente, com ou sem máscara e em qualquer caixa. O CPF completo vai na URL, que proxies
    /// podem registrar. CNPJ e CPF não se misturam: filtrar por um CNPJ nunca devolve um emitente pessoa física.
    /// </summary>
    /// <example>11222333000181</example>
    public string? IssuerDocument { get; init; }

    // Nome antigo do filtro (escondido do Swagger). Ignorá-lo em silêncio devolveria a lista inteira a um cliente que ainda o envia.
    public string? IssuerCnpj { get; init; }

    /// <summary>UF do emitente, com duas letras.</summary>
    /// <example>PE</example>
    public string? IssuerState { get; init; }

    /// <summary>Emissão a partir deste dia (AAAA-MM-DD), inclusive. Os dias são interpretados em UTC.</summary>
    /// <example>2026-08-01</example>
    public DateOnly? IssuedFrom { get; init; }

    /// <summary>Emissão até este dia (AAAA-MM-DD), inclusive. Os dias são interpretados em UTC.</summary>
    /// <example>2026-08-31</example>
    public DateOnly? IssuedTo { get; init; }

    /// <summary>Valida a requisição; os erros vão para <paramref name="errors"/> e o resultado é nulo.</summary>
    public ListDocumentsQuery? ToQuery(ModelStateDictionary errors)
    {
        if (Page < 1)
            errors.AddModelError(nameof(Page), "page must be 1 or greater.");
        if (PageSize is < 1 or > MaxPageSize)
            errors.AddModelError(nameof(PageSize), $"pageSize must be between 1 and {MaxPageSize}.");

        var type = ParseEnum<DocumentType>(Type, nameof(Type), errors);
        var status = ParseEnum<DocumentStatus>(Status, nameof(Status), errors);

        TaxId? issuer = null;
        if (IssuerDocument is not null && !TaxId.TryParse(IssuerDocument, out issuer))
            errors.AddModelError(nameof(IssuerDocument), "issuerDocument is not a valid CNPJ or CPF.");
        if (IssuerCnpj is not null)
            errors.AddModelError(nameof(IssuerCnpj), "issuerCnpj was renamed to issuerDocument.");

        string? state = null;
        if (IssuerState is not null)
        {
            state = IssuerState.Trim();
            if (state.Length != 2 || !state.All(char.IsAsciiLetter))
                errors.AddModelError(nameof(IssuerState), "issuerState must be a two-letter code.");
            state = state.ToUpperInvariant();
        }

        if (IssuedFrom > IssuedTo)
            errors.AddModelError(nameof(IssuedFrom), "issuedFrom must not be after issuedTo.");

        return errors.IsValid
            ? new ListDocumentsQuery(Page, PageSize, type, status, issuer, state, IssuedFrom, IssuedTo)
            : null;
    }

    // Aceita só nomes; valores numéricos como "1" são rejeitados.
    private static T? ParseEnum<T>(string? value, string field, ModelStateDictionary errors) where T : struct, Enum
    {
        if (value is null) return null;
        var name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            errors.AddModelError(field, $"{char.ToLowerInvariant(field[0])}{field[1..]} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
            return null;
        }
        return Enum.Parse<T>(name);
    }
}
