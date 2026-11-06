using System.Text.Json;

namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты общего разборщика сообщений <see cref="EventClassifier"/> (используется и Consumer,
/// и CHIngest — до выноса в GeoPulse.Messaging логика дублировалась в обоих сервисах).
/// Проверяют, что битый JSON, неизвестный eventType и несовпадающий schemaVersion превращаются
/// в маршрут отбраковки (Kind=Invalid + reason), а не в исключение наверх.
/// </summary>
public sealed class EventClassificationTests
{
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

    private static string TrajectoryJson(int npdWellBoreCode = 5351) =>
        JsonSerializer.Serialize(
            new TrajectoryEvent
            {
                Data = new TrajectoryData
                {
                    NpdWellBoreCode = npdWellBoreCode,
                    TdMd = 3510.00,
                    TdTvd = 3138.06,
                    Points = new[]
                    {
                        new TrajectoryPointData
                        {
                            Md = 145.90,
                            Tvd = 145.90,
                            Inclination = 0.0,
                            Azimuth = 0.0,
                            Easting = 435049.831,
                            Northing = 6478560.825
                        }
                    }
                }
            },
            MessagingJson.Options);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_EmptyOrWhitespace_ReturnsEmptyMessage(string? raw)
    {
        var result = EventClassifier.Classify(raw);

        Assert.Equal(MessageKind.Invalid, result.Kind);
        Assert.Equal(DlqReasons.EmptyMessage, result.Error);
    }

    [Fact]
    public void Classify_BrokenJson_ReturnsUnparseableJson()
    {
        var result = EventClassifier.Classify("{not-valid-json");

        Assert.Equal(MessageKind.Invalid, result.Kind);
        Assert.Equal(DlqReasons.Malformed, result.Error);
    }

    [Fact]
    public void Classify_UnknownSchemaVersion_ReturnsReason()
    {
        var result = EventClassifier.Classify("{\"eventType\":\"production.daily\",\"schemaVersion\":2}");

        Assert.Equal(MessageKind.Invalid, result.Kind);
        Assert.Equal(DlqReasons.UnknownSchemaVersion, result.Error);
    }

    [Fact]
    public void Classify_UnknownEventType_ReturnsUnknownEventType()
    {
        var result = EventClassifier.Classify("{\"eventType\":\"foo.bar\",\"schemaVersion\":1}");

        Assert.Equal(MessageKind.Invalid, result.Kind);
        Assert.Equal(DlqReasons.UnknownEventTypeFor("foo.bar"), result.Error);
    }

    [Fact]
    public void Classify_ExplicitNullData_ReturnsMissingData()
    {
        var result = EventClassifier.Classify("{\"eventType\":\"production.daily\",\"schemaVersion\":1,\"data\":null}");

        Assert.Equal(MessageKind.Invalid, result.Kind);
        Assert.Equal(DlqReasons.MissingData, result.Error);
    }

    [Fact]
    public void Classify_OmittedData_YieldsDailyWithEmptyData()
    {
        // Фактическое поведение: свойство Data имеет инициализатор = new(), поэтому отсутствие
        // поля data даёт пустой объект (NpdWellBoreCode=0), а не null. Такой «пустой» daily
        // далее уходит в DLQ как unknown well (неизвестный код 0), а не как missing.data.
        var result = EventClassifier.Classify("{\"eventType\":\"production.daily\",\"schemaVersion\":1}");

        Assert.Equal(MessageKind.Daily, result.Kind);
        Assert.Null(result.Error);
        Assert.NotNull(result.Daily);
        Assert.Equal(0, result.Daily!.NpdWellBoreCode);
    }

    [Fact]
    public void Classify_ValidDaily_ReturnsDailyKindWithData()
    {
        var result = EventClassifier.Classify(DailyJson(npdWellBoreCode: 5351, oilVol: 100.5));

        Assert.Equal(MessageKind.Daily, result.Kind);
        Assert.Null(result.Error);
        Assert.NotNull(result.Daily);
        Assert.Equal(5351, result.Daily!.NpdWellBoreCode);
        Assert.Equal(new DateOnly(2016, 1, 1), result.Daily.Date);
        Assert.Equal(100.5, result.Daily.OilVol);
    }

    [Fact]
    public void Classify_ValidWell_ReturnsWellKindWithData()
    {
        var result = EventClassifier.Classify(WellJson(5351));

        Assert.Equal(MessageKind.Well, result.Kind);
        Assert.Null(result.Error);
        Assert.NotNull(result.Well);
        Assert.Equal(5351, result.Well!.NpdWellBoreCode);
        Assert.Equal("W", result.Well.Name);
    }

    [Fact]
    public void Classify_ValidTrajectory_ReturnsTrajectoryKindWithData()
    {
        var result = EventClassifier.Classify(TrajectoryJson(5351));

        Assert.Equal(MessageKind.Trajectory, result.Kind);
        Assert.Null(result.Error);
        Assert.NotNull(result.Trajectory);
        Assert.Equal(5351, result.Trajectory!.NpdWellBoreCode);
        Assert.Equal(3510.00, result.Trajectory.TdMd);
        Assert.Equal(3138.06, result.Trajectory.TdTvd);
        Assert.Single(result.Trajectory.Points);
    }

    [Fact]
    public void Classify_DailyWithWrongDataShape_ReturnsUnparseableDailyEvent()
    {
        var result = EventClassifier.Classify("{\"eventType\":\"production.daily\",\"schemaVersion\":1,\"data\":123}");

        Assert.Equal(MessageKind.Invalid, result.Kind);
        Assert.Equal(DlqReasons.UnparseableDailyEvent, result.Error);
    }

    [Fact]
    public void EventTypeOf_ReturnsEventTypeFromEnvelope()
    {
        Assert.Equal(EventTypes.ProductionDaily, EventClassifier.EventTypeOf(DailyJson()));
    }

    [Fact]
    public void EventTypeOf_BrokenJson_ReturnsUnparseable()
    {
        Assert.Equal("unparseable", EventClassifier.EventTypeOf("{not-valid-json"));
    }

    [Fact]
    public void EventTypeOf_NoEventType_ReturnsUnknown()
    {
        Assert.Equal("unknown", EventClassifier.EventTypeOf("{\"schemaVersion\":1}"));
    }
}
