using GeoPulse.Aggregator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json ищем рядом с dll (AppContext.BaseDirectory), а не по текущей рабочей директории.
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

builder.Services.AddHostedService<AggregatorService>();

using var host = builder.Build();
await host.RunAsync();
