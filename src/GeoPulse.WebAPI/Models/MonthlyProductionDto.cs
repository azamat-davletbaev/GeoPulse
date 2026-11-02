using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models
{
    /// <summary>Помесячный агрегат добычи скважины.</summary>
    public record MonthlyProductionDto
    {
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

        /// <summary>Проекция доменной модели в DTO.</summary>
        public static MonthlyProductionDto FromModel(MonthlyProduction model) => new()
        {
            Year = model.Year,
            Month = model.Month,
            OilVolSum = model.OilVolSum,
            GasVolSum = model.GasVolSum,
            WaterVolSum = model.WaterVolSum,
            WaterInjectionVolSum = model.WaterInjectionVolSum,
            OnStreamHrsSum = model.OnStreamHrsSum,
            DaysCount = model.DaysCount
        };
    }
}
