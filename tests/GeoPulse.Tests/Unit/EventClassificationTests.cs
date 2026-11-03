using System.Text.Json;

namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты классификации входящих сообщений (private static Classify в Consumer и CHIngest).
/// Проверяют, что битый JSON, неизвестный eventType и несовпадающий schemaVersion превращаются
/// в маршрут отбраковки (kind=Invalid + reason), а не исключение наверх.
/// </summary>
public sealed class EventClassificationTests
{
    public static IEnumerable<object[]> ServiceTypes => new[]
    {
        new object[] { typeof(ProductionConsumerService) },
        new object[] { typeof(CHIngestService) },
    };

    private static object Classify(Type serviceType, string? raw) =>
        ReflectionHelper.InvokePrivateStatic(serviceType, "Classify", raw!);

    private static string Kind(object result) =>
        (string)ReflectionHelper.GetProperty(result, "Kind")!.ToString()!;

    private static string? Error(object result) =>
        (string?)ReflectionHelper.GetProperty(result, "Error");

    private static string DailyJson(int npdWellBoreCode = 5351, double? oilVol = 100.5) =>
        JsonSerializer.Serialize(
            new DailyProductionEvent
            {
                Data = new DailyProductionData
                {
                    NpdWellBoreCode = npdWellBoreCode,
                    Date = new DateOnly(2016, 1, 1),
                    OilVol = oilVol
                }
            },
            MessagingJson.Options);

    private static string WellJson(int npdWellBoreCode = 5351) =>
        JsonSerializer.Serialize(
            new WellRegisteredEvent
            {
                Data = new WellRegisteredData { NpdWellBoreCode = npdWellBoreCode, Name = "W", Field = "F", Type = "OP" }
            },
            MessagingJson.Options);

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_NullOrWhitespace_ReturnsEmptyMessage(Type serviceType)
    {
        Assert.Equal("empty.message", Error(Classify(serviceType, null)));
        Assert.Equal("empty.message", Error(Classify(serviceType, string.Empty)));
        Assert.Equal("empty.message", Error(Classify(serviceType, "   ")));
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_BrokenJson_ReturnsUnparseableJson(Type serviceType)
    {
        var result = Classify(serviceType, "{not-valid-json");

        Assert.Equal("Invalid", Kind(result));
        Assert.Equal("unparseable.json", Error(result));
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_UnknownSchemaVersion_ReturnsUnknownSchemaVersion(Type serviceType)
    {
        var result = Classify(serviceType, "{\"eventType\":\"production.daily\",\"schemaVersion\":2}");

        Assert.Equal("Invalid", Kind(result));
        Assert.Equal("unknown.schemaVersion", Error(result));
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_UnknownEventType_ReturnsUnknownEventType(Type serviceType)
    {
        var result = Classify(serviceType, "{\"eventType\":\"foo.bar\",\"schemaVersion\":1}");

        Assert.Equal("Invalid", Kind(result));
        Assert.Equal("unknown.eventType:foo.bar", Error(result));
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_ExplicitNullData_ReturnsMissingData(Type serviceType)
    {
        var result = Classify(serviceType, "{\"eventType\":\"production.daily\",\"schemaVersion\":1,\"data\":null}");

        Assert.Equal("Invalid", Kind(result));
        Assert.Equal("missing.data", Error(result));
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_OmittedData_YieldsDailyWithEmptyData(Type serviceType)
    {
        // Фактическое поведение: свойство Data имеет инициализатор = new(), поэтому отсутствие
        // поля data даёт пустой объект (NpdWellBoreCode=0), а не null. Такой «пустой» daily
        // далее уходит в DLQ как unknown well (неизвестный код 0), а не как missing.data.
        var result = Classify(serviceType, "{\"eventType\":\"production.daily\",\"schemaVersion\":1}");

        Assert.Equal("Daily", Kind(result));
        Assert.Null(Error(result));

        var daily = (DailyProductionData)ReflectionHelper.GetProperty(result, "Daily")!;
        Assert.Equal(0, daily.NpdWellBoreCode);
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_ValidDaily_ReturnsDailyKindWithData(Type serviceType)
    {
        var result = Classify(serviceType, DailyJson(npdWellBoreCode: 5351, oilVol: 100.5));

        Assert.Equal("Daily", Kind(result));
        Assert.Null(Error(result));

        var daily = (DailyProductionData)ReflectionHelper.GetProperty(result, "Daily")!;
        Assert.Equal(5351, daily.NpdWellBoreCode);
        Assert.Equal(new DateOnly(2016, 1, 1), daily.Date);
        Assert.Equal(100.5, daily.OilVol);
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_ValidWell_ReturnsWellKindWithData(Type serviceType)
    {
        var result = Classify(serviceType, WellJson(5351));

        Assert.Equal("Well", Kind(result));
        Assert.Null(Error(result));

        var well = (WellRegisteredData)ReflectionHelper.GetProperty(result, "Well")!;
        Assert.Equal(5351, well.NpdWellBoreCode);
        Assert.Equal("W", well.Name);
    }

    [Theory]
    [MemberData(nameof(ServiceTypes))]
    public void Classify_DailyWithWrongDataShape_ReturnsUnparseableDailyEvent(Type serviceType)
    {
        var result = Classify(serviceType, "{\"eventType\":\"production.daily\",\"schemaVersion\":1,\"data\":123}");

        Assert.Equal("Invalid", Kind(result));
        Assert.Equal("unparseable.daily.event", Error(result));
    }
}
