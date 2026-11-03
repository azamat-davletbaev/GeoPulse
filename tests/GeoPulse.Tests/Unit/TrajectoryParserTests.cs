namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тест парсера текстовых выгрузок траекторий (*_ACTUAL) на реальном фрагменте
/// файла 15/9-F-4 (data/Volve_Well_technical_data/WellWellbore/15_9-F-4/15_9-F-4/...).
/// </summary>
public sealed class TrajectoryParserTests
{
    private const string Fixture =
        """
        HEADER INFORMATION (all units in Statoil default)
        ----------------------------------------------------------------
        COMPANY: Statoil Norway
        FIELD: SLEIPNER
        WELL NAME: F-4
        WELLBORE NAME: F-4
        Kick Off Depth: 145.90m
        ----------------------------------------------------------------
        Surface EW: 435049.831m
        Surface NS: 6478560.825m
        Bottom Hole MD: 3510.00m
        Bottom Hole TVD: 3138.06m
        Bottom Hole EW: 435653.833m
        Bottom Hole NS: 6477677.554m
        ----------------------------------------------------------------
        Map-Zone: Zone 31N (0 E to 6 E)
        Geo Datum: European 1950 - Mean
        ----------------------------------------------------------------
        SURVEY LIST
        MD        Inc       Azim      TVD       X-Offset (E/W)  Y-Offset (N/S)  UTM E/W         UTM N/S         DLS
        m RKB     deg       deg       m RKB     m               m               m               m               deg/30m
        145.90    0.00      0.00      145.90    -0.190          -2.699          435049.831      6478560.825     0.00
        184.12    0.10      233.25    184.12    -0.216          -2.719          435049.805      6478560.805     0.08
        224.45    0.16      227.58    224.45    -0.286          -2.778          435049.735      6478560.746     0.05
        3510.00   54.67     202.72    3138.06   604.023         -886.278        435653.833      6477677.554     0.00
        """;

    [Fact]
    public void Parse_ExtractsBottomHoleDepthAndSurveyPoints()
    {
        var parsed = TrajectoryParser.Parse(Fixture);

        Assert.NotNull(parsed);
        Assert.Equal(3510.00, parsed!.TdMd);
        Assert.Equal(3138.06, parsed.TdTvd);
        Assert.Equal(4, parsed.Points.Count);
    }

    [Fact]
    public void Parse_FirstPoint_HasSurfaceCoordinates()
    {
        var parsed = TrajectoryParser.Parse(Fixture)!;

        var first = parsed.Points[0];
        Assert.Equal(145.90, first.Md);
        Assert.Equal(145.90, first.Tvd);
        Assert.Equal(0.00, first.Inclination);
        Assert.Equal(0.00, first.Azimuth);
        Assert.Equal(435049.831, first.Easting);
        Assert.Equal(6478560.825, first.Northing);
    }

    [Fact]
    public void Parse_LastPoint_MatchesBottomHole()
    {
        var parsed = TrajectoryParser.Parse(Fixture)!;

        var last = parsed.Points[^1];
        Assert.Equal(3510.00, last.Md);
        Assert.Equal(3138.06, last.Tvd);
        Assert.Equal(54.67, last.Inclination);
        Assert.Equal(202.72, last.Azimuth);
        Assert.Equal(435653.833, last.Easting);
        Assert.Equal(6477677.554, last.Northing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyContent_ReturnsNull(string? content)
    {
        Assert.Null(TrajectoryParser.Parse(content!));
    }

    [Fact]
    public void Parse_WithoutSurveyList_ReturnsNull()
    {
        Assert.Null(TrajectoryParser.Parse("HEADER INFORMATION\nBottom Hole MD: 100.00m\n"));
    }
}
