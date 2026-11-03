using System.Net;
using System.Text.Json;

namespace GeoPulse.Tests.Contract;

/// <summary>
/// Контрактные тесты новых эндпоинтов этапа 7: GET /api/wells (добавочные поля забоя tdMd/tdTvd)
/// и GET /api/Wells/{wellId}/trajectory (замеры в WGS84). Проверяются через TestServer с in-memory стабами.
/// </summary>
public sealed class TrajectoryContractTests
{
    private static Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client)> StartAsync(
        IEnumerable<Well> wells,
        IEnumerable<WellLocation>? locations = null,
        IEnumerable<WellTrajectory>? trajectories = null) =>
        ApiTestHost.StartAsync(services =>
        {
            services.AddSingleton<IWellRepository>(new FakeWellRepository(wells, locations));
            services.AddSingleton<IDailyProductionRepository>(new FakeDailyProductionRepository());
            services.AddSingleton<IWellTrajectoryRepository>(new FakeWellTrajectoryRepository(trajectories));
        });

    [Fact]
    public async Task GetWells_IncludesTdFields()
    {
        var locations = new[]
        {
            new WellLocation
            {
                WellId = 1,
                NpdWellBoreCode = 5351,
                Name = "Well 1",
                Field = "Volve",
                Type = "OP",
                Latitude = 58.44,
                Longitude = 1.88,
                TdMd = 3510.00,
                TdTvd = 3138.06
            }
        };

        var (app, client) = await StartAsync(Array.Empty<Well>(), locations);

        try
        {
            var response = await client.GetAsync("/api/wells");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var first = doc.RootElement.EnumerateArray().First();
            Assert.Equal(3510.00, first.GetProperty("tdMd").GetDouble());
            Assert.Equal(3138.06, first.GetProperty("tdTvd").GetDouble());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetTrajectory_ReturnsWgs84Points()
    {
        var wells = new[] { new Well { Id = 1, NpdWellBoreCode = 5351, Name = "Well 1", Field = "Volve", Type = "OP" } };
        var trajectories = new[]
        {
            new WellTrajectory
            {
                Md = 145.90,
                Tvd = 145.90,
                Inclination = 0.0,
                Azimuth = 0.0,
                Easting = 435049.831,
                Northing = 6478560.825
            }
        };

        var (app, client) = await StartAsync(wells, trajectories: trajectories);

        try
        {
            var response = await client.GetAsync("/api/Wells/1/trajectory");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var point = doc.RootElement.EnumerateArray().First();

            Assert.Equal(145.90, point.GetProperty("md").GetDouble());
            Assert.Equal(145.90, point.GetProperty("tvd").GetDouble());
            Assert.Equal(58.44097404, point.GetProperty("latitude").GetDouble(), 5);
            Assert.Equal(1.88589571, point.GetProperty("longitude").GetDouble(), 5);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetTrajectory_UnknownWell_Returns404()
    {
        var (app, client) = await StartAsync(Array.Empty<Well>());

        try
        {
            var response = await client.GetAsync("/api/Wells/999/trajectory");
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
