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

    public Task AddRangeAsync(IEnumerable<Well> wells, CancellationToken cancellationToken = default)
    {
        _wells.AddRange(wells);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(int npdWellBoreCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(_wells.Any(well => well.NpdWellBoreCode == npdWellBoreCode));
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

/// <summary>In-memory подмена IDailyProductionRepository (в контрактных тестах не используется).</summary>
internal sealed class FakeDailyProductionRepository : IDailyProductionRepository
{
    public Task<DailyProduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<DailyProduction?>(null);

    public Task<IReadOnlyList<DailyProduction>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DailyProduction>>(Array.Empty<DailyProduction>());

    public Task AddRangeAsync(IEnumerable<DailyProduction> items, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<PagedResult<DailyProduction>> GetPageAsync(int wellId, DailyProductionQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<DailyProduction>());

    public Task<DailyProduction?> GetByDateAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult<DailyProduction?>(null);

    public Task<ProductionCoverage?> GetCoverageAsync(int wellId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProductionCoverage?>(null);

    public Task<bool> ExistsAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>In-memory подмена IMonthlyProductionReader для тестов ReportService и ReportsController.</summary>
internal sealed class FakeMonthlyProductionReader : IMonthlyProductionReader
{
    private readonly IReadOnlyList<MonthlyProductionFlatDto> _monthly;
    private readonly ProductionSummaryDto _summary;

    public FakeMonthlyProductionReader(
        IEnumerable<MonthlyProductionFlatDto>? monthly = null,
        ProductionSummaryDto? summary = null)
    {
        _monthly = monthly?.ToList() ?? new List<MonthlyProductionFlatDto>();
        _summary = summary ?? new ProductionSummaryDto();
    }

    public Task<IReadOnlyList<MonthlyProductionFlatDto>> GetAllMonthlyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_monthly);

    public Task<ProductionSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_summary);
}
