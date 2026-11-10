namespace GeoPulse.Tests.Integration;

/// <summary>
/// Интеграционные тесты общего резолва npdWellBoreCode → well_id (PostgreSQL через Testcontainers).
/// Проверяют контракт кэша: положительный результат запоминается, отсутствующая скважина
/// не кэшируется (иначе событие, пришедшее раньше well.registered, «залипло» бы в DLQ).
/// </summary>
[Collection("integration")]
[Trait("Category", "Integration")]
public sealed class WellIdCacheTests
{
    private readonly GeoPulseContainersFixture _fixture;

    public WellIdCacheTests(GeoPulseContainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ResolveAsync_RegisteredWell_ReturnsWellId()
    {
        const int npdCode = 5481;
        var wellId = await InsertWellAsync(npdCode);

        await using var cache = new WellIdCache(_fixture.PostgresConnectionString);

        Assert.Equal(wellId, await cache.ResolveAsync(npdCode));
    }

    [Fact]
    public async Task ResolveAsync_UnknownWell_ReturnsNull()
    {
        await using var cache = new WellIdCache(_fixture.PostgresConnectionString);

        Assert.Null(await cache.ResolveAsync(999997));
    }

    [Fact]
    public async Task ResolveAsync_UnknownWellRegisteredLater_IsResolvedAfterFirstMiss()
    {
        const int npdCode = 5482;

        await using var cache = new WellIdCache(_fixture.PostgresConnectionString);

        // Первое событие пришло раньше well.registered: промах не должен закэшироваться.
        Assert.Null(await cache.ResolveAsync(npdCode));

        var wellId = await InsertWellAsync(npdCode);

        Assert.Equal(wellId, await cache.ResolveAsync(npdCode));
    }

    [Fact]
    public async Task ResolveAsync_SecondCall_UsesCacheWithoutDatabase()
    {
        const int npdCode = 5483;
        var wellId = await InsertWellAsync(npdCode);

        await using var cache = new WellIdCache(_fixture.PostgresConnectionString);
        Assert.Equal(wellId, await cache.ResolveAsync(npdCode));

        // Удаляем строку из БД: если бы кэш не работал, второй вызов вернул бы null.
        await DeleteWellAsync(npdCode);

        Assert.Equal(wellId, await cache.ResolveAsync(npdCode));
    }

    private async Task<int> InsertWellAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        var well = new WellEntity { NpdWellBoreCode = npdCode, Name = "W", Field = "Volve", Type = "OP" };
        db.Wells.Add(well);
        await db.SaveChangesAsync();
        return well.Id;
    }

    private async Task DeleteWellAsync(int npdCode)
    {
        await using var db = _fixture.CreateDbContext();
        var well = await db.Wells.FirstAsync(item => item.NpdWellBoreCode == npdCode);
        db.Wells.Remove(well);
        await db.SaveChangesAsync();
    }
}
