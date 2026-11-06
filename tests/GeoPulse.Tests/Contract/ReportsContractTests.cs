using System.Net;

namespace GeoPulse.Tests.Contract;

/// <summary>
/// Контрактные тесты GET /api/Reports/*: тип контента, Content-Disposition=attachment,
/// корректное имя файла с расширением. Проверяются через реальный HTTP-пайплайн (TestServer)
/// с in-memory подменами, без обращения к ClickHouse и PostgreSQL.
/// Скважина адресуется кодом НПД — так же, как остальные ресурсы /api/wells.
/// </summary>
public sealed class ReportsContractTests
{
    private const int KnownWellCode = 5351;

    private static void RegisterReports(IServiceCollection services)
    {
        services.AddScoped(_ => new ReportDataLoader(
            new FakeWellRepository(new[]
            {
                new Well { Id = 1, NpdWellBoreCode = KnownWellCode, Name = "W", Field = "Volve", Type = "OP" }
            }),
            new FakeMonthlyProductionReader(
                new[]
                {
                    new MonthlyProduction { WellId = 1, Year = 2016, Month = 1, OilVolSum = 100, WaterVolSum = 10 }
                },
                new ProductionSummary { TotalOilVol = 100, TotalWaterVol = 10 })));
        services.AddSingleton(new XlsxReportWriter());
        services.AddSingleton(new CsvReportWriter());
    }

    [Fact]
    public async Task MonthlyXlsx_ReturnsSpreadsheetContentType_AndAttachmentFilename()
    {
        var (app, client) = await ApiTestHost.StartAsync(RegisterReports);

        try
        {
            var response = await client.GetAsync("/api/Reports/monthly.xlsx");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("attachment", disposition.DispositionType);

            var fileName = disposition.FileNameStar ?? disposition.FileName;
            Assert.NotNull(fileName);
            Assert.EndsWith(".xlsx", fileName);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MonthlyCsv_ReturnsTextCsv_AndAttachmentFilename()
    {
        var (app, client) = await ApiTestHost.StartAsync(RegisterReports);

        try
        {
            var response = await client.GetAsync("/api/Reports/monthly.csv");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("attachment", disposition.DispositionType);

            var fileName = disposition.FileNameStar ?? disposition.FileName;
            Assert.NotNull(fileName);
            Assert.EndsWith(".csv", fileName);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task WellXlsx_ReturnsSpreadsheetContentType_AndAttachmentFilenameWithWellCode()
    {
        var (app, client) = await ApiTestHost.StartAsync(RegisterReports);

        try
        {
            var response = await client.GetAsync($"/api/Reports/wells/{KnownWellCode}.xlsx");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);

            var disposition = response.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("attachment", disposition.DispositionType);

            var fileName = disposition.FileNameStar ?? disposition.FileName;
            Assert.NotNull(fileName);
            Assert.StartsWith($"well_{KnownWellCode}_", fileName);
            Assert.EndsWith(".xlsx", fileName);

            // Тело — настоящий xlsx (ZIP-контейнер), а не пустая заглушка.
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 0);
            Assert.Equal(0x50, bytes[0]);
            Assert.Equal(0x4B, bytes[1]);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task WellXlsx_UnknownWell_Returns404()
    {
        var (app, client) = await ApiTestHost.StartAsync(RegisterReports);

        try
        {
            var response = await client.GetAsync("/api/Reports/wells/999999.xlsx");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":404", body);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
