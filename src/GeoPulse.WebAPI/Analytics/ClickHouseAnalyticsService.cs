using System.Data.Common;
using System.Globalization;
using ClickHouse.Client.ADO;
using GeoPulse.ClickHouse;
using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Analytics
{
    /// <summary>Аналитика из ClickHouse (база gp, таблица monthly_production).</summary>
    public sealed class ClickHouseAnalyticsService : IMonthlyProductionReader
    {
        /// <summary>Колонки помесячного агрегата — один список для всех запросов.</summary>
        private const string MonthlyColumns =
            "well_id, year, month, oil_vol_sum, gas_vol_sum, water_vol_sum, " +
            "water_injection_vol_sum, on_stream_hrs_sum, days_count";

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
            QueryMonthlyAsync(MonthlyByWellSql(wellId), cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<MonthlyProduction>> GetAllMonthlyAsync(CancellationToken cancellationToken = default) =>
            QueryMonthlyAsync(
                $"SELECT {MonthlyColumns} FROM {ClickHouseSchema.MonthlyProductionTable} ORDER BY year, month, well_id",
                cancellationToken);

        /// <inheritdoc />
        public async Task<ProductionSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT sum(oil_vol_sum), sum(gas_vol_sum), sum(water_vol_sum), " +
                "sum(water_injection_vol_sum), sum(on_stream_hrs_sum), sum(days_count) " +
                $"FROM {ClickHouseSchema.MonthlyProductionTable}";

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
            CancellationToken cancellationToken)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

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
        /// SQL выборки агрегатов одной скважины. well_id — целое из нашей БД (не пользовательский
        /// ввод), поэтому подставляется литералом: драйвер ClickHouse.Client 7.14 не даёт
        /// именованных параметров в используемом API, а экранирование для int не требуется.
        /// </summary>
        private static string MonthlyByWellSql(int wellId) =>
            $"SELECT {MonthlyColumns} FROM {ClickHouseSchema.MonthlyProductionTable} " +
            $"WHERE well_id = {wellId.ToString(CultureInfo.InvariantCulture)} ORDER BY year, month";

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
