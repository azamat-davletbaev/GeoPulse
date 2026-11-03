using System.Reflection;
using GeoPulse.Infrastructure;
using GeoPulse.WebAPI;
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

// Единая регистрация CoreDbContext и репозиториев (тот же extension, что использует Ingestion.Service).
var connectionString = builder.Configuration.GetConnectionString("GeoPulse")
    ?? DatabaseConfiguration.DefaultConnectionString;

builder.Services.AddGeoPulseInfrastructure(connectionString);
builder.Services.AddSingleton<ClickHouseAnalyticsService>();

var app = builder.Build();

// Необработанные исключения отдаются клиенту как application/problem+json (RFC 7807).
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
