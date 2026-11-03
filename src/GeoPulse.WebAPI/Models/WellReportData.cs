namespace GeoPulse.WebAPI.Models
{
    /// <summary>
    /// Подготовленные данные отчёта по одной скважине (готовы к выгрузке): справочные поля,
    /// суммарные нефть/вода за всё время, период работы, доля в добыче месторождения
    /// и помесячная детализация. Обводнённость считается здесь же — как в отчёте
    /// по месторождению, на фронте она не дублируется.
    /// </summary>
    public sealed record WellReportData(
        string Name,
        string Field,
        double TotalOilVol,
        double TotalWaterVol,
        string Period,
        double SharePct,
        IReadOnlyList<MonthlyReportRow> Monthly);
}
