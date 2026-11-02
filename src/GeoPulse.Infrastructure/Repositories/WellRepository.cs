using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.Infrastructure.Repositories
{
    /// <summary>Репозиторий скважин (EF Core).</summary>
    public sealed class WellRepository : IWellRepository
    {
        private readonly CoreDbContext _db;

        /// <summary>Конструктор.</summary>
        public WellRepository(CoreDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc />
        public async Task<Well?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _db.Wells
                .AsNoTracking()
                .FirstOrDefaultAsync(well => well.Id == id, cancellationToken);

            return entity is null ? null : Mapper.ToModel(entity);
        }

        /// <inheritdoc />
        public async Task<Well?> GetByNpdWellBoreCodeAsync(int npdWellBoreCode, CancellationToken cancellationToken = default)
        {
            var entity = await _db.Wells
                .AsNoTracking()
                .FirstOrDefaultAsync(well => well.NpdWellBoreCode == npdWellBoreCode, cancellationToken);

            return entity is null ? null : Mapper.ToModel(entity);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Well>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var entities = await _db.Wells
                .AsNoTracking()
                .OrderBy(well => well.NpdWellBoreCode)
                .ToListAsync(cancellationToken);

            return entities.Select(Mapper.ToModel).ToList();
        }

        /// <inheritdoc />
        public async Task AddRangeAsync(IEnumerable<Well> wells, CancellationToken cancellationToken = default)
        {
            var models = wells.ToList();
            if (models.Count == 0)
            {
                return;
            }

            var entities = models.Select(Mapper.ToEntity).ToList();
            _db.Wells.AddRange(entities);
            await _db.SaveChangesAsync(cancellationToken);

            for (var i = 0; i < models.Count; i++)
            {
                models[i].Id = entities[i].Id;
            }
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(int npdWellBoreCode, CancellationToken cancellationToken = default) =>
            _db.Wells.AnyAsync(well => well.NpdWellBoreCode == npdWellBoreCode, cancellationToken);
    }
}
