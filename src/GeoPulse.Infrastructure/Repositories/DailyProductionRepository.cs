using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure.Entities;
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
        public async Task<PagedResult<DailyProduction>> GetPageAsync(
            int wellId,
            DailyProductionQuery query,
            CancellationToken cancellationToken = default)
        {
            var source = BuildFilteredQuery(wellId, query);

            var totalCount = await source.CountAsync(cancellationToken);

            // Сортировка и пагинация выполняются в SQL; пустая страница не требует похода за данными.
            List<DailyProductionEntity> entities;
            if (query.PageSize <= 0 || query.Skip >= totalCount)
            {
                entities = new List<DailyProductionEntity>();
            }
            else
            {
                var ordered = query.NewestFirst
                    ? source.OrderByDescending(item => item.Date)
                    : source.OrderBy(item => item.Date);

                entities = await ordered
                    .Skip(query.Skip)
                    .Take(query.PageSize)
                    .ToListAsync(cancellationToken);
            }

            return new PagedResult<DailyProduction>
            {
                Items = entities.Select(Mapper.ToModel).ToList(),
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }

        /// <inheritdoc />
        public async Task<DailyProduction?> GetByDateAsync(
            int wellId,
            DateOnly date,
            CancellationToken cancellationToken = default)
        {
            var entity = await _db.DailyProduction
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.WellId == wellId && item.Date == date, cancellationToken);

            return entity is null ? null : Mapper.ToModel(entity);
        }

        /// <inheritdoc />
        public async Task<ProductionCoverage?> GetCoverageAsync(int wellId, CancellationToken cancellationToken = default)
        {
            // GroupBy по ключу скважины: агрегат по пустому набору не возвращает строку вовсе,
            // поэтому MIN/MAX не приходит как NULL и не ломает маппинг в DateOnly.
            var coverage = await _db.DailyProduction
                .AsNoTracking()
                .Where(item => item.WellId == wellId)
                .GroupBy(item => item.WellId)
                .Select(group => new
                {
                    FirstDate = group.Min(item => item.Date),
                    LastDate = group.Max(item => item.Date),
                    DaysCount = group.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (coverage is null)
            {
                return null;
            }

            return new ProductionCoverage
            {
                FirstDate = coverage.FirstDate,
                LastDate = coverage.LastDate,
                DaysCount = coverage.DaysCount
            };
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(int wellId, DateOnly date, CancellationToken cancellationToken = default) =>
            _db.DailyProduction.AnyAsync(
                item => item.WellId == wellId && item.Date == date,
                cancellationToken);

        /// <summary>Базовый запрос по скважине с фильтром по диапазону дат.</summary>
        private IQueryable<DailyProductionEntity> BuildFilteredQuery(int wellId, DailyProductionQuery query)
        {
            var source = _db.DailyProduction
                .AsNoTracking()
                .Where(item => item.WellId == wellId);

            if (query.From is { } from)
            {
                source = source.Where(item => item.Date >= from);
            }

            if (query.To is { } to)
            {
                source = source.Where(item => item.Date <= to);
            }

            return source;
        }
    }
}
