using System.Net;
using System.Text.Json;

namespace GeoPulse.Tests.Contract;

/// <summary>
/// Контрактный тест GET /api/wells: число объектов, camelCase-поля и отсутствие внутреннего id.
/// Проверяется через реальный HTTP-пайплайн (TestServer) с in-memory стабами репозиториев.
/// </summary>
public sealed class WellsContractTests
{
    [Fact]
    public async Task GetWells_ReturnsSevenWells_WithCamelCaseFields_AndNoIdProperty()
    {
        var locations = Enumerable.Range(1, 7)
            .Select(i => new WellLocation
            {
                WellId = i,
                NpdWellBoreCode = 5300 + i,
                Name = $"Well {i}",
                Field = "Volve",
                Type = "OP",
                Latitude = 58.0 + i,
                Longitude = 1.5 + i
            })
            .ToList();

        var (app, client) = await ApiTestHost.StartAsync(services =>
        {
            services.AddSingleton<IWellRepository>(new FakeWellRepository(locations: locations));
            services.AddSingleton<IDailyProductionRepository>(new FakeDailyProductionRepository());
        });

        try
        {
            var response = await client.GetAsync("/api/wells");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var items = doc.RootElement.EnumerateArray().ToArray();
            Assert.Equal(7, items.Length);

            var first = items[0];
            Assert.True(first.TryGetProperty("wellId", out _));
            Assert.True(first.TryGetProperty("npdWellBoreCode", out _));
            Assert.True(first.TryGetProperty("latitude", out _));
            Assert.True(first.TryGetProperty("longitude", out _));
            Assert.False(first.TryGetProperty("id", out _));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
