using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure.Repositories;
using GeoPulse.Infrastructure.WellIds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoPulse.Infrastructure
{
    /// <summary>Регистрация зависимостей инфраструктуры (EF Core + репозитории).</summary>
    public static class DependencyInjection
    {
        /// <summary>Подключает CoreDbContext, репозитории и резолв well_id через интерфейсы.</summary>
        public static IServiceCollection AddGeoPulseInfrastructure(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(connectionString));

            services.AddScoped<IWellRepository, WellRepository>();
            services.AddScoped<IDailyProductionRepository, DailyProductionRepository>();
            services.AddScoped<IWellTrajectoryRepository, WellTrajectoryRepository>();

            services.AddGeoPulseWellIdResolver(connectionString);

            return services;
        }

        /// <summary>
        /// Регистрирует только резолв npdWellBoreCode → well_id (без EF Core).
        /// Нужен сервисам, которые читают справочник скважин, но не работают со схемой PostgreSQL.
        /// </summary>
        public static IServiceCollection AddGeoPulseWellIdResolver(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton<IWellIdResolver>(_ => new WellIdCache(connectionString));

            return services;
        }
    }
}
