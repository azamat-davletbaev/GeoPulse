namespace GeoPulse.Core.Models
{
    /// <summary>Помесячный агрегат добычи скважины.</summary>
    public class MonthlyProduction
    {
        /// <summary>Ссылка на скважину (FK → Well.Id).</summary>
        public int WellId { get; set; }

        /// <summary>Год.</summary>
        public int Year { get; set; }

        /// <summary>Месяц (1..12).</summary>
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
