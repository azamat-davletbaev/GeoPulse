using System.Text.Json;

namespace GeoPulse.Tests.Integration;

/// <summary>
/// Интеграционные тесты идемпотентности Consumer (PostgreSQL) через Testcontainers.
/// Каждый тест использует собственные топики и коды скважин, поэтому независим от остальных.
/// </summary>
[Collection("integration")]
[Trait("Category", "Integration")]
public sealed class ConsumerIdempotencyTests
{
    private readonly GeoPulseContainersFixture _fixture;

    public ConsumerIdempotencyTests(GeoPulseContainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProductionDaily_DeliveredTwice_InsertedOnce()
    {
        const int npdCode = 5351;
        var date = new DateOnly(2016, 1, 1);
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), WellJson(npdCode));
        var daily = DailyJson(npdCode, date);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), daily);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), daily);

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildConsumerService(topic, dlqTopic, dlqProducer, out var provider);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(async () => await CountDailyProductionAsync(npdCode, date) == 1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            provider.Dispose();
        }

        Assert.Equal(1, await CountWellsAsync(npdCode));
        Assert.Equal(1, await CountDailyProductionAsync(npdCode, date));
    }

    [Fact]
    public async Task WellRegistered_DeliveredTwice_IsIdempotent()
    {
        const int npdCode = 5352;
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        var well = WellJson(npdCode);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), well);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), well);

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildConsumerService(topic, dlqTopic, dlqProducer, out var provider);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(async () => await CountWellsAsync(npdCode) == 1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            provider.Dispose();
        }

        Assert.Equal(1, await CountWellsAsync(npdCode));
    }

    [Fact]
    public async Task Trajectory_DeliveredTwice_InsertedOnce()
    {
        const int npdCode = 5353;
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), WellJson(npdCode));
        var trajectory = TrajectoryJson(npdCode);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), trajectory);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), trajectory);

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildConsumerService(topic, dlqTopic, dlqProducer, out var provider);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(async () => await CountTrajectoryPointsAsync(npdCode) == 2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            provider.Dispose();
        }

        Assert.Equal(1, await CountWellsAsync(npdCode));
        Assert.Equal(2, await CountTrajectoryPointsAsync(npdCode));
        Assert.Equal(1, await CountTrajectoryMetaAsync(npdCode));
        Assert.Equal(1, await CountWellLocationsAsync(npdCode));
    }

    [Fact]
    public async Task ProductionDaily_UnknownWell_GoesToDlq()
    {
        const int unknownCode = 99999;
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        // production.daily без предшествующего well.registered — скважина неизвестна.
        await KafkaTestUtils.ProduceAsync(
            _fixture.KafkaBootstrapServers, topic, unknownCode.ToString(),
            DailyJson(unknownCode, new DateOnly(2016, 3, 1)));

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildConsumerService(topic, dlqTopic, dlqProducer, out var provider);

        try
        {
            await service.StartAsync(CancellationToken.None);

            var dlqMessage = KafkaTestUtils.ConsumeOne(_fixture.KafkaBootstrapServers, dlqTopic);
            Assert.NotNull(dlqMessage);
            Assert.Equal(DlqReasons.WellNotFound, KafkaTestUtils.GetHeader(dlqMessage.Message, "reason"));
            Assert.Equal(DailyJson(unknownCode, new DateOnly(2016, 3, 1)), dlqMessage.Message.Value);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            provider.Dispose();
        }

        Assert.Equal(0, await CountWellsAsync(unknownCode));
    }

    private ProductionConsumerService BuildConsumerService(
        string topic,
        string dlqTopic,
        IProducer<string, string> dlqProducer,
        out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(_fixture.PostgresConnectionString));
        provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = _fixture.KafkaBootstrapServers,
                ["Kafka:Topic"] = topic,
                ["Kafka:DlqTopic"] = dlqTopic,
                ["Kafka:BatchSize"] = "500"
            })
            .Build();

        // DLQ-публикатор и резолв well_id подключаются так же, как в Program.cs консьюмера.
        var dlq = new DlqPublisher(dlqProducer, NullLogger<DlqPublisher>.Instance);
        var wellIds = new WellIdCache(_fixture.PostgresConnectionString);
        var topics = new TopicProvisioner(NullLogger<TopicProvisioner>.Instance);

        return new ProductionConsumerService(
            scopeFactory,
            config,
            NullLogger<ProductionConsumerService>.Instance,
            dlq,
            wellIds,
            topics);
    }

    private async Task<int> CountWellsAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.Wells.CountAsync(well => well.NpdWellBoreCode == npdCode);
    }

    private async Task<int> CountDailyProductionAsync(int npdCode, DateOnly date)
    {
        await using var db = _fixture.CreateDbContext();
        var well = await db.Wells.AsNoTracking().FirstOrDefaultAsync(w => w.NpdWellBoreCode == npdCode);
        if (well is null)
        {
            return 0;
        }

        return await db.DailyProduction.CountAsync(item => item.WellId == well.Id && item.Date == date);
    }

    private async Task<int> CountTrajectoryPointsAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        var well = await db.Wells.AsNoTracking().FirstOrDefaultAsync(w => w.NpdWellBoreCode == npdCode);
        if (well is null)
        {
            return 0;
        }

        return await db.WellTrajectories.CountAsync(item => item.WellId == well.Id);
    }

    private async Task<int> CountTrajectoryMetaAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        var well = await db.Wells.AsNoTracking().FirstOrDefaultAsync(w => w.NpdWellBoreCode == npdCode);
        if (well is null)
        {
            return 0;
        }

        return await db.WellTrajectoryMeta.CountAsync(item => item.WellId == well.Id);
    }

    private async Task<int> CountWellLocationsAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        var well = await db.Wells.AsNoTracking().FirstOrDefaultAsync(w => w.NpdWellBoreCode == npdCode);
        if (well is null)
        {
            return 0;
        }

        return await db.WellLocations.CountAsync(item => item.WellId == well.Id);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMilliseconds = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Условие не выполнилось за отведённое время.");
    }

    private static string WellJson(int npdCode) =>
        JsonSerializer.Serialize(
            new WellRegisteredEvent
            {
                Data = new WellRegisteredData { NpdWellBoreCode = npdCode, Name = "W", Field = "Volve", Type = "OP" }
            },
            MessagingJson.Options);

    private static string DailyJson(int npdCode, DateOnly date) =>
        JsonSerializer.Serialize(
            new DailyProductionEvent
            {
                Data = new DailyProductionData { NpdWellBoreCode = npdCode, Date = date, OilVol = 100.5 }
            },
            MessagingJson.Options);

    private static string TrajectoryJson(int npdCode) =>
        JsonSerializer.Serialize(
            new TrajectoryEvent
            {
                Data = new TrajectoryData
                {
                    NpdWellBoreCode = npdCode,
                    TdMd = 3510.00,
                    TdTvd = 3138.06,
                    SurfaceEasting = 435049.831,
                    SurfaceNorthing = 6478560.825,
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
                        },
                        new TrajectoryPointData
                        {
                            Md = 3510.00,
                            Tvd = 3138.06,
                            Inclination = 54.67,
                            Azimuth = 202.72,
                            Easting = 435653.833,
                            Northing = 6477677.554
                        }
                    }
                }
            },
            MessagingJson.Options);
}


