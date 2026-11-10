using System.Text;

namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты генераторов выгрузок: структура XLSX (листы, заголовки, контрольные суммы при
/// обратном разборе через ClosedXML) и формат CSV (BOM, английские заголовки, запятая).
/// </summary>
public sealed class ReportWritersTests
{
    private static MonthlyReportData MonthlyData() => new(
        new ProductionSummary { TotalOilVol = 300, TotalWaterVol = 150 },
        "2016-01 — 2016-02",
        2,
        new[]
        {
            new MonthlyReportRow(2016, 1, 300, 150, 33.333),
            new MonthlyReportRow(2016, 2, 0, 0, 0)
        },
        new[]
        {
            new WellReportRow("Well A", 5351, 100, 50, 33.333),
            new WellReportRow("Well B", 5352, 200, 100, 66.667)
        });

    private static WellReportData WellData() => new(
        "Well A",
        "Volve",
        100,
        50,
        "2016-01 — 2016-02",
        33.333,
        new[]
        {
            new MonthlyReportRow(2016, 1, 100, 50, 33.333),
            new MonthlyReportRow(2016, 2, 0, 0, 0)
        });

    [Fact]
    public void BuildMonthlyXlsx_HasThreeSheets_WithExpectedHeaders()
    {
        var bytes = new XlsxReportWriter().BuildMonthlyXlsx(MonthlyData());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.Equal(3, workbook.Worksheets.Count);
        Assert.Equal("Сводка", workbook.Worksheet(1).Name);
        Assert.Equal("Помесячно", workbook.Worksheet(2).Name);
        Assert.Equal("По скважинам", workbook.Worksheet(3).Name);

        var monthly = workbook.Worksheet("Помесячно");
        Assert.Equal("Год", monthly.Cell(1, 1).GetString());
        Assert.Equal("Месяц", monthly.Cell(1, 2).GetString());
        Assert.Equal("Нефть, м³", monthly.Cell(1, 3).GetString());
        Assert.Equal("Вода, м³", monthly.Cell(1, 4).GetString());
        Assert.Equal("Обводнённость, %", monthly.Cell(1, 5).GetString());

        var wells = workbook.Worksheet("По скважинам");
        Assert.Equal("Скважина", wells.Cell(1, 1).GetString());
        Assert.Equal("Нефть, м³", wells.Cell(1, 2).GetString());
        Assert.Equal("Вода, м³", wells.Cell(1, 3).GetString());
        Assert.Equal("Доля, %", wells.Cell(1, 4).GetString());
    }

    [Fact]
    public void BuildMonthlyXlsx_SummarySheetMatchesTotals()
    {
        var bytes = new XlsxReportWriter().BuildMonthlyXlsx(MonthlyData());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var summary = workbook.Worksheet("Сводка");

        Assert.Equal(300, summary.Cell(2, 2).GetDouble(), 3);
        Assert.Equal(150, summary.Cell(3, 2).GetDouble(), 3);
        Assert.Equal("2016-01 — 2016-02", summary.Cell(4, 2).GetString());
        Assert.Equal(2, summary.Cell(5, 2).GetDouble(), 3);
    }

    [Fact]
    public void BuildWellXlsx_HasTwoSheets_WithExpectedHeaders()
    {
        var bytes = new XlsxReportWriter().BuildWellXlsx(WellData());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.Equal("Скважина", workbook.Worksheet(1).Name);
        Assert.Equal("Помесячно", workbook.Worksheet(2).Name);

        var well = workbook.Worksheet("Скважина");
        Assert.Equal("Показатель", well.Cell(1, 1).GetString());
        Assert.Equal("Значение", well.Cell(1, 2).GetString());
        Assert.Equal("Скважина", well.Cell(2, 1).GetString());
        Assert.Equal("Well A", well.Cell(2, 2).GetString());
        Assert.Equal("Месторождение", well.Cell(3, 1).GetString());
        Assert.Equal("Суммарная нефть, м³", well.Cell(4, 1).GetString());
        Assert.Equal("Суммарная вода, м³", well.Cell(5, 1).GetString());
        Assert.Equal("Период", well.Cell(6, 1).GetString());
        Assert.Equal("Доля от добычи месторождения, %", well.Cell(7, 1).GetString());
    }

    [Fact]
    public void BuildWellXlsx_OilSums_MatchControlSum()
    {
        var bytes = new XlsxReportWriter().BuildWellXlsx(WellData());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        // Обратный парсинг: сумма помесячной нефти должна совпасть с итогом на листе «Скважина».
        var monthly = workbook.Worksheet("Помесячно");
        var lastRow = monthly.LastRowUsed()!.RowNumber();
        var monthlyOil = 0.0;
        for (var row = 2; row <= lastRow; row++)
        {
            monthlyOil += monthly.Cell(row, 3).GetDouble();
        }

        var well = workbook.Worksheet("Скважина");
        Assert.Equal(100, monthlyOil, 3);
        Assert.Equal(monthlyOil, well.Cell(4, 2).GetDouble(), 3);
        Assert.Equal(50, well.Cell(5, 2).GetDouble(), 3);
        Assert.Equal(33.333, well.Cell(7, 2).GetDouble(), 3);
        Assert.Equal("2016-01 — 2016-02", well.Cell(6, 2).GetString());
    }

    [Fact]
    public void BuildMonthlyCsv_HasBomHeadersAndInvariantNumbers()
    {
        var bytes = new CsvReportWriter().BuildMonthlyCsv(MonthlyData());

        // BOM в начале файла — иначе Excel открывает кириллицу как mojibake.
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Year,Month,OilVol,WaterVol,WaterCutPct", lines[0]);
        Assert.Equal("2016,1,300.00,150.00,33.33", lines[1]);
        Assert.Equal("2016,2,0.00,0.00,0.00", lines[2]);
    }
}
