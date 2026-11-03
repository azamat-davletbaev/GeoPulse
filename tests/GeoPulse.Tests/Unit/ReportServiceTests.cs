using System.Text;

namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты ReportService на структуру отчёта: число листов, заголовки колонок,
/// контрольная сумма суммарной нефти (xlsx распарсивается обратно тем же ClosedXML),
/// а также формат CSV (BOM, английские заголовки, разделитель-запятая).
/// </summary>
public sealed class ReportServiceTests
{
    private static readonly Well[] SampleWells =
    {
        new Well { Id = 1, NpdWellBoreCode = 5351, Name = "Well A", Field = "Volve", Type = "OP" },
        new Well { Id = 2, NpdWellBoreCode = 5352, Name = "Well B", Field = "Volve", Type = "OP" },
    };

    private static readonly ProductionSummaryDto SampleSummary = new()
    {
        TotalOilVol = 300,
        TotalWaterVol = 150,
    };

    private static IReadOnlyList<MonthlyProductionFlatDto> SampleMonthly() => new[]
    {
        new MonthlyProductionFlatDto { WellId = 1, Year = 2016, Month = 1, OilVolSum = 100, WaterVolSum = 50 },
        new MonthlyProductionFlatDto { WellId = 2, Year = 2016, Month = 1, OilVolSum = 200, WaterVolSum = 100 },
        new MonthlyProductionFlatDto { WellId = 1, Year = 2016, Month = 2, OilVolSum = 0, WaterVolSum = 0 },
    };

    private static ReportService CreateService() =>
        new(new FakeWellRepository(SampleWells), new FakeMonthlyProductionReader(SampleMonthly(), SampleSummary));

    [Fact]
    public async Task LoadAsync_ComputesWaterCutAndShareOnce()
    {
        var data = await CreateService().LoadAsync(CancellationToken.None);

        Assert.Equal(2, data.WellCount);
        Assert.Equal("2016-01 — 2016-02", data.Period);

        // Два месяца: январь (по обеим скважинам) и февраль (только Well A, нули).
        Assert.Equal(2, data.Monthly.Count);
        var january = data.Monthly.First();
        Assert.Equal(300, january.OilVol);
        Assert.Equal(150, january.WaterVol);
        Assert.Equal(33.333, january.WaterCutPct, 3);

        var wellA = data.Wells.First(well => well.NpdWellBoreCode == 5351);
        Assert.Equal(100, wellA.OilVol);
        Assert.Equal(50, wellA.WaterVol);
        Assert.Equal(33.333, wellA.SharePct, 3);
    }

    [Fact]
    public async Task BuildXlsx_HasThreeSheets_WithExpectedHeaders()
    {
        var data = await CreateService().LoadAsync(CancellationToken.None);
        var bytes = CreateService().BuildXlsx(data);

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
    public async Task BuildXlsx_WellSheetOilSum_MatchesControlSum()
    {
        var data = await CreateService().LoadAsync(CancellationToken.None);
        var bytes = CreateService().BuildXlsx(data);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheet("По скважинам");
        var lastRow = sheet.LastRowUsed()!.RowNumber();
        var sum = 0.0;
        for (var row = 2; row <= lastRow; row++)
        {
            sum += sheet.Cell(row, 2).GetDouble();
        }

        Assert.Equal(300, sum, 2);
    }

    [Fact]
    public async Task BuildCsv_HasBom_EnglishHeaders_AndCommaSeparatedRows()
    {
        var data = await CreateService().LoadAsync(CancellationToken.None);
        var bytes = CreateService().BuildCsv(data);

        // BOM (EF BB BF) в начале файла.
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Year,Month,OilVol,WaterVol,WaterCutPct", lines[0].TrimEnd('\r'));
        Assert.StartsWith("2016,1,300.00,150.00,", lines[1].TrimEnd('\r'));
        Assert.StartsWith("2016,2,0.00,0.00,", lines[2].TrimEnd('\r'));
    }

    [Fact]
    public async Task LoadWellAsync_UnknownWell_ReturnsNull()
    {
        var data = await CreateService().LoadWellAsync(999, CancellationToken.None);

        Assert.Null(data);
    }

    [Fact]
    public async Task LoadWellAsync_ComputesTotalsWaterCutPeriodAndShare()
    {
        var data = await CreateService().LoadWellAsync(1, CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("Well A", data.Name);
        Assert.Equal("Volve", data.Field);

        // Только строки скважины 1: январь (100/50) и февраль (0/0).
        Assert.Equal(2, data.Monthly.Count);
        Assert.Equal(100, data.TotalOilVol, 3);
        Assert.Equal(50, data.TotalWaterVol, 3);
        Assert.Equal("2016-01 — 2016-02", data.Period);

        // Обводнённость и доля — те же формулы, что и в отчёте по месторождению.
        Assert.Equal(33.333, data.Monthly[0].WaterCutPct, 3);
        Assert.Equal(0, data.Monthly[1].WaterCutPct, 3);
        Assert.Equal(33.333, data.SharePct, 3);
    }

    [Fact]
    public async Task BuildWellXlsx_HasTwoSheets_WithExpectedHeaders()
    {
        var data = await CreateService().LoadWellAsync(1, CancellationToken.None);
        var bytes = CreateService().BuildWellXlsx(data!);

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

        var monthly = workbook.Worksheet("Помесячно");
        Assert.Equal("Год", monthly.Cell(1, 1).GetString());
        Assert.Equal("Месяц", monthly.Cell(1, 2).GetString());
        Assert.Equal("Нефть, м³", monthly.Cell(1, 3).GetString());
        Assert.Equal("Вода, м³", monthly.Cell(1, 4).GetString());
        Assert.Equal("Обводнённость, %", monthly.Cell(1, 5).GetString());
    }

    [Fact]
    public async Task BuildWellXlsx_OilSums_MatchControlSum()
    {
        var data = await CreateService().LoadWellAsync(1, CancellationToken.None);
        var bytes = CreateService().BuildWellXlsx(data!);

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
}
