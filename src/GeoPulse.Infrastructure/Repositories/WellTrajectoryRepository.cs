namespace GeoPulse.Infrastructure.Repositories
{
    using GeoPulse.Core.Models;
    using GeoPulse.Core.Repositories;
    using Microsoft.EntityFrameworkCore;

    /// <summary>Репозиторий траекторий стволов (EF Core).</summary>
    public sealed class WellTrajectoryRepository : IWellTrajectoryRepository
    {
        private readonly CoreDbContext _db;

        /// <summary>Конструктор.</summary>
        public WellTrajectoryRepository(CoreDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<WellTrajectory>> GetByWellIdAsync(int wellId, CancellationToken cancellationToken = default)
        {
            var entities = await _db.WellTrajectories
                .AsNoTracking()
                .Where(item => item.WellId == wellId)
                .OrderBy(item => item.Md)
                .ToListAsync(cancellationToken);

            return entities.Select(Mapper.ToModel).ToList();
        }
    }
}
