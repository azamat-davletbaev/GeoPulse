namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты слоя загрузки отчётов (<see cref="ReportDataLoader"/>): производные показатели
/// (обводнённость, доля, период) считаются один раз при загрузке, а не в генераторах форматов.
/// Источники данных подменены in-memory стабами: PostgreSQL и ClickHouse не нужны.
/// </summary>
public sealed class ReportDataLoaderTests
{
    private static readonly Well[] SampleWells =
    {
        new Well { Id = 1, NpdWellBoreCode = 5351, Name = "Well A", Field = "Volve", Type = "OP" },
        new Well { Id = 2, NpdWellBoreCode = 5352, Name = "Well B", Field = "Volve", Type = "OP" },
    };

    private static readonly ProductionSummary SampleSummary = new()
    {
        TotalOilVol = 300,
        TotalWaterVol = 150,
    };

    private static IReadOnlyList<MonthlyProduction> SampleMonthly() => new[]
    {
        new MonthlyProduction { WellId = 1, Year = 2016, Month = 1, OilVolSum = 100, WaterVolSum = 50 },
        new MonthlyProduction { WellId = 2, Year = 2016, Month = 1, OilVolSum = 200, WaterVolSum = 100 },
        new MonthlyProduction { WellId = 1, Year = 2016, Month = 2, OilVolSum = 0, WaterVolSum = 0 },
    };

    private static ReportDataLoader CreateLoader() =>
        new(new FakeWellRepository(SampleWells), new FakeMonthlyProductionReader(SampleMonthly(), SampleSummary));

    [Fact]
    public async Task LoadAsync_ComputesWaterCutAndShareOnce()
    {
        var data = await CreateLoader().LoadAsync(CancellationToken.None);

        Assert.Equal(2, data.WellCount);
        Assert.Equal("2016-01 — 2016-02", data.Period);

        // Два месяца: январь (по обеим скважинам) и февраль (только Well A, нули).
        Assert.Equal(2, data.Monthly.Count);
        var january = data.Monthly.First();
        Assert.Equal(300, january.OilVol, 3);
        Assert.Equal(150, january.WaterVol, 3);
        Assert.Equal(33.333, january.WaterCutPct, 3);

        var wellA = data.Wells.First(well => well.NpdWellBoreCode == 5351);
        Assert.Equal(100, wellA.OilVol, 3);
        Assert.Equal(50, wellA.WaterVol, 3);
        Assert.Equal(33.333, wellA.SharePct, 3);
    }

    [Fact]
    public async Task LoadWellAsync_ComputesTotalsWaterCutPeriodAndShare()
    {
        var data = await CreateLoader().LoadWellAsync(5351, CancellationToken.None);

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
    public async Task LoadWellAsync_UnknownCode_ReturnsNull()
    {
        var data = await CreateLoader().LoadWellAsync(999999, CancellationToken.None);

        Assert.Null(data);
    }
}
