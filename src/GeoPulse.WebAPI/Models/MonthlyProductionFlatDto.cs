namespace GeoPulse.WebAPI.Models
{
    /// <summary>Плоская строка помесячного агрегата с идентификатором скважины (для отчётов по месторождению).</summary>
    public record MonthlyProductionFlatDto
    {
        /// <summary>Идентификатор скважины (wells.Id).</summary>
        public int WellId { get; init; }

        /// <summary>Год.</summary>
        public int Year { get; init; }

        /// <summary>Месяц (1..12).</summary>
        public int Month { get; init; }

        /// <summary>Суммарная добыча нефти, м³.</summary>
        public double OilVolSum { get; init; }

        /// <summary>Суммарная добыча воды, м³.</summary>
        public double WaterVolSum { get; init; }
    }
}
