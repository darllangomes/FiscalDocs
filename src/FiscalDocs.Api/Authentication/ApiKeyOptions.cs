namespace FiscalDocs.Api.Authentication;

public sealed class ApiKeyOptions
{
    public const string SectionName = "Auth";

    public string ApiKey { get; init; } = string.Empty;
}
