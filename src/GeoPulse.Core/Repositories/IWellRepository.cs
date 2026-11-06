using GeoPulse.Core.Models;

namespace GeoPulse.Core.Repositories
{
    /// <summary>Репозиторий скважин.</summary>
    public interface IWellRepository
    {
        /// <summary>Скважина по идентификатору.</summary>
        Task<Well?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>Скважина по коду НПД (натуральный ключ).</summary>
        Task<Well?> GetByNpdWellBoreCodeAsync(int npdWellBoreCode, CancellationToken cancellationToken = default);

        /// <summary>Все скважины.</summary>
        Task<IReadOnlyList<Well>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>Все скважины с координатами устья (LEFT JOIN well_locations).</summary>
        Task<IReadOnlyList<WellLocation>> GetAllWithLocationsAsync(CancellationToken cancellationToken = default);
    }
}
