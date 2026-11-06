using Confluent.Kafka;
using GeoPulse.CHIngest;
using GeoPulse.Infrastructure;
using GeoPulse.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json ищем рядом с dll (AppContext.BaseDirectory), а не по текущей рабочей директории.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
// Env-переменные добавляем после JSON, чтобы при запуске в контейнере значения из окружения
// (ConnectionStrings__CoreDb, ClickHouse__ConnectionString, Kafka__*) переопределяли appsettings.json.
builder.Configuration.AddEnvironmentVariables();

var bootstrapServers = builder.Configuration["Kafka:BootstrapServers"]
    ?? throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");

var postgresConnectionString = builder.Configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName)
    ?? throw new InvalidOperationException("Не задана ConnectionStrings:CoreDb в appsettings.json.");

// Один продюсер на DLQ: публикует некорректные события в production-daily-dlq.
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build());
builder.Services.AddSingleton<DlqPublisher>();

// Создание топика при старте: CHIngest может быть запущен до первого прогона ingestion.
builder.Services.AddSingleton<TopicProvisioner>();

// Резолв npdWellBoreCode → well_id: справочник читается из PostgreSQL с кэшем.
builder.Services.AddGeoPulseWellIdResolver(postgresConnectionString);

builder.Services.AddHostedService<CHIngestService>();

using var host = builder.Build();
await host.RunAsync();
