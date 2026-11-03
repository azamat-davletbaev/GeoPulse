using System.Globalization;
using ClickHouse.Client.ADO;
using GeoPulse.WebAPI.Models;
using Microsoft.Extensions.Configuration;

namespace GeoPulse.WebAPI
{
    /// <summary>Аналитика из ClickHouse (база gp, таблица monthly_production).</summary>
    public sealed class ClickHouseAnalyticsService : IMonthlyProductionReader
    {
        private readonly string _connectionString;

        /// <summary>Конструктор.</summary>
        public ClickHouseAnalyticsService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ClickHouse")
                ?? throw new InvalidOperationException("Не задана ConnectionStrings:ClickHouse в appsettings.json.");
        }

        /// <summary>Помесячные агрегаты одной скважины (по возрастанию года и месяца).</summary>
        public async Task<IReadOnlyList<MonthlyProductionDto>> GetMonthlyByWellAsync(int wellId, CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT year, month, oil_vol_sum, gas_vol_sum, water_vol_sum, water_injection_vol_sum, on_stream_hrs_sum, days_count " +
                "FROM gp.monthly_production WHERE well_id = " + wellId.ToString(CultureInfo.InvariantCulture) +
                " ORDER BY year, month";

            var result = new List<MonthlyProductionDto>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new MonthlyProductionDto
                {
                    Year = Convert.ToInt32(reader.GetValue(0)),
                    Month = Convert.ToInt32(reader.GetValue(1)),
                    OilVolSum = Convert.ToDouble(reader.GetValue(2)),
                    GasVolSum = Convert.ToDouble(reader.GetValue(3)),
                    WaterVolSum = Convert.ToDouble(reader.GetValue(4)),
                    WaterInjectionVolSum = Convert.ToDouble(reader.GetValue(5)),
                    OnStreamHrsSum = Convert.ToDouble(reader.GetValue(6)),
                    DaysCount = Convert.ToInt32(reader.GetValue(7))
                });
            }

            return result;
        }

        /// <summary>Суммарная добыча по всем скважинам за всё время (один SQL-запрос).</summary>
        public async Task<ProductionSummaryDto> GetSummaryAsync(CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT sum(oil_vol_sum), sum(gas_vol_sum), sum(water_vol_sum), sum(water_injection_vol_sum), sum(on_stream_hrs_sum), sum(days_count) " +
                "FROM gp.monthly_production";

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new ProductionSummaryDto
                {
                    TotalOilVol = Convert.ToDouble(reader.GetValue(0)),
                    TotalGasVol = Convert.ToDouble(reader.GetValue(1)),
                    TotalWaterVol = Convert.ToDouble(reader.GetValue(2)),
                    TotalWaterInjectionVol = Convert.ToDouble(reader.GetValue(3)),
                    TotalOnStreamHrs = Convert.ToDouble(reader.GetValue(4)),
                    TotalDays = Convert.ToInt32(reader.GetValue(5))
                };
            }

            return new ProductionSummaryDto();
        }

        /// <summary>Все помесячные агрегаты по всем скважинам, по возрастанию года и месяца (для отчётов).</summary>
        public async Task<IReadOnlyList<MonthlyProductionFlatDto>> GetAllMonthlyAsync(CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT well_id, year, month, oil_vol_sum, water_vol_sum " +
                "FROM gp.monthly_production ORDER BY year, month, well_id";

            var result = new List<MonthlyProductionFlatDto>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new MonthlyProductionFlatDto
                {
                    WellId = Convert.ToInt32(reader.GetValue(0)),
                    Year = Convert.ToInt32(reader.GetValue(1)),
                    Month = Convert.ToInt32(reader.GetValue(2)),
                    OilVolSum = Convert.ToDouble(reader.GetValue(3)),
                    WaterVolSum = Convert.ToDouble(reader.GetValue(4))
                });
            }

            return result;
        }
    }
}
