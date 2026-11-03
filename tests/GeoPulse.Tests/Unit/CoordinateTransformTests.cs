namespace GeoPulse.Tests.Unit;

/// <summary>
/// Проверка геодезического преобразования ED50/UTM31N → WGS84 (EPSG:1613) на
/// контрольных парах из sql/seed_well_locations.sql: координаты устьев 7 скважин Volve.
/// </summary>
public sealed class CoordinateTransformTests
{
    public static IEnumerable<object[]> KnownSurfaceCoordinates => new[]
    {
        // easting, northing, latitude (WGS84), longitude (WGS84)
        new object[] { 435049.831, 6478560.825, 58.44097404, 1.88589571 }, // 15/9-F-4
        new object[] { 435046.488, 6478566.687, 58.44102618, 1.88583679 }, // 15/9-F-1 C
        new object[] { 435052.438, 6478562.310, 58.44098776, 1.88593993 }, // 15/9-F-14
        new object[] { 435050.210, 6478566.220, 58.44102254, 1.88590065 }, // 15/9-F-12
        new object[] { 435050.945, 6478558.870, 58.44095665, 1.88591532 }, // 15/9-F-5
        new object[] { 435049.095, 6478568.172, 58.44103990, 1.88588101 }, // 15/9-F-11
        new object[] { 435053.552, 6478560.355, 58.44097037, 1.88595955 }, // 15/9-F-15 D
    };

    [Theory]
    [MemberData(nameof(KnownSurfaceCoordinates))]
    public void Ed50Utm31ToWgs84_MatchesSeedWellLocations(
        double easting, double northing, double expectedLat, double expectedLon)
    {
        var (lat, lon) = CoordinateTransform.Ed50Utm31ToWgs84(easting, northing);

        Assert.Equal(expectedLat, lat, 5);
        Assert.Equal(expectedLon, lon, 5);
    }
}
