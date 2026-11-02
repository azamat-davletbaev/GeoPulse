using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.Infrastructure.Repositories
{
    /// <summary>Репозиторий помесячных агрегатов добычи (EF Core, схема analytics).</summary>
    public sealed class AnalyticsRepository : IAnalyticsRepository
    {
        private readonly CoreDbContext _db;

        /// <summary>Конструктор.</summary>
        public AnalyticsRepository(CoreDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<MonthlyProduction>> GetMonthlyByWellAsync(
            int wellId,
            CancellationToken cancellationToken = default)
        {
            var entities = await _db.MonthlyProduction
                .AsNoTracking()
                .Where(item => item.WellId == wellId)
                .OrderBy(item => item.Year)
                .ThenBy(item => item.Month)
                .ToListAsync(cancellationToken);

            return entities.Select(ToModel).ToList();
        }

        /// <inheritdoc />
        public async Task<ProductionSummary?> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            // Один SQL-запрос: SUM по всем строкам аналитики.
            var summary = await _db.MonthlyProduction
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    TotalOilVol = group.Sum(item => item.OilVolSum),
                    TotalGasVol = group.Sum(item => item.GasVolSum),
                    TotalWaterVol = group.Sum(item => item.WaterVolSum),
                    TotalWaterInjectionVol = group.Sum(item => item.WaterInjectionVolSum),
                    TotalOnStreamHrs = group.Sum(item => item.OnStreamHrsSum),
                    TotalDays = group.Sum(item => item.DaysCount)
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (summary is null)
            {
                return null;
            }

            return new ProductionSummary
            {
                TotalOilVol = summary.TotalOilVol,
                TotalGasVol = summary.TotalGasVol,
                TotalWaterVol = summary.TotalWaterVol,
                TotalWaterInjectionVol = summary.TotalWaterInjectionVol,
                TotalOnStreamHrs = summary.TotalOnStreamHrs,
                TotalDays = summary.TotalDays
            };
        }

        private static MonthlyProduction ToModel(MonthlyProductionEntity entity) => new()
        {
            WellId = entity.WellId,
            Year = entity.Year,
            Month = entity.Month,
            OilVolSum = entity.OilVolSum,
            GasVolSum = entity.GasVolSum,
            WaterVolSum = entity.WaterVolSum,
            WaterInjectionVolSum = entity.WaterInjectionVolSum,
            OnStreamHrsSum = entity.OnStreamHrsSum,
            DaysCount = entity.DaysCount
        };
    }
}
