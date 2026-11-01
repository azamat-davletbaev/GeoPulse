using System.Reflection;
using GeoPulse.WebAPI.Data;
using GeoPulse.WebAPI.Data.Import;
using Microsoft.EntityFrameworkCore;

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

var connectionString = builder.Configuration.GetConnectionString(DataConfiguration.ConnectionStringName)
    ?? DataConfiguration.DefaultConnectionString;

var importOptions = builder.Configuration
    .GetSection(VolveImportOptions.SectionName)
    .Get<VolveImportOptions>() ?? new VolveImportOptions();
builder.Services.AddSingleton(importOptions);

builder.Services.AddDbContextFactory<GeoPulseDbContext>(options =>
    options.UseGeoPulseDatabase(connectionString, importOptions.BatchSize));
builder.Services.AddScoped(serviceProvider =>
    serviceProvider.GetRequiredService<IDbContextFactory<GeoPulseDbContext>>().CreateDbContext());
builder.Services.AddScoped<VolveImportService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Схема создаётся миграцией (wells, wellbores, drilling_reports, production_daily, ...).
// Применение при старте включается флагом Database:AutoMigrate.
if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationFactory = migrationScope.ServiceProvider.GetRequiredService<IDbContextFactory<GeoPulseDbContext>>();
    await using var migrationDb = await migrationFactory.CreateDbContextAsync();
    await migrationDb.Database.MigrateAsync();
}

app.MapControllers();

// Режим командной строки: dotnet run -- --import
// Применяет миграцию и загружает данные из папки xml пакетами по 1000 записей.
if (args.Contains("--import", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<GeoPulseDbContext>>();
    await using (var db = await factory.CreateDbContextAsync())
    {
        await db.Database.MigrateAsync();
    }

    var importer = scope.ServiceProvider.GetRequiredService<VolveImportService>();
    var result = await importer.ImportAsync();

    Console.WriteLine(
        $"Импорт Volve: {result.TotalRows} записей, {result.BatchCount} пакетов по {result.BatchSize}; "
        + $"скважин {result.Wells}, стволов {result.Wellbores}, отчётов {result.DrillingReports}, "
        + $"добыча (сут.) {result.ProductionDaily}, добыча (мес.) {result.ProductionMonthly}, "
        + $"за {result.Duration.TotalSeconds:0.0} с");

    return;
}

app.Run();
