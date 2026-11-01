using GeoPulse.Core.Models;

namespace GeoPulse.Core.Repositories
{
    /// <summary>Репозиторий скважин.</summary>
    public interface IWellRepository
    {
        /// <summary>Скважина по идентификатору.</summary>
        Task<Well?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>Все скважины.</summary>
        Task<IReadOnlyList<Well>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>Пакетная вставка скважин; после сохранения заполняет <see cref="Well.Id"/>.</summary>
        Task AddRangeAsync(IEnumerable<Well> wells, CancellationToken cancellationToken = default);

        /// <summary>Существует ли скважина с указанным кодом НПД.</summary>
        Task<bool> ExistsAsync(int npdWellBoreCode, CancellationToken cancellationToken = default);
    }
}
