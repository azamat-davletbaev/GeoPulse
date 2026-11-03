using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoPulse.Infrastructure
{
    /// <summary>Регистрация зависимостей инфраструктуры (EF Core + репозитории).</summary>
    public static class DependencyInjection
    {
        /// <summary>Подключает CoreDbContext и репозитории через интерфейсы.</summary>
        public static IServiceCollection AddGeoPulseInfrastructure(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(connectionString));

            services.AddScoped<IWellRepository, WellRepository>();
            services.AddScoped<IDailyProductionRepository, DailyProductionRepository>();
            services.AddScoped<IWellTrajectoryRepository, WellTrajectoryRepository>();
            services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

            return services;
        }
    }
}
