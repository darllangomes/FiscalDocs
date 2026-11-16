using FiscalDocs.Infrastructure;
using FiscalDocs.Infrastructure.Messaging;
using FiscalDocs.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDocumentIngestedProcessor, DocumentIngestedProcessor>();
builder.Services.AddSingleton<DocumentIngestedConsumer>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddScoped<OutboxCleaner>();
builder.Services.AddHostedService<OutboxRelayService>();
builder.Services.AddHostedService<OutboxCleanupService>();

var host = builder.Build();
host.Run();
