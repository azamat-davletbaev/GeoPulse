namespace GeoPulse.Tests.TestHelpers;

/// <summary>
/// Лёгкий in-memory хост контроллеров WebAPI (TestServer) с подменой зависимостей.
/// Позволяет проверять реальный HTTP-контракт (статус-коды, JSON-сериализацию, camelCase)
/// без поднятия внешней БД и без изменения продового Program.cs.
/// </summary>
internal static class ApiTestHost
{
    public static async Task<(WebApplication App, HttpClient Client)> StartAsync(Action<IServiceCollection> configureServices)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AnalyticsController).Assembly);
        builder.Services.AddProblemDetails();

        configureServices(builder.Services);

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }
}
