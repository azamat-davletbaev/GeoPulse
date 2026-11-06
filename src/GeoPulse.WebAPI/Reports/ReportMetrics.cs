using GeoPulse.Core.Models;
using GeoPulse.WebAPI.Models.Reports;

namespace GeoPulse.WebAPI.Reports
{
    /// <summary>
    /// Расчётные величины отчётов: обводнённость, доля в добыче месторождения, период и
    /// свёртки помесячных агрегатов. Вынесено из сервиса загрузки, чтобы формулы можно было
    /// проверять юнит-тестами и переиспользовать в XLSX и CSV без дублирования.
    /// </summary>
    public static class ReportMetrics
    {
        /// <summary>Обводнённость, %: вода / (нефть + вода) × 100; при сумме ≤ 0 — 0.</summary>
        public static double WaterCutPct(double oil, double water)
        {
            var total = oil + water;
            return total <= 0 ? 0 : water / total * 100;
        }

        /// <summary>Доля скважины в добыче месторождения, %: нефть / всего нефти × 100; при нуле — 0.</summary>
        public static double SharePct(double oil, double totalOil) => totalOil <= 0 ? 0 : oil / totalOil * 100;

        /// <summary>Период в формате «YYYY-MM — YYYY-MM» по первому и последнему месяцу; «—» при пустых данных.</summary>
        /// <param name="monthly">Помесячные агрегаты в порядке возрастания даты.</param>
        public static string BuildPeriod(IReadOnlyList<MonthlyProduction> monthly)
        {
            if (monthly.Count == 0)
            {
                return "—";
            }

            var first = monthly[0];
            var last = monthly[^1];
            return $"{first.Year}-{first.Month:00} — {last.Year}-{last.Month:00}";
        }

        /// <summary>Строки листа «Помесячно»: агрегат по всем скважинам за каждый месяц.</summary>
        /// <param name="monthly">Помесячные агрегаты по всем скважинам.</param>
        public static IReadOnlyList<MonthlyReportRow> BuildMonthlyRows(IEnumerable<MonthlyProduction> monthly) =>
            monthly
                .GroupBy(item => (item.Year, item.Month))
                .OrderBy(group => group.Key.Year)
                .ThenBy(group => group.Key.Month)
                .Select(group =>
                {
                    var oil = group.Sum(item => item.OilVolSum);
                    var water = group.Sum(item => item.WaterVolSum);
                    return new MonthlyReportRow(group.Key.Year, group.Key.Month, oil, water, WaterCutPct(oil, water));
                })
                .ToList();

        /// <summary>Суммы нефти и воды по каждой скважине — знаменатели для доли в добыче.</summary>
        /// <param name="monthly">Помесячные агрегаты по всем скважинам.</param>
        public static IReadOnlyDictionary<int, (double Oil, double Water)> SumByWell(
            IEnumerable<MonthlyProduction> monthly) =>
            monthly
                .GroupBy(item => item.WellId)
                .ToDictionary(
                    group => group.Key,
                    group => (Oil: group.Sum(item => item.OilVolSum), Water: group.Sum(item => item.WaterVolSum)));
    }
}
