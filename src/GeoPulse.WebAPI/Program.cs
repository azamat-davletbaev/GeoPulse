using System.Reflection;
using GeoPulse.Infrastructure;
using GeoPulse.WebAPI;

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

app.MapControllers();

app.Run();
