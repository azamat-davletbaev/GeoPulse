using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoPulse.Infrastructure
{
    /// <summary>Фабрика контекста для команд dotnet-ef (design-time).</summary>
    public class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
    {
        /// <inheritdoc />
        public CoreDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CoreDb")
                ?? DatabaseConfiguration.DefaultConnectionString;

            var options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new CoreDbContext(options);
        }
    }
}
