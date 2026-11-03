namespace GeoPulse.Infrastructure.Entities
{
    /// <summary>EF-сущность скважины (таблица <c>wells</c>).</summary>
    public class WellEntity
    {
        /// <summary>PK.</summary>
        public int Id { get; set; }

        /// <summary>Код ствола НПД (уникальный).</summary>
        public int NpdWellBoreCode { get; set; }

        /// <summary>Имя ствола.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Месторождение.</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Тип скважины.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Записи суточной добычи.</summary>
        public List<DailyProductionEntity> DailyProduction { get; set; } = new();

        /// <summary>Координаты устья (1:1).</summary>
        public WellLocationEntity? WellLocation { get; set; }
    }
}
