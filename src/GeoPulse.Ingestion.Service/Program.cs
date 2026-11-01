using GeoPulse.Infrastructure;
using GeoPulse.Ingestion.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json ищем рядом с dll (AppContext.BaseDirectory), а не по текущей рабочей директории,
// чтобы путь к Excel и строка подключения не зависели от места запуска процесса.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

var connectionString = builder.Configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName)
    ?? DatabaseConfiguration.DefaultConnectionString;

builder.Services.AddGeoPulseInfrastructure(connectionString);
builder.Services.AddScoped<IngestionService>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();
await ingestionService.RunAsync();
