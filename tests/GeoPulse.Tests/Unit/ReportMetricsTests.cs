namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты расчётных величин отчётов. Формулы вынесены в <see cref="ReportMetrics"/>,
/// поэтому проверяются напрямую — без загрузки данных и генерации файлов.
/// </summary>
public sealed class ReportMetricsTests
{
    [Theory]
    [InlineData(100, 50, 33.3333)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 10, 100)]
    [InlineData(10, 0, 0)]
    [InlineData(-5, 5, 0)]
    public void WaterCutPct_ReturnsShareOfWater(double oil, double water, double expected)
    {
        Assert.Equal(expected, ReportMetrics.WaterCutPct(oil, water), 4);
    }

    [Theory]
    [InlineData(100, 400, 25)]
    [InlineData(0, 400, 0)]
    [InlineData(100, 0, 0)]
    public void SharePct_ReturnsShareOfFieldOil(double oil, double totalOil, double expected)
    {
        Assert.Equal(expected, ReportMetrics.SharePct(oil, totalOil), 4);
    }

    [Fact]
    public void BuildPeriod_UsesFirstAndLastMonth()
    {
        var monthly = new[]
        {
            new MonthlyProduction { Year = 2008, Month = 9 },
            new MonthlyProduction { Year = 2016, Month = 3 }
        };

        Assert.Equal("2008-09 — 2016-03", ReportMetrics.BuildPeriod(monthly));
    }

    [Fact]
    public void BuildPeriod_EmptyData_ReturnsDash()
    {
        Assert.Equal("—", ReportMetrics.BuildPeriod(Array.Empty<MonthlyProduction>()));
    }

    [Fact]
    public void BuildMonthlyRows_SumsAllWellsPerMonth()
    {
        var monthly = new[]
        {
            new MonthlyProduction { WellId = 2, Year = 2016, Month = 1, OilVolSum = 200, WaterVolSum = 100 },
            new MonthlyProduction { WellId = 1, Year = 2016, Month = 1, OilVolSum = 100, WaterVolSum = 50 },
            new MonthlyProduction { WellId = 1, Year = 2015, Month = 12, OilVolSum = 10, WaterVolSum = 10 }
        };

        var rows = ReportMetrics.BuildMonthlyRows(monthly);

        // Порядок — по возрастанию года и месяца.
        Assert.Equal(2, rows.Count);
        Assert.Equal((2015, 12), (rows[0].Year, rows[0].Month));
        Assert.Equal((2016, 1), (rows[1].Year, rows[1].Month));

        Assert.Equal(300, rows[1].OilVol, 3);
        Assert.Equal(150, rows[1].WaterVol, 3);
        Assert.Equal(33.333, rows[1].WaterCutPct, 3);
        Assert.Equal(50, rows[0].WaterCutPct, 3);
    }

    [Fact]
    public void SumByWell_SumsOilAndWaterPerWell()
    {
        var monthly = new[]
        {
            new MonthlyProduction { WellId = 1, Year = 2016, Month = 1, OilVolSum = 100, WaterVolSum = 50 },
            new MonthlyProduction { WellId = 1, Year = 2016, Month = 2, OilVolSum = 20, WaterVolSum = 5 },
            new MonthlyProduction { WellId = 2, Year = 2016, Month = 1, OilVolSum = 7, WaterVolSum = 3 }
        };

        var sums = ReportMetrics.SumByWell(monthly);

        Assert.Equal(120, sums[1].Oil, 3);
        Assert.Equal(55, sums[1].Water, 3);
        Assert.Equal(7, sums[2].Oil, 3);
        Assert.Equal(3, sums[2].Water, 3);
    }
}
