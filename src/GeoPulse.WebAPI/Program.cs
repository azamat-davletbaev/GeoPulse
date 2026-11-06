using System.Reflection;
using GeoPulse.Infrastructure;
using GeoPulse.WebAPI.Analytics;
using GeoPulse.WebAPI.Health;
using GeoPulse.WebAPI.Reports;
using Microsoft.Extensions.FileProviders;

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

var app = builder.Build();

// Необработанные исключения отдаются клиенту как application/problem+json (RFC 7807).
app.UseExceptionHandler();

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

app.MapControllers();

app.Run();
