using ClickHouse.Client.ADO;
using GeoPulse.ClickHouse;
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
        /// <summary>DDL, который обеспечивает агрегатор: агрегаты и их источник.</summary>
        private static readonly string[] SchemaStatements =
        {
            ClickHouseSchema.CreateDatabaseSql,
            ClickHouseSchema.CreateMonthlyProductionSql,
            ClickHouseSchema.CreateProductionDailySql
        };

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

            await ClickHouseSchema.EnsureAsync(connectionString, SchemaStatements, stoppingToken);
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
