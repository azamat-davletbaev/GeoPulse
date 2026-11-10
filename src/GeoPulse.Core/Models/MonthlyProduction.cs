namespace GeoPulse.Core.Models
{
    /// <summary>
    /// Помесячный агрегат добычи скважины. Источник — ClickHouse (gp.monthly_production):
    /// в PostgreSQL эта таблица не хранится, поэтому модель живёт в Core и используется
    /// как доменный контракт между читателем аналитики и слоем представления.
    /// </summary>
    public sealed record MonthlyProduction
    {
        /// <summary>Ссылка на скважину (wells.Id).</summary>
        public int WellId { get; init; }

        /// <summary>Год.</summary>
        public int Year { get; init; }

        /// <summary>Месяц (1..12).</summary>
        public int Month { get; init; }

        /// <summary>Суммарная добыча нефти, м³.</summary>
        public double OilVolSum { get; init; }

        /// <summary>Суммарная добыча газа, м³.</summary>
        public double GasVolSum { get; init; }

        /// <summary>Суммарная добыча воды, м³.</summary>
        public double WaterVolSum { get; init; }

        /// <summary>Суммарная закачка воды, м³.</summary>
        public double WaterInjectionVolSum { get; init; }

        /// <summary>Суммарные часы работы.</summary>
        public double OnStreamHrsSum { get; init; }

        /// <summary>Число суток с данными в месяце.</summary>
        public int DaysCount { get; init; }
    }
}
