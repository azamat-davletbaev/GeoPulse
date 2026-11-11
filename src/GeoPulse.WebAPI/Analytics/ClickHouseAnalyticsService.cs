using System.Data.Common;
using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using GeoPulse.ClickHouse;
using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Analytics
{
    /// <summary>
    /// Аналитика из ClickHouse (база gp). Помесячные агрегаты считаются на лету из сырых
    /// суточных строк gp.production_daily — предвычисленной таблицы агрегатов больше нет (ADR-016).
    /// </summary>
    public sealed class ClickHouseAnalyticsService : IMonthlyProductionReader
    {
        /// <summary>
        /// Агрегация суточных строк в помесячные. FINAL обязателен: дедупликацию повторов топика
        /// выполняет движок ReplacingMergeTree по (npd_well_bore_code, date). Порядок и имена колонок
        /// совпадают с прежней выборкой из gp.monthly_production, поэтому маппинг в DTO не изменился.
        /// </summary>
        private const string MonthlyAggregateSql =
            "SELECT well_id, toYear(date) AS year, toMonth(date) AS month, " +
            "sum(oil_vol) AS oil_vol_sum, sum(gas_vol) AS gas_vol_sum, sum(water_vol) AS water_vol_sum, " +
            "sum(water_injection_vol) AS water_injection_vol_sum, sum(on_stream_hrs) AS on_stream_hrs_sum, " +
            "count() AS days_count " +
            "FROM " + ClickHouseSchema.ProductionDailyTable + " FINAL ";

        /// <summary>Все помесячные агрегаты по всем скважинам.</summary>
        private const string AllMonthlySql =
            MonthlyAggregateSql + "GROUP BY well_id, year, month ORDER BY year, month, well_id";

        /// <summary>Помесячные агрегаты одной скважины; well_id передаётся параметром запроса.</summary>
        private const string MonthlyByWellSql =
            MonthlyAggregateSql + "WHERE well_id = {wellId:UInt32} GROUP BY well_id, year, month ORDER BY year, month";

        /// <summary>
        /// Итоги за всё время. Суммы те же, что давала сумма по месяцам, а count() суточных строк
        /// равен прежнему sum(days_count) по агрегатам.
        /// </summary>
        private const string SummarySql =
            "SELECT sum(oil_vol), sum(gas_vol), sum(water_vol), sum(water_injection_vol), " +
            "sum(on_stream_hrs), count() " +
            "FROM " + ClickHouseSchema.ProductionDailyTable + " FINAL";

        private readonly string _connectionString;

        /// <summary>Конструктор.</summary>
        public ClickHouseAnalyticsService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ClickHouse")
                ?? throw new InvalidOperationException("Не задана ConnectionStrings:ClickHouse в appsettings.json.");
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<MonthlyProduction>> GetMonthlyByWellAsync(
            int wellId,
            CancellationToken cancellationToken = default) =>
            QueryMonthlyAsync(MonthlyByWellSql, wellId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<MonthlyProduction>> GetAllMonthlyAsync(CancellationToken cancellationToken = default) =>
            QueryMonthlyAsync(AllMonthlySql, wellId: null, cancellationToken);

        /// <inheritdoc />
        public async Task<ProductionSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SummarySql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new ProductionSummary();
            }

            return new ProductionSummary
            {
                TotalOilVol = GetDouble(reader, 0),
                TotalGasVol = GetDouble(reader, 1),
                TotalWaterVol = GetDouble(reader, 2),
                TotalWaterInjectionVol = GetDouble(reader, 3),
                TotalOnStreamHrs = GetDouble(reader, 4),
                TotalDays = (int)GetDouble(reader, 5)
            };
        }

        private async Task<IReadOnlyList<MonthlyProduction>> QueryMonthlyAsync(
            string sql,
            int? wellId,
            CancellationToken cancellationToken)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            if (wellId.HasValue)
            {
                // Внешнее значение передаётся параметром запроса {wellId:UInt32}, а не подстановкой
                // в текст SQL: ClickHouse.Client 7.14 поддерживает AddParameter для placeholders.
                command.AddParameter("wellId", (uint)wellId.Value);
            }

            var result = new List<MonthlyProduction>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new MonthlyProduction
                {
                    WellId = (int)GetDouble(reader, 0),
                    Year = (int)GetDouble(reader, 1),
                    Month = (int)GetDouble(reader, 2),
                    OilVolSum = GetDouble(reader, 3),
                    GasVolSum = GetDouble(reader, 4),
                    WaterVolSum = GetDouble(reader, 5),
                    WaterInjectionVolSum = GetDouble(reader, 6),
                    OnStreamHrsSum = GetDouble(reader, 7),
                    DaysCount = (int)GetDouble(reader, 8)
                });
            }

            return result;
        }

        /// <summary>
        /// Читает числовое значение независимо от точного типа ClickHouse (UInt8/UInt16/UInt32/UInt64/Float64):
        /// драйвер возвращает соответствующий CLR-тип, приводим через Convert.
        /// </summary>
        private static double GetDouble(DbDataReader reader, int ordinal)
        {
            var value = reader.GetValue(ordinal);
            return value is null or DBNull ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
    }
}
