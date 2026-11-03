using System.Text.Json;

namespace GeoPulse.Tests.Integration;

/// <summary>
/// Интеграционные тесты идемпотентности CHIngest (ClickHouse) через Testcontainers.
/// Резолв npdWellBoreCode → well_id выполняется по кэшу из PostgreSQL, как в продовом коде.
/// </summary>
[Collection("integration")]
public sealed class CHIngestIdempotencyTests
{
    private readonly GeoPulseContainersFixture _fixture;

    public CHIngestIdempotencyTests(GeoPulseContainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProductionDaily_DeliveredTwice_SingleRowAfterFinal()
    {
        const int npdCode = 5361;
        var date = new DateOnly(2016, 2, 1);
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        await InsertWellAsync(npdCode);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        var daily = DailyJson(npdCode, date);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), daily);
        await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), daily);

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildCHIngestService(topic, dlqTopic, dlqProducer);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(async () => await ClickHouseDailyCountAsync(npdCode, date) == 1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1, await ClickHouseDailyCountAsync(npdCode, date));
    }

    [Fact]
    public async Task ProductionDaily_UnknownWell_GoesToDlq()
    {
        const int unknownCode = 99998;
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        // Скважина не зарегистрирована в PostgreSQL — кэш well_id её не содержит.
        await KafkaTestUtils.ProduceAsync(
            _fixture.KafkaBootstrapServers, topic, unknownCode.ToString(),
            DailyJson(unknownCode, new DateOnly(2016, 3, 1)));

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildCHIngestService(topic, dlqTopic, dlqProducer);

        try
        {
            await service.StartAsync(CancellationToken.None);

            var dlqMessage = KafkaTestUtils.ConsumeOne(_fixture.KafkaBootstrapServers, dlqTopic);
            Assert.NotNull(dlqMessage);
            Assert.Equal(DlqReasons.UnknownWell, KafkaTestUtils.GetHeader(dlqMessage.Message, "reason"));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, await ClickHouseDailyCountAsync(unknownCode, new DateOnly(2016, 3, 1)));
    }

    [Fact]
    public async Task WellRegistered_ThenProductionDaily_ResolvesWithoutRestart()
    {
        const int sentinelCode = 5363;
        const int npdCode = 5364;
        var date = new DateOnly(2016, 2, 3);
        var topic = $"production-daily-{Guid.NewGuid():N}";
        var dlqTopic = $"production-daily-dlq-{Guid.NewGuid():N}";

        // Sentinel уже в PostgreSQL до старта — попадает в стартовый кэш и служит
        // маркером того, что сервис загрузил кэш и начал читать топик.
        await InsertWellAsync(sentinelCode);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, topic);
        await KafkaTestUtils.CreateTopicAsync(_fixture.KafkaBootstrapServers, dlqTopic);

        using var dlqProducer = KafkaTestUtils.BuildProducer(_fixture.KafkaBootstrapServers);
        var service = BuildCHIngestService(topic, dlqTopic, dlqProducer);

        try
        {
            // Старт на «пустой» для целевой скважины БД: npdCode в стартовом кэше отсутствует.
            await service.StartAsync(CancellationToken.None);

            await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, sentinelCode.ToString(), WellJson(sentinelCode));
            await WaitUntilAsync(async () => await ClickHouseWellCountAsync(sentinelCode) == 1);

            // Скважина появляется в PostgreSQL уже после загрузки кэша — как при порядке
            // деплоя (сначала up -d, затем импорт).
            await InsertWellAsync(npdCode);

            await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), WellJson(npdCode));
            await KafkaTestUtils.ProduceAsync(_fixture.KafkaBootstrapServers, topic, npdCode.ToString(), DailyJson(npdCode, date));

            await WaitUntilAsync(async () => await ClickHouseDailyCountAsync(npdCode, date) == 1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1, await ClickHouseDailyCountAsync(npdCode, date));
        Assert.Equal(1, await ClickHouseWellCountAsync(npdCode));
    }

    private CHIngestService BuildCHIngestService(string topic, string dlqTopic, IProducer<string, string> dlqProducer)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClickHouse:ConnectionString"] = _fixture.ClickHouseConnectionString,
                ["ConnectionStrings:CoreDb"] = _fixture.PostgresConnectionString,
                ["Kafka:BootstrapServers"] = _fixture.KafkaBootstrapServers,
                ["Kafka:Topic"] = topic,
                ["Kafka:DlqTopic"] = dlqTopic,
                ["Kafka:BatchSize"] = "500"
            })
            .Build();

        return new CHIngestService(config, NullLogger<CHIngestService>.Instance, dlqProducer);
    }

    private async Task InsertWellAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        db.Wells.Add(new WellEntity { NpdWellBoreCode = npdCode, Name = "W", Field = "Volve", Type = "OP" });
        await db.SaveChangesAsync();
    }

    private async Task<int> ClickHouseDailyCountAsync(int npdCode, DateOnly date)
    {
        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT count() FROM gp.production_daily FINAL " +
            $"WHERE npd_well_bore_code = {npdCode} AND date = '{date:yyyy-MM-dd}'";

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMilliseconds = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch
            {
                // Таблица ClickHouse ещё не создана CHIngest-ом — ждём дальше.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Условие не выполнилось за отведённое время.");
    }

    private static string DailyJson(int npdCode, DateOnly date) =>
        JsonSerializer.Serialize(
            new DailyProductionEvent
            {
                Data = new DailyProductionData { NpdWellBoreCode = npdCode, Date = date, OilVol = 100.5 }
            },
            MessagingJson.Options);

    private static string WellJson(int npdCode) =>
        JsonSerializer.Serialize(
            new WellRegisteredEvent
            {
                Data = new WellRegisteredData { NpdWellBoreCode = npdCode, Name = "W", Field = "Volve", Type = "OP" }
            },
            MessagingJson.Options);

    private async Task<int> ClickHouseWellCountAsync(int npdCode)
    {
        await using var connection = new ClickHouseConnection(_fixture.ClickHouseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count() FROM gp.wells FINAL WHERE npd_well_bore_code = {npdCode}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}

