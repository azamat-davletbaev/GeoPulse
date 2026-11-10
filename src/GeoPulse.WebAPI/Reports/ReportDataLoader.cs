using GeoPulse.Core.Repositories;
using GeoPulse.WebAPI.Analytics;
using GeoPulse.WebAPI.Models.Reports;

namespace GeoPulse.WebAPI.Reports
{
    /// <summary>
    /// Загрузка данных отчётов: справочник скважин из PostgreSQL, помесячные агрегаты из ClickHouse.
    /// Все производные показатели (обводнённость, доли, период) считаются здесь один раз
    /// через <see cref="ReportMetrics"/> и дальше только форматируются.
    /// </summary>
    public sealed class ReportDataLoader
    {
        private readonly IWellRepository _wells;
        private readonly IMonthlyProductionReader _analytics;

        /// <summary>Конструктор.</summary>
        public ReportDataLoader(IWellRepository wells, IMonthlyProductionReader analytics)
        {
            _wells = wells;
            _analytics = analytics;
        }

        /// <summary>Данные отчёта по месторождению: сводка, период, число скважин и помесячные/поскважинные строки.</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task<MonthlyReportData> LoadAsync(CancellationToken cancellationToken)
        {
            var wells = await _wells.GetAllAsync(cancellationToken);
            var monthly = await _analytics.GetAllMonthlyAsync(cancellationToken);
            var summary = await _analytics.GetSummaryAsync(cancellationToken);

            var byWell = ReportMetrics.SumByWell(monthly);

            var wellRows = wells
                .OrderBy(well => well.NpdWellBoreCode)
                .Select(well =>
                {
                    byWell.TryGetValue(well.Id, out var aggregate);
                    return new WellReportRow(
                        well.Name,
                        well.NpdWellBoreCode,
                        aggregate.Oil,
                        aggregate.Water,
                        ReportMetrics.SharePct(aggregate.Oil, summary.TotalOilVol));
                })
                .ToList();

            return new MonthlyReportData(
                summary,
                ReportMetrics.BuildPeriod(monthly),
                wells.Count,
                ReportMetrics.BuildMonthlyRows(monthly),
                wellRows);
        }

        /// <summary>
        /// Данные отчёта по одной скважине. Адресация — по коду НПД (как и остальные
        /// ресурсы скважины в API). Возвращает null, если скважины нет.
        /// </summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task<WellReportData?> LoadWellAsync(int npdWellBoreCode, CancellationToken cancellationToken)
        {
            var well = await _wells.GetByNpdWellBoreCodeAsync(npdWellBoreCode, cancellationToken);
            if (well is null)
            {
                return null;
            }

            var monthly = await _analytics.GetAllMonthlyAsync(cancellationToken);
            var summary = await _analytics.GetSummaryAsync(cancellationToken);

            // Помесячные строки уникальны в разрезе (well_id, year, month): достаточно
            // отфильтровать и упорядочить, группировка не нужна.
            var rows = monthly
                .Where(item => item.WellId == well.Id)
                .OrderBy(item => item.Year)
                .ThenBy(item => item.Month)
                .ToList();

            var oil = rows.Sum(item => item.OilVolSum);
            var water = rows.Sum(item => item.WaterVolSum);

            var monthlyRows = rows
                .Select(item => new MonthlyReportRow(
                    item.Year,
                    item.Month,
                    item.OilVolSum,
                    item.WaterVolSum,
                    ReportMetrics.WaterCutPct(item.OilVolSum, item.WaterVolSum)))
                .ToList();

            return new WellReportData(
                well.Name,
                well.Field,
                oil,
                water,
                ReportMetrics.BuildPeriod(rows),
                ReportMetrics.SharePct(oil, summary.TotalOilVol),
                monthlyRows);
        }
    }
}
