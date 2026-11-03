namespace GeoPulse.Tests.Integration;

/// <summary>
/// Общий fixture на всю интеграционную коллекцию: Postgres, Kafka и ClickHouse поднимаются
/// один раз (ICollectionFixture), а не на каждый тест. Образы совпадают с docker-compose.yml.
/// </summary>
public sealed class GeoPulseContainersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("geopulse_core")
        .WithUsername("geopulse")
        .WithPassword("geopulse")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.0.0").Build();

    private readonly ClickHouseContainer _clickHouse = new ClickHouseBuilder("clickhouse/clickhouse-server:24.8").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    public string ClickHouseConnectionString => _clickHouse.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _kafka.StartAsync(),
            _clickHouse.StartAsync());

        await MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _kafka.DisposeAsync().AsTask(),
            _clickHouse.DisposeAsync().AsTask());
    }

    public CoreDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options);

    private async Task MigrateAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }
}
