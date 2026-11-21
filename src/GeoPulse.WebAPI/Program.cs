using System.Reflection;
using GeoPulse.Infrastructure;
using GeoPulse.WebAPI.Analytics;
using GeoPulse.WebAPI.Health;
using GeoPulse.WebAPI.Reports;
using Microsoft.Extensions.FileProviders;
#if DEBUG
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
#endif
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Подключаем XML-комментарии контроллера (summary) как описания в Swagger.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// Единая регистрация CoreDbContext и репозиториев (AddGeoPulseInfrastructure), как в Consumer.
// Имя строки подключения общее для всех сервисов — DatabaseConfiguration.ConnectionStringName.
var connectionString = builder.Configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName)
    ?? DatabaseConfiguration.DefaultConnectionString;

builder.Services.AddGeoPulseInfrastructure(connectionString);

// Аналитика читается из ClickHouse; контроллер и отчёты работают через интерфейс-порт.
builder.Services.AddSingleton<IMonthlyProductionReader, ClickHouseAnalyticsService>();

// Отчёты: загрузка данных и два независимых генератора форматов.
builder.Services.AddScoped<ReportDataLoader>();
builder.Services.AddSingleton<XlsxReportWriter>();
builder.Services.AddSingleton<CsvReportWriter>();

// /health проверяет PostgreSQL через пробу, а не через DbContext напрямую.
builder.Services.AddScoped<IDatabaseHealthProbe, DatabaseHealthProbe>();

#if DEBUG
// OpenTelemetry-трейсинг — ТОЛЬКО локальная разработка. Двойная защита:
// в Release-сборке (прод-образ) код исключается компилятором, а в Debug
// подключается лишь при ASPNETCORE_ENVIRONMENT=Development (launchSettings.json).
// Экспортёр — консольный: по завершении запроса в консоль печатается дерево
// спанов (TraceId/SpanId/ParentId, длительность, теги, текст SQL).
if (builder.Environment.IsDevelopment())
{
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("GeoPulse.WebAPI"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            // EF Core: в актуальной версии инструментации (1.21) текст SQL попадает
            // в спан всегда (атрибуты db.query.text / db.statement) — прежней опции
            // SetDbStatementForText больше нет, отдельное включение не требуется.
            .AddEntityFrameworkCoreInstrumentation()
            .AddConsoleExporter());
}
#endif

var app = builder.Build();

// Необработанные исключения отдаются клиенту как application/problem+json (RFC 7807).
// Это самый внешний middleware: он должен стоять выше всего, что может бросить исключение.
app.UseExceptionHandler();

// Метрики Prometheus (prometheus-net): HTTP-метрики + метрики рантайма/процесса.
// Стоит как можно раньше, чтобы в счётчики попадали ВСЕ запросы, включая 404 и
// будущие 401 (аутентификации в приложении пока нет). Наружу /metrics закрыт в Caddyfile.
app.UseHttpMetrics();

// Явный UseRouting ОБЯЗАТЕЛЕН: если его не вызвать, WebApplication сам оборачивает
// конвейер (UseRouting вставляется в начало), и тогда метрики считались бы уже
// ПОСЛЕ сопоставления маршрута. С явным вызовом порядок фиксирован:
// исключения -> метрики -> маршрутизация -> endpoints.
app.UseRouting();

// Swagger доступен во всех окружениях: это демо-стенд, а контракт смок-теста
// (tests/smoke/webapi-smoke.ps1) проверяет GET /swagger/v1/swagger.json на 200,
// в том числе при развёртывании на сервере (ASPNETCORE_ENVIRONMENT=Production).
app.UseSwagger();
app.UseSwaggerUI();

// Статический фронт (папка web/) — раздаётся самим WebAPI.
// Локально путь разрешается относительно корня содержимого (корень репозитория),
// в опубликованном/контейнерном образе — рядом с исполняемым файлом.
var webRoot = Path.Combine(app.Environment.ContentRootPath, "web");
if (!Directory.Exists(webRoot))
{
    webRoot = Path.Combine(AppContext.BaseDirectory, "web");
}

if (Directory.Exists(webRoot))
{
    var webFileProvider = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = webFileProvider });
}

// GET /metrics — exposition-формат Prometheus (метрики процесса .NET и HTTP).
// Регистрируется ПОСЛЕ UseRouting: это обычный endpoint (exposition-сервер
// prometheus-net), путь по умолчанию совпадает со scrape_configs в
// observability/prometheus/prometheus.yml.
app.MapMetrics();

app.MapControllers();

app.Run();
