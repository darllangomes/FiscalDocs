using System.Text.Json.Serialization;
using FiscalDocs.Application;
using FiscalDocs.Infrastructure;
using FiscalDocs.Infrastructure.Persistence;
using FiscalDocs.Api.ErrorHandling;
using FiscalDocs.Api.Authentication;
using FiscalDocs.Api.Health;
using FiscalDocs.Api.RateLimiting;
using FiscalDocs.Api.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FiscalDocs API",
        Version = "v1",
        Description = """
            Ingestão e consulta de documentos fiscais (**NFe** e **CTe**) a partir do XML.

            **Como autenticar.** Todas as rotas exigem o header `X-Api-Key`. Clique em **Authorize**, cole a chave
            (a que você configurou em `Auth:ApiKey`, ou `API_KEY` no `.env`) e confirme.

            **Fluxo para experimentar:**
            1. `POST /api/v1/documents`: envie um XML (o exemplo do corpo já é um documento válido). `201` cria; reenviar o mesmo XML dá `200`.
            2. `GET /api/v1/documents`: liste e copie o `id` de um item.
            3. `GET /api/v1/documents/{id}`: leia o documento e anote o `ETag` da resposta.
            4. `PATCH /api/v1/documents/{id}`: informe o ETag (entre aspas) em `If-Match` e altere `status` ou `notes`.
            5. `GET /api/v1/documents/{id}/xml`: baixe o XML original.

            O roteiro completo, com os casos de erro, está em `docs/manual-testing.md`.
            """
    });

    var xmlComments = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlComments))
        options.IncludeXmlComments(xmlComments);

    options.OperationFilter<OperationDocumentationFilter>();
    options.SchemaFilter<ContractDocumentationFilter>();

    options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyAuthenticationHandler.HeaderName,
        Description = "Chave de API enviada no header X-Api-Key."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = ApiKeyAuthenticationHandler.SchemeName
            }
        }] = []
    });
});
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services
    .AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Auth:ApiKey must be configured.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddApiHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FiscalDocsDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// As sondas de saúde chegam por HTTP simples e não podem ser redirecionadas.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments(HealthExtensions.BasePath),
    branch => branch.UseHttpsRedirection());

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapApiHealthChecks();

app.Run();

public partial class Program { }
