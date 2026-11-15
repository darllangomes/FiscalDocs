using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FiscalDocs.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly byte[] _expectedHash;

    public ApiKeyAuthenticationHandler
    (
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiKeyOptions> apiKeyOptions
    ) : base(options, logger, encoder)
    {
        _expectedHash = Hash(apiKeyOptions.Value.ApiKey);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || provided.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        if (!CryptographicOperations.FixedTimeEquals(Hash(provided[0]), _expectedHash))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "api-client")],
            Scheme.Name
        );
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static byte[] Hash(string? value) => SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
}


