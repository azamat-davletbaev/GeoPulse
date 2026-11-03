using GeoPulse.Ingestion.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json ищем рядом с dll (AppContext.BaseDirectory), а не по текущей рабочей директории,
// чтобы путь к Excel и настройки Kafka не зависели от места запуска процесса.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
// Env-переменные добавляем после JSON, чтобы при запуске в контейнере значения из окружения
// (Kafka__BootstrapServers, Kafka__Topic) переопределяли appsettings.json.
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddScoped<IngestionService>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();
await ingestionService.RunAsync();
