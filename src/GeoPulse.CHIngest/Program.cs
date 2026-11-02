using Confluent.Kafka;
using GeoPulse.CHIngest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json ищем рядом с dll (AppContext.BaseDirectory), а не по текущей рабочей директории.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

var bootstrapServers = builder.Configuration["Kafka:BootstrapServers"]
    ?? throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");

// Один продюсер на DLQ: публикует некорректные события в production-daily-dlq.
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build());

builder.Services.AddHostedService<CHIngestService>();

using var host = builder.Build();
await host.RunAsync();
