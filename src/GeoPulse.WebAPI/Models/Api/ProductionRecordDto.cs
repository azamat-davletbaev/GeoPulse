using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models.Api
{
    /// <summary>Суточная добыча скважины.</summary>
    public record ProductionRecordDto
    {
        /// <summary>Дата добычи.</summary>
        public DateOnly Date { get; init; }

        /// <summary>Часы работы за сутки.</summary>
        public double? OnStreamHrs { get; init; }

        /// <summary>Среднее забойное давление, бар.</summary>
        public double? AvgDownholePressure { get; init; }

        /// <summary>Средняя забойная температура, °C.</summary>
        public double? AvgDownholeTemperature { get; init; }

        /// <summary>Средняя депрессия на НКТ, бар.</summary>
        public double? AvgDpTubing { get; init; }

        /// <summary>Среднее давление в затрубном пространстве, бар.</summary>
        public double? AvgAnnulusPressure { get; init; }

        /// <summary>Среднее положение штуцера, %.</summary>
        public double? AvgChokeSize { get; init; }

        /// <summary>Среднее устьевое давление, бар.</summary>
        public double? AvgWhp { get; init; }

        /// <summary>Средняя устьевая температура, °C.</summary>
        public double? AvgWht { get; init; }

        /// <summary>Положение штуцера, %.</summary>
        public double? DpChokeSize { get; init; }

        /// <summary>Добыча нефти, м³/сут.</summary>
        public double? OilVol { get; init; }

        /// <summary>Добыча газа, м³/сут.</summary>
        public double? GasVol { get; init; }

        /// <summary>Добыча воды, м³/сут.</summary>
        public double? WaterVol { get; init; }

        /// <summary>Закачка воды, м³/сут.</summary>
        public double? WaterInjectionVol { get; init; }

        /// <summary>Проекция доменной модели в DTO.</summary>
        /// <param name="item">Доменная модель суточной добычи.</param>
        public static ProductionRecordDto FromModel(DailyProduction item) => new()
        {
            Date = item.Date,
            OnStreamHrs = item.OnStreamHrs,
            AvgDownholePressure = item.AvgDownholePressure,
            AvgDownholeTemperature = item.AvgDownholeTemperature,
            AvgDpTubing = item.AvgDpTubing,
            AvgAnnulusPressure = item.AvgAnnulusPressure,
            AvgChokeSize = item.AvgChokeSize,
            AvgWhp = item.AvgWhp,
            AvgWht = item.AvgWht,
            DpChokeSize = item.DpChokeSize,
            OilVol = item.OilVol,
            GasVol = item.GasVol,
            WaterVol = item.WaterVol,
            WaterInjectionVol = item.WaterInjectionVol
        };
    }
}
