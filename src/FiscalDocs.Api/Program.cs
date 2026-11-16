using System.Text.Json.Serialization;
using FiscalDocs.Application;
using FiscalDocs.Infrastructure;
using FiscalDocs.Infrastructure.Persistence;
using FiscalDocs.Api.ErrorHandling;
using FiscalDocs.Api.Authentication;
using FiscalDocs.Api.Health;
using FiscalDocs.Api.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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

app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapApiHealthChecks();

app.Run();

public partial class Program { }
