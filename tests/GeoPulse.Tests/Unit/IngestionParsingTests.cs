
namespace GeoPulse.Tests.Unit;

/// <summary>
/// Юнит-тесты парсинга и нормализации Excel-полей Ingestion (private static чистые функции:
/// Text, ParseDouble, ParseInt, ParseDate). Граничные значения: null, DBNull, пустые строки,
/// отрицательные и нулевые числа, неверные строки, OADate-серийники и строковые даты.
/// </summary>
public sealed class IngestionParsingTests
{
    private static string? Text(object? raw) =>
        (string?)ReflectionHelper.InvokePrivateStatic(typeof(IngestionService), "Text", raw!);

    private static double? ParseDouble(object? raw) =>
        (double?)ReflectionHelper.InvokePrivateStatic(typeof(IngestionService), "ParseDouble", raw!);

    private static int? ParseInt(object? raw) =>
        (int?)ReflectionHelper.InvokePrivateStatic(typeof(IngestionService), "ParseInt", raw!);

    private static DateOnly? ParseDate(object? raw) =>
        (DateOnly?)ReflectionHelper.InvokePrivateStatic(typeof(IngestionService), "ParseDate", raw!);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Text_NullOrWhitespace_ReturnsNull(object? raw)
    {
        Assert.Null(Text(raw));
    }

    [Fact]
    public void Text_TrimsAndReturnsValue()
    {
        Assert.Equal("Volve", Text("  Volve  "));
    }

    [Fact]
    public void Text_DbNull_ReturnsNull()
    {
        Assert.Null(Text(DBNull.Value));
    }

    [Fact]
    public void ParseDouble_NullAndDbNull_ReturnNull()
    {
        Assert.Null(ParseDouble(null));
        Assert.Null(ParseDouble(DBNull.Value));
    }

    [Fact]
    public void ParseDouble_EmptyAndWhitespace_ReturnNull()
    {
        Assert.Null(ParseDouble(""));
        Assert.Null(ParseDouble("   "));
    }

    [Fact]
    public void ParseDouble_InvalidString_ReturnsNull()
    {
        Assert.Null(ParseDouble("abc"));
        Assert.Null(ParseDouble("1,5")); // запятая недопустима в InvariantCulture
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-12.5, -12.5)]
    [InlineData(42.25, 42.25)]
    public void ParseDouble_NumericStrings_Parsed(double input, double expected)
    {
        Assert.Equal(expected, ParseDouble(input.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ParseDouble_ZeroAndNegativeValues_ArePreserved()
    {
        Assert.Equal(0.0, ParseDouble(0.0));
        Assert.Equal(0.0, ParseDouble(0));
        Assert.Equal(-5.0, ParseDouble(-5.0));
    }

    [Theory]
    [InlineData(3.7, 4)]
    [InlineData(-3.7, -4)]
    [InlineData(2.5, 3)]
    public void ParseInt_RoundsAwayFromZero(double input, int expected)
    {
        Assert.Equal(expected, ParseInt(input));
    }

    [Fact]
    public void ParseInt_NullAndEmpty_ReturnNull()
    {
        Assert.Null(ParseInt(null));
        Assert.Null(ParseInt(""));
    }

    [Fact]
    public void ParseDate_DateTime_Converted()
    {
        Assert.Equal(new DateOnly(2016, 1, 1), ParseDate(new DateTime(2016, 1, 1)));
    }

    [Fact]
    public void ParseDate_IsoString_Parsed()
    {
        Assert.Equal(new DateOnly(2016, 1, 1), ParseDate("2016-01-01"));
    }

    [Fact]
    public void ParseDate_EmptyAndInvalid_ReturnNull()
    {
        Assert.Null(ParseDate(null));
        Assert.Null(ParseDate(DBNull.Value));
        Assert.Null(ParseDate(""));
        Assert.Null(ParseDate("   "));
        Assert.Null(ParseDate("not-a-date"));
    }

    [Fact]
    public void ParseDate_OaDateSerial_Converted()
    {
        const double serial = 42401.0;
        Assert.Equal(DateOnly.FromDateTime(DateTime.FromOADate(serial)), ParseDate(serial));
    }

    [Fact]
    public void ParseDate_OutOfOaDateRange_ReturnsNull()
    {
        Assert.Null(ParseDate(0.5));     // < 1
        Assert.Null(ParseDate("100000")); // >= 100000 и не строка-дата
    }
}
