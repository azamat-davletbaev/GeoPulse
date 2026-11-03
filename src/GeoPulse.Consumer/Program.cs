using Confluent.Kafka;
using GeoPulse.Consumer;
using GeoPulse.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json ищем рядом с dll (AppContext.BaseDirectory), а не по текущей рабочей директории.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
// Env-переменные добавляем после JSON, чтобы при запуске в контейнере значения из окружения
// (ConnectionStrings__CoreDb, Kafka__*) переопределяли appsettings.json.
builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName)
    ?? DatabaseConfiguration.DefaultConnectionString;

builder.Services.AddGeoPulseInfrastructure(connectionString);

var bootstrapServers = builder.Configuration["Kafka:BootstrapServers"]
    ?? throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");

// Один продюсер на DLQ: публикует оригинальные сообщения в production-daily-dlq.
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build());

builder.Services.AddHostedService<ProductionConsumerService>();

using var host = builder.Build();
await host.RunAsync();
