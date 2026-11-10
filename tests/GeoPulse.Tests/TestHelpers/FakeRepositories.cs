namespace GeoPulse.Tests.TestHelpers;

/// <summary>In-memory подмена IWellRepository для контрактных тестов API.</summary>
internal sealed class FakeWellRepository : IWellRepository
{
    private readonly List<Well> _wells;
    private readonly List<WellLocation> _locations;

    public FakeWellRepository(IEnumerable<Well>? wells = null, IEnumerable<WellLocation>? locations = null)
    {
        _wells = wells?.ToList() ?? new List<Well>();
        _locations = locations?.ToList() ?? new List<WellLocation>();
    }

    public Task<Well?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_wells.FirstOrDefault(well => well.Id == id));

    public Task<Well?> GetByNpdWellBoreCodeAsync(int npdWellBoreCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(_wells.FirstOrDefault(well => well.NpdWellBoreCode == npdWellBoreCode));

    public Task<IReadOnlyList<Well>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Well>>(_wells);

    public Task<IReadOnlyList<WellLocation>> GetAllWithLocationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WellLocation>>(_locations);
}

/// <summary>In-memory подмена IWellTrajectoryRepository для контрактных тестов траекторий.</summary>
internal sealed class FakeWellTrajectoryRepository : IWellTrajectoryRepository
{
    private readonly IReadOnlyList<WellTrajectory> _trajectories;

    public FakeWellTrajectoryRepository(IEnumerable<WellTrajectory>? trajectories = null)
    {
        _trajectories = trajectories?.ToList() ?? new List<WellTrajectory>();
    }

    public Task<IReadOnlyList<WellTrajectory>> GetByWellIdAsync(int wellId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WellTrajectory>>(
            // Контракт репозитория совпадает с реальной реализацией (EF): точки строго по возрастанию MD.
            _trajectories.OrderBy(item => item.Md).ToList());
}

/// <summary>In-memory подмена IDailyProductionRepository: пустые ответы для контрактных тестов.</summary>
internal sealed class FakeDailyProductionRepository : IDailyProductionRepository
{
    public Task<PagedResult<DailyProduction>> GetPageAsync(int wellId, DailyProductionQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<DailyProduction>());

    public Task<DailyProduction?> GetByDateAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult<DailyProduction?>(null);

    public Task<ProductionCoverage?> GetCoverageAsync(int wellId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProductionCoverage?>(null);
}

/// <summary>In-memory подмена IMonthlyProductionReader для тестов загрузки отчётов и аналитики.</summary>
internal sealed class FakeMonthlyProductionReader : IMonthlyProductionReader
{
    private readonly IReadOnlyList<MonthlyProduction> _monthly;
    private readonly ProductionSummary _summary;

    public FakeMonthlyProductionReader(
        IEnumerable<MonthlyProduction>? monthly = null,
        ProductionSummary? summary = null)
    {
        _monthly = monthly?.ToList() ?? new List<MonthlyProduction>();
        _summary = summary ?? new ProductionSummary();
    }

    public Task<IReadOnlyList<MonthlyProduction>> GetMonthlyByWellAsync(
        int wellId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MonthlyProduction>>(
            _monthly.Where(item => item.WellId == wellId).ToList());

    public Task<IReadOnlyList<MonthlyProduction>> GetAllMonthlyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_monthly);

    public Task<ProductionSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_summary);
}

/// <summary>Подмена пробы БД для тестов /health: доступность задаётся в конструкторе.</summary>
internal sealed class FakeDatabaseHealthProbe : IDatabaseHealthProbe
{
    private readonly bool _available;

    public FakeDatabaseHealthProbe(bool available = true)
    {
        _available = available;
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_available);
}
