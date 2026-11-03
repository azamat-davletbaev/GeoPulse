using System.Text.Json;

namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты сериализации событий (GeoPulse.Core) через общий конфиг MessagingJson.
/// Проверяют, что JSON-представление production.daily, которое читают Consumer/CHIngest,
/// соответствует модели и camelCase-контракту.
/// </summary>
public sealed class EventJsonSerializationTests
{
    [Fact]
    public void ProductionDailyEvent_RoundTrips_AllFieldsPreserved()
    {
        var original = new DailyProductionEvent
        {
            Data = new DailyProductionData
            {
                NpdWellBoreCode = 5351,
                Date = new DateOnly(2016, 1, 2),
                OnStreamHrs = 24,
                AvgDownholePressure = 250.5,
                AvgDownholeTemperature = 90.1,
                AvgDpTubing = 20.2,
                AvgAnnulusPressure = 30.3,
                AvgChokeSize = 40.4,
                AvgWhp = 50.5,
                AvgWht = 60.6,
                DpChokeSize = 70.7,
                OilVol = 100.8,
                GasVol = 200.9,
                WaterVol = 300.1,
                WaterInjectionVol = 400.2
            }
        };

        var json = JsonSerializer.Serialize(original, MessagingJson.Options);
        var restored = JsonSerializer.Deserialize<DailyProductionEvent>(json, MessagingJson.Options)!;

        Assert.Equal(EventTypes.ProductionDaily, restored.EventType);
        Assert.Equal(EventTypes.SchemaVersion, restored.SchemaVersion);

        var data = restored.Data;
        Assert.Equal(5351, data.NpdWellBoreCode);
        Assert.Equal(new DateOnly(2016, 1, 2), data.Date);
        Assert.Equal(24, data.OnStreamHrs);
        Assert.Equal(250.5, data.AvgDownholePressure);
        Assert.Equal(90.1, data.AvgDownholeTemperature);
        Assert.Equal(20.2, data.AvgDpTubing);
        Assert.Equal(30.3, data.AvgAnnulusPressure);
        Assert.Equal(40.4, data.AvgChokeSize);
        Assert.Equal(50.5, data.AvgWhp);
        Assert.Equal(60.6, data.AvgWht);
        Assert.Equal(70.7, data.DpChokeSize);
        Assert.Equal(100.8, data.OilVol);
        Assert.Equal(200.9, data.GasVol);
        Assert.Equal(300.1, data.WaterVol);
        Assert.Equal(400.2, data.WaterInjectionVol);
    }

    [Fact]
    public void ProductionDailyEvent_Serializes_CamelCaseAndIsoDate()
    {
        var evt = new DailyProductionEvent
        {
            Data = new DailyProductionData { NpdWellBoreCode = 5351, Date = new DateOnly(2016, 1, 1) }
        };

        var json = JsonSerializer.Serialize(evt, MessagingJson.Options);

        Assert.Contains("\"eventType\":\"production.daily\"", json);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"npdWellBoreCode\":5351", json);
        Assert.Contains("\"date\":\"2016-01-01\"", json);
        Assert.DoesNotContain("NpdWellBoreCode", json);
    }

    [Fact]
    public void ProductionDailyData_BoundaryValues_NullZeroNegativeRoundTrip()
    {
        var evt = new DailyProductionEvent
        {
            Data = new DailyProductionData
            {
                NpdWellBoreCode = 5351,
                Date = new DateOnly(2016, 1, 1),
                OilVol = 0,
                GasVol = -12.5,
                WaterVol = null,
                WaterInjectionVol = null,
                OnStreamHrs = 0
            }
        };

        var json = JsonSerializer.Serialize(evt, MessagingJson.Options);
        var restored = JsonSerializer.Deserialize<DailyProductionEvent>(json, MessagingJson.Options)!;

        Assert.Equal(0, restored.Data.OilVol);
        Assert.Equal(-12.5, restored.Data.GasVol);
        Assert.Null(restored.Data.WaterVol);
        Assert.Null(restored.Data.WaterInjectionVol);
        Assert.Equal(0, restored.Data.OnStreamHrs);
    }

    [Fact]
    public void WellRegisteredEvent_RoundTrips()
    {
        var original = new WellRegisteredEvent
        {
            Data = new WellRegisteredData { NpdWellBoreCode = 5351, Name = "15/9-F-1", Field = "Volve", Type = "OP" }
        };

        var json = JsonSerializer.Serialize(original, MessagingJson.Options);
        var restored = JsonSerializer.Deserialize<WellRegisteredEvent>(json, MessagingJson.Options)!;

        Assert.Equal(EventTypes.WellRegistered, restored.EventType);
        Assert.Equal(EventTypes.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(5351, restored.Data.NpdWellBoreCode);
        Assert.Equal("15/9-F-1", restored.Data.Name);
        Assert.Equal("Volve", restored.Data.Field);
        Assert.Equal("OP", restored.Data.Type);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitive()
    {
        const string pascalCase = "{\"EventType\":\"production.daily\",\"SchemaVersion\":1,\"Data\":{\"NpdWellBoreCode\":5351,\"Date\":\"2016-01-01\",\"OilVol\":1.5}}";

        var evt = JsonSerializer.Deserialize<DailyProductionEvent>(pascalCase, MessagingJson.Options)!;

        Assert.Equal(EventTypes.ProductionDaily, evt.EventType);
        Assert.Equal(5351, evt.Data.NpdWellBoreCode);
        Assert.Equal(1.5, evt.Data.OilVol);
    }
}
