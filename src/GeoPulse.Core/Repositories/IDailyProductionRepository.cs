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

        /// <summary>Существует ли запись для пары (скважина, дата).</summary>
        Task<bool> ExistsAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default);
    }
}
