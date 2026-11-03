using ClickHouse.Client.ADO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoPulse.Aggregator
{
    /// <summary>
    /// Пересчёт помесячных агрегатов из ClickHouse: на старте и далее по таймеру
    /// (RecalcIntervalSeconds) выполняет полный пересбор gp.monthly_production
    /// из gp.production_daily (FINAL) — без инкрементального состояния.
    /// </summary>
    public sealed class AggregatorService : BackgroundService
    {
        private const string CreateDatabaseSql = "CREATE DATABASE IF NOT EXISTS gp";

        private const string CreateMonthlySql =
            "CREATE TABLE IF NOT EXISTS gp.monthly_production (" +
            "well_id UInt32," +
            "year UInt16," +
            "month UInt8," +
            "oil_vol_sum Float64," +
            "gas_vol_sum Float64," +
            "water_vol_sum Float64," +
            "water_injection_vol_sum Float64," +
            "on_stream_hrs_sum Float64," +
            "days_count UInt64" +
            ") ENGINE = MergeTree ORDER BY (well_id, year, month)";

        private const string CreateProductionDailySql =
            "CREATE TABLE IF NOT EXISTS gp.production_daily (" +
            "well_id UInt32," +
            "npd_well_bore_code UInt32," +
            "date Date," +
            "oil_vol Float64," +
            "gas_vol Float64," +
            "water_vol Float64," +
            "water_injection_vol Float64," +
            "on_stream_hrs Float64," +
            "avg_downhole_pressure Float64," +
            "avg_downhole_temperature Float64," +
            "avg_dp_tubing Float64," +
            "avg_annulus_pressure Float64," +
            "avg_choke_size Float64," +
            "avg_whp Float64," +
            "avg_wht Float64," +
            "dp_choke_size Float64" +
            ") ENGINE = ReplacingMergeTree ORDER BY (npd_well_bore_code, date)";

        private const string RebuildInsertSql =
            "INSERT INTO gp.monthly_production " +
            "SELECT well_id, toYear(date) AS year, toMonth(date) AS month, " +
            "sum(oil_vol), sum(gas_vol), sum(water_vol), sum(water_injection_vol), " +
            "sum(on_stream_hrs), count() AS days_count " +
            "FROM gp.production_daily FINAL " +
            "GROUP BY well_id, year, month";

        private readonly IConfiguration _configuration;
        private readonly ILogger<AggregatorService> _logger;

        /// <summary>Конструктор.</summary>
        public AggregatorService(IConfiguration configuration, ILogger<AggregatorService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var connectionString = _configuration["ClickHouse:ConnectionString"]
                ?? throw new InvalidOperationException("Не задан ClickHouse:ConnectionString в appsettings.json.");
            var intervalSeconds = _configuration.GetValue("RecalcIntervalSeconds", 60);
            if (intervalSeconds <= 0)
            {
                intervalSeconds = 60;
            }

            await EnsureSchemaAsync(connectionString, stoppingToken);
            _logger.LogInformation("Агрегатор запущен: интервал {Interval} сек", intervalSeconds);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
            do
            {
                try
                {
                    await RebuildAsync(connectionString, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при пересчёте месячных агрегатов; следующий тик повторит попытку");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        /// <summary>Создаёт базу gp и таблицы monthly_production / production_daily в ClickHouse.</summary>
        private static async Task EnsureSchemaAsync(string connectionString, CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            command.CommandText = CreateDatabaseSql;
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = CreateMonthlySql;
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = CreateProductionDailySql;
            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Полный пересбор месячных агрегатов: TRUNCATE + INSERT ... SELECT ... FINAL.</summary>
        private async Task RebuildAsync(string connectionString, CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            command.CommandText = "TRUNCATE TABLE gp.monthly_production";
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = RebuildInsertSql;
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = "SELECT count() FROM gp.monthly_production";
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct));

            _logger.LogInformation("Агрегатор: строк пересчитано {Count}", count);
        }
    }
}
