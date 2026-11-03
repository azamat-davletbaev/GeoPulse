namespace GeoPulse.Infrastructure.Entities
{
    /// <summary>EF-сущность метаданных траектории ствола (таблица <c>well_trajectory_meta</c>): забой (TD).</summary>
    public class WellTrajectoryMetaEntity
    {
        /// <summary>PK и FK → wells.Id (1:1 со скважиной).</summary>
        public int WellId { get; set; }

        /// <summary>Забой по стволу (Bottom Hole MD), м.</summary>
        public double? TdMd { get; set; }

        /// <summary>Забой по вертикали (Bottom Hole TVD), м.</summary>
        public double? TdTvd { get; set; }

        /// <summary>Скважина (навигация).</summary>
        public WellEntity? Well { get; set; }
    }
}
