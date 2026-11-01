using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.Infrastructure.Repositories
{
    /// <summary>Репозиторий суточной добычи (EF Core).</summary>
    public sealed class DailyProductionRepository : IDailyProductionRepository
    {
        private readonly CoreDbContext _db;

        /// <summary>Конструктор.</summary>
        public DailyProductionRepository(CoreDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc />
        public async Task<DailyProduction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var entity = await _db.DailyProduction
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            return entity is null ? null : Mapper.ToModel(entity);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DailyProduction>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var entities = await _db.DailyProduction
                .AsNoTracking()
                .OrderBy(item => item.WellId)
                .ThenBy(item => item.Date)
                .ToListAsync(cancellationToken);

            return entities.Select(Mapper.ToModel).ToList();
        }

        /// <inheritdoc />
        public async Task AddRangeAsync(IEnumerable<DailyProduction> items, CancellationToken cancellationToken = default)
        {
            var models = items.ToList();
            if (models.Count == 0)
            {
                return;
            }

            _db.DailyProduction.AddRange(models.Select(Mapper.ToEntity));
            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default) =>
            _db.DailyProduction.AnyAsync(
                item => item.WellId == wellId && item.Date == date,
                cancellationToken);
    }
}
