namespace GeoPulse.WebAPI.Models
{
    /// <summary>Данные по скважине для витрины добычи.</summary>
    public record WellDto
    {
        /// <summary>Код скважины в НПД.</summary>
        public int NpdWellBoreCode { get; init; }

        /// <summary>Название скважины.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Дебит нефти, т/сут.</summary>
        public double OilRate { get; init; }
    }
}
