using System.Reflection;
using GeoPulse.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
