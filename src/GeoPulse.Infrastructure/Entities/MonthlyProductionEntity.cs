namespace GeoPulse.Infrastructure.Entities
{
    /// <summary>EF-сущность помесячного агрегата добычи (таблица <c>analytics.monthly_production</c>).</summary>
    public class MonthlyProductionEntity
    {
        /// <summary>FK → wells.id.</summary>
        public int WellId { get; set; }

        /// <summary>Год агрегата.</summary>
        public int Year { get; set; }

        /// <summary>Месяц агрегата (1..12).</summary>
        public int Month { get; set; }

        /// <summary>Суммарная добыча нефти, м³.</summary>
        public double OilVolSum { get; set; }

        /// <summary>Суммарная добыча газа, м³.</summary>
        public double GasVolSum { get; set; }

        /// <summary>Суммарная добыча воды, м³.</summary>
        public double WaterVolSum { get; set; }

        /// <summary>Суммарная закачка воды, м³.</summary>
        public double WaterInjectionVolSum { get; set; }

        /// <summary>Суммарные часы работы.</summary>
        public double OnStreamHrsSum { get; set; }

        /// <summary>Число суток с данными в месяце.</summary>
        public int DaysCount { get; set; }
    }
}
