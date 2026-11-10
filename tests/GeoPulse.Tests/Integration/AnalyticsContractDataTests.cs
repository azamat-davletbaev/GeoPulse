using System.Net;

namespace GeoPulse.Tests.Integration;

/// <summary>
/// Интеграционные тесты /api/Analytics/monthly и /summary на реальном ClickHouse (fixture).
/// SQL и маппинг проверяются как в проде: читатель подключается к Testcontainers-ClickHouse,
/// схема создаётся тем же ClickHouseSchema, что используют CHIngest и Aggregator.
/// </summary>
[Collection("integration")]
[Trait("Category", "Integration")]
public sealed class AnalyticsContractDataTests
{
    private readonly GeoPulseContainersFixture _fixture;

    public AnalyticsContractDataTests(GeoPulseContainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Monthly_WithWellId_ReturnsData()
    {
        await SeedMonthlyAsync();
        var (app, client) = await StartAsync();

        try
        {
            var response = await client.GetAsync("/api/Analytics/monthly?wellId=5351");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var items = doc.RootElement.EnumerateArray().ToArray();
            Assert.Single(items);
            Assert.Equal(100.5, items[0].GetProperty("oilVolSum").GetDouble());
            Assert.Equal(2016, items[0].GetProperty("year").GetInt32());
            Assert.Equal(1, items[0].GetProperty("month").GetInt32());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Monthly_WithWellCode_LegacyContractStillWorks()
    {
        await SeedMonthlyAsync();
        var (app, client) = await StartAsync();

        try
        {
            var response = await client.GetAsync("/api/Analytics/monthly?wellCode=5351");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var items = doc.RootElement.EnumerateArray().ToArray();
            Assert.Single(items);
            Assert.Equal(100.5, items[0].GetProperty("oilVolSum").GetDouble());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Summary_ReturnsTotals_WithUnchangedContract()
    {
        await SeedMonthlyAsync();
        var (app, client) = await StartAsync();

        try
        {
            var response = await client.GetAsync("/api/Analytics/summary");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(100.5, root.GetProperty("totalOilVol").GetDouble());
            Assert.Equal(10.5, root.GetProperty("totalGasVol").GetDouble());
            Assert.Equal(20.5, root.GetProperty("totalWaterVol").GetDouble());
            Assert.Equal(30.5, root.GetProperty("totalWaterInjectionVol").GetDouble());
            Assert.Equal(24.0, root.GetProperty("totalOnStreamHrs").GetDouble());
            Assert.Equal(1, root.GetProperty("totalDays").GetInt32());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private async Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client)> StartAsync()
    {
        var wells = new FakeWellRepository(wells: new[]
        {
            new Well { Id = 5351, NpdWellBoreCode = 5351, Name = "W", Field = "Volve", Type = "OP" }
        });

        var analytics = new ClickHouseAnalyticsService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClickHouse"] = _fixture.ClickHouseConnectionString
            })
            .Build());

        return await ApiTestHost.StartAsync(services =>
        {
            services.AddSingleton<IWellRepository>(wells);
            services.AddSingleton<IMonthlyProductionReader>(analytics);
        });
    }

    private async Task SeedMonthlyAsync()
    {
        // Схема создаётся общим ClickHouseSchema — тем же кодом, что в CHIngest и Aggregator.
        await ClickHouseSchema.EnsureAsync(
            _fixture.ClickHouseConnectionString,
            new[]
            {
                ClickHouseSchema.CreateDatabaseSql,
                ClickHouseSchema.CreateMonthlyProductionSql
            });

        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = $"TRUNCATE TABLE IF EXISTS {ClickHouseSchema.MonthlyProductionTable}";
        await command.ExecuteNonQueryAsync();

        command.CommandText =
            $"INSERT INTO {ClickHouseSchema.MonthlyProductionTable} VALUES " +
            "(5351, 2016, 1, 100.5, 10.5, 20.5, 30.5, 24.0, 1)";
        await command.ExecuteNonQueryAsync();
    }
}
