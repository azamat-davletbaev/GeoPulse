using GeoPulse.Infrastructure;
using GeoPulse.Ingestion.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName)
    ?? DatabaseConfiguration.DefaultConnectionString;

builder.Services.AddGeoPulseInfrastructure(connectionString);
builder.Services.AddScoped<IngestionService>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();
await ingestionService.RunAsync();
