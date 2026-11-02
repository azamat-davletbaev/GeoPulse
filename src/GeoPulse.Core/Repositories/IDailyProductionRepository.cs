using GeoPulse.Core.Models;

namespace GeoPulse.Core.Repositories
{
    /// <summary>Репозиторий суточной добычи.</summary>
    public interface IDailyProductionRepository
    {
        /// <summary>Запись добычи по идентификатору.</summary>
        Task<DailyProduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Все записи суточной добычи.</summary>
        Task<IReadOnlyList<DailyProduction>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>Пакетная вставка записей добычи.</summary>
        Task AddRangeAsync(IEnumerable<DailyProduction> items, CancellationToken cancellationToken = default);

        /// <summary>Страница суточной добычи скважины по фильтру (диапазон дат, сортировка, пагинация).</summary>
        Task<PagedResult<DailyProduction>> GetPageAsync(
            int wellId,
            DailyProductionQuery query,
            CancellationToken cancellationToken = default);

        /// <summary>Запись добычи скважины за конкретную дату.</summary>
        Task<DailyProduction?> GetByDateAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default);

        /// <summary>Границы периода и число суточных записей скважины; null — записей нет.</summary>
        Task<ProductionCoverage?> GetCoverageAsync(int wellId, CancellationToken cancellationToken = default);

        /// <summary>Существует ли запись для пары (скважина, дата).</summary>
        Task<bool> ExistsAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default);
    }
}
