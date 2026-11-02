using GeoPulse.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.Infrastructure
{
    /// <summary>Контекст EF Core основной БД GeoPulse (PostgreSQL).</summary>
    public class CoreDbContext : DbContext
    {
        /// <summary>Конструктор.</summary>
        public CoreDbContext(DbContextOptions<CoreDbContext> options) : base(options)
        {
        }

        /// <summary>Скважины.</summary>
        public DbSet<WellEntity> Wells => Set<WellEntity>();

        /// <summary>Суточная добыча.</summary>
        public DbSet<DailyProductionEntity> DailyProduction => Set<DailyProductionEntity>();

        /// <summary>Помесячные агрегаты добычи (схема analytics).</summary>
        public DbSet<MonthlyProductionEntity> MonthlyProduction => Set<MonthlyProductionEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<WellEntity>(builder =>
            {
                builder.ToTable("wells");
                builder.HasKey(well => well.Id);
                builder.Property(well => well.NpdWellBoreCode).IsRequired();
                builder.HasIndex(well => well.NpdWellBoreCode).IsUnique();
                builder.Property(well => well.Name).HasMaxLength(100).IsRequired();
                builder.Property(well => well.Field).HasMaxLength(100);
                builder.Property(well => well.Type).HasMaxLength(20);
            });

            modelBuilder.Entity<DailyProductionEntity>(builder =>
            {
                builder.ToTable("daily_production");
                builder.HasKey(item => item.Id);
                builder.Property(item => item.Date).IsRequired();
                builder.HasIndex(item => new { item.WellId, item.Date }).IsUnique();
                builder.HasOne(item => item.Well)
                    .WithMany(well => well.DailyProduction)
                    .HasForeignKey(item => item.WellId)
                    .HasConstraintName("fk_daily_production_wells")
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MonthlyProductionEntity>(builder =>
            {
                builder.ToTable("monthly_production", "analytics");
                builder.HasKey(item => new { item.WellId, item.Year, item.Month });
                builder.Property(item => item.OilVolSum).IsRequired();
                builder.Property(item => item.GasVolSum).IsRequired();
                builder.Property(item => item.WaterVolSum).IsRequired();
                builder.Property(item => item.WaterInjectionVolSum).IsRequired();
                builder.Property(item => item.OnStreamHrsSum).IsRequired();
                builder.Property(item => item.DaysCount).IsRequired();
            });

            ApplySnakeCaseNames(modelBuilder);
        }

        /// <summary>Приводит имена колонок к snake_case.</summary>
        private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    property.SetColumnName(ToSnakeCase(property.Name));
                }
            }
        }

        private static string ToSnakeCase(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length + 8);
            for (var i = 0; i < name.Length; i++)
            {
                var current = name[i];
                if (char.IsUpper(current) && i > 0)
                {
                    var previous = name[i - 1];
                    var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    {
                        builder.Append('_');
                    }
                }

                builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString();
        }
    }
}
