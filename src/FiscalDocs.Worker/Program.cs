using FiscalDocs.Infrastructure;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDocumentIngestedProcessor, DocumentIngestedProcessor>();
builder.Services.AddSingleton<DocumentIngestedConsumer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
