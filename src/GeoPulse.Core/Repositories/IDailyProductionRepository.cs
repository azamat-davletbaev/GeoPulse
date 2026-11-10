using GeoPulse.Core.Models;

namespace GeoPulse.Core.Repositories
{
    /// <summary>Репозиторий суточной добычи (чтение для API и отчётов).</summary>
    public interface IDailyProductionRepository
    {
        /// <summary>Страница суточной добычи скважины по фильтру (диапазон дат, сортировка, пагинация).</summary>
        Task<PagedResult<DailyProduction>> GetPageAsync(
            int wellId,
            DailyProductionQuery query,
            CancellationToken cancellationToken = default);

        /// <summary>Запись добычи скважины за конкретную дату.</summary>
        Task<DailyProduction?> GetByDateAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default);

        /// <summary>Границы периода и число суточных записей скважины; null — записей нет.</summary>
        Task<ProductionCoverage?> GetCoverageAsync(int wellId, CancellationToken cancellationToken = default);
    }
}
