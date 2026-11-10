using System.Globalization;

namespace GeoPulse.Tests.Integration;

/// <summary>
/// Интеграционные тесты агрегатора (ClickHouse) через Testcontainers.
/// Проверяют, что первый тик не падает при отсутствии gp.production_daily
/// и что помесячные агрегаты пересчитываются после появления данных.
/// </summary>
[Collection("integration")]
[Trait("Category", "Integration")]
public sealed class AggregatorServiceTests
{
    private readonly GeoPulseContainersFixture _fixture;

    public AggregatorServiceTests(GeoPulseContainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FirstTick_WhenProductionDailyMissing_CreatesTableAndAggregates()
    {
        const int wellId = 7371;
        const int npdCode = 7371;
        var date = new DateOnly(2016, 3, 15);

        await DropAggregatorTablesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClickHouse:ConnectionString"] = _fixture.ClickHouseConnectionString,
                ["RecalcIntervalSeconds"] = "1"
            })
            .Build();

        var service = new AggregatorService(config, NullLogger<AggregatorService>.Instance);

        try
        {
            await service.StartAsync(CancellationToken.None);

            // Первый тик должен сам создать gp.production_daily, а не упасть.
            await WaitUntilAsync(async () => await TableExistsAsync("production_daily"));

            // Имитируем данные, которые записал CHIngest.
            await InsertProductionDailyAsync(wellId, npdCode, date, oilVol: 100.0, gasVol: 50.0);

            // Следующий тик полностью пересоберёт gp.monthly_production.
            await WaitUntilAsync(async () => await MonthlyOilSumAsync(wellId, 2016, 3) == 100.0);

            Assert.Equal(100.0, await MonthlyOilSumAsync(wellId, 2016, 3));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private async Task DropAggregatorTablesAsync()
    {
        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = "CREATE DATABASE IF NOT EXISTS gp";
        await command.ExecuteNonQueryAsync();

        command.CommandText = "DROP TABLE IF EXISTS gp.production_daily";
        await command.ExecuteNonQueryAsync();

        command.CommandText = "DROP TABLE IF EXISTS gp.monthly_production";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT count() FROM system.tables WHERE database = 'gp' AND name = '{tableName}'";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task InsertProductionDailyAsync(
        int wellId, int npdCode, DateOnly date, double oilVol, double gasVol)
    {
        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO gp.production_daily " +
            "(well_id, npd_well_bore_code, date, oil_vol, gas_vol, water_vol, water_injection_vol, on_stream_hrs) " +
            $"VALUES ({wellId}, {npdCode}, '{date:yyyy-MM-dd}', " +
            $"{oilVol.ToString(CultureInfo.InvariantCulture)}, {gasVol.ToString(CultureInfo.InvariantCulture)}, 0, 0, 24)";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<double> MonthlyOilSumAsync(int wellId, int year, int month)
    {
        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT oil_vol_sum FROM gp.monthly_production " +
            $"WHERE well_id = {wellId} AND year = {year} AND month = {month}";
        var result = await command.ExecuteScalarAsync();
        return result is null ? 0 : Convert.ToDouble(result, CultureInfo.InvariantCulture);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMilliseconds = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch
            {
                // Таблица/строка ещё не готовы — ждём дальше.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Условие не выполнилось за отведённое время.");
    }
}
