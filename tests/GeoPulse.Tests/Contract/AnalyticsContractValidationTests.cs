using System.Net;
using System.Text.Json;

namespace GeoPulse.Tests.Contract;

/// <summary>
/// Контрактные тесты /api/Analytics/monthly (валидация 400/404) через реальный HTTP-пайплайн
/// и чистые DTO-контракты (поля не изменились). Читатель аналитики в путях валидации
/// не вызывается, поэтому достаточно пустой подмены IMonthlyProductionReader.
/// </summary>
public sealed class AnalyticsContractValidationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Monthly_WithoutWellIdOrCode_Returns400()
    {
        var (app, client) = await ApiTestHost.StartAsync(services =>
        {
            services.AddSingleton<IWellRepository>(new FakeWellRepository());
            services.AddSingleton<IMonthlyProductionReader>(new FakeMonthlyProductionReader());
        });

        try
        {
            var response = await client.GetAsync("/api/Analytics/monthly");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Monthly_WithUnknownWellId_Returns404()
    {
        var (app, client) = await ApiTestHost.StartAsync(services =>
        {
            services.AddSingleton<IWellRepository>(new FakeWellRepository());
            services.AddSingleton<IMonthlyProductionReader>(new FakeMonthlyProductionReader());
        });

        try
        {
            var response = await client.GetAsync("/api/Analytics/monthly?wellId=9999");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void ProductionSummaryDto_ContractFieldsUnchanged()
    {
        var dto = new ProductionSummaryDto
        {
            TotalOilVol = 1.5,
            TotalGasVol = 2.5,
            TotalWaterVol = 3.5,
            TotalWaterInjectionVol = 4.5,
            TotalOnStreamHrs = 5.5,
            TotalDays = 6
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, WebJson));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[]
            {
                "totalDays",
                "totalGasVol",
                "totalOilVol",
                "totalOnStreamHrs",
                "totalWaterInjectionVol",
                "totalWaterVol"
            },
            names);
    }

    [Fact]
    public void MonthlyProductionDto_ContractFieldsUnchanged()
    {
        var dto = new MonthlyProductionDto
        {
            Year = 2016,
            Month = 1,
            OilVolSum = 1.5,
            GasVolSum = 2.5,
            WaterVolSum = 3.5,
            WaterInjectionVolSum = 4.5,
            OnStreamHrsSum = 5.5,
            DaysCount = 6
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, WebJson));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[]
            {
                "daysCount",
                "gasVolSum",
                "month",
                "oilVolSum",
                "onStreamHrsSum",
                "waterInjectionVolSum",
                "waterVolSum",
                "year"
            },
            names);
    }
}
