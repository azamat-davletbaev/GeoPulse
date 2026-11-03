namespace GeoPulse.WebAPI.Models
{
    /// <summary>Подготовленные данные помесячного отчёта по месторождению (готовы к выгрузке).</summary>
    public sealed record MonthlyReportData(
        ProductionSummaryDto Summary,
        string Period,
        int WellCount,
        IReadOnlyList<MonthlyReportRow> Monthly,
        IReadOnlyList<WellReportRow> Wells);

    /// <summary>Помесячная строка отчёта (агрегат по всем скважинам за месяц).</summary>
    public sealed record MonthlyReportRow(int Year, int Month, double OilVol, double WaterVol, double WaterCutPct);

    /// <summary>Строка отчёта по скважине.</summary>
    public sealed record WellReportRow(string Name, int NpdWellBoreCode, double OilVol, double WaterVol, double SharePct);
}
