using FiscalDocs.Application.Documents;
using FiscalDocs.Domain.Enums;
using FiscalDocs.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FiscalDocs.Api.Contracts;

public sealed class ListDocumentsRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? IssuerCnpj { get; init; }
    public string? IssuerState { get; init; }
    public DateOnly? IssuedFrom { get; init; }
    public DateOnly? IssuedTo { get; init; }

    /// <summary>Validates the request; errors go to <paramref name="errors"/> and the result is null.</summary>
    public ListDocumentsQuery? ToQuery(ModelStateDictionary errors)
    {
        if (Page < 1)
            errors.AddModelError(nameof(Page), "page must be 1 or greater.");
        if (PageSize is < 1 or > MaxPageSize)
            errors.AddModelError(nameof(PageSize), $"pageSize must be between 1 and {MaxPageSize}.");

        var type = ParseEnum<DocumentType>(Type, nameof(Type), errors);
        var status = ParseEnum<DocumentStatus>(Status, nameof(Status), errors);

        Cnpj? cnpj = null;
        if (IssuerCnpj is not null && !Cnpj.TryParse(IssuerCnpj, out cnpj))
            errors.AddModelError(nameof(IssuerCnpj), "issuerCnpj is not a valid CNPJ.");

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
            ? new ListDocumentsQuery(Page, PageSize, type, status, cnpj, state, IssuedFrom, IssuedTo)
            : null;
    }

    // Matches names only, so numeric values such as "1" are rejected.
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
