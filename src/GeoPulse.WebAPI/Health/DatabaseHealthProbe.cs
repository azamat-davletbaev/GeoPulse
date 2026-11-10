using GeoPulse.Infrastructure;

namespace GeoPulse.WebAPI.Health
{
    /// <summary>Проба доступности PostgreSQL через EF Core (единственное место, где /health видит DbContext).</summary>
    public sealed class DatabaseHealthProbe : IDatabaseHealthProbe
    {
        private readonly CoreDbContext _db;

        /// <summary>Конструктор.</summary>
        public DatabaseHealthProbe(CoreDbContext db)
        {
            _db = db;
        }

        /// <inheritdoc />
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
            _db.Database.CanConnectAsync(cancellationToken);
    }
}
