namespace GeoPulse.Core.Models
{
    /// <summary>Суточная добыча по скважине (лист Daily Production Data).</summary>
    public class DailyProduction
    {
        /// <summary>Идентификатор (PK, Guid).</summary>
        public Guid Id { get; set; }

        /// <summary>Ссылка на скважину (FK → Well.Id).</summary>
        public int WellId { get; set; }

        /// <summary>Дата добычи.</summary>
        public DateOnly Date { get; set; }

        /// <summary>Часы работы за сутки (ON_STREAM_HRS).</summary>
        public double? OnStreamHrs { get; set; }

        /// <summary>Среднее забойное давление, бар.</summary>
        public double? AvgDownholePressure { get; set; }

        /// <summary>Средняя забойная температура, °C.</summary>
        public double? AvgDownholeTemperature { get; set; }

        /// <summary>Средняя депрессия на НКТ, бар.</summary>
        public double? AvgDpTubing { get; set; }

        /// <summary>Среднее давление в затрубном пространстве, бар.</summary>
        public double? AvgAnnulusPressure { get; set; }

        /// <summary>Среднее положение штуцера, %.</summary>
        public double? AvgChokeSize { get; set; }

        /// <summary>Среднее устьевое давление, бар.</summary>
        public double? AvgWhp { get; set; }

        /// <summary>Средняя устьевая температура, °C.</summary>
        public double? AvgWht { get; set; }

        /// <summary>Положение штуцера, %.</summary>
        public double? DpChokeSize { get; set; }

        /// <summary>Добыча нефти, м³/сут.</summary>
        public double? OilVol { get; set; }

        /// <summary>Добыча газа, м³/сут.</summary>
        public double? GasVol { get; set; }

        /// <summary>Добыча воды, м³/сут.</summary>
        public double? WaterVol { get; set; }

        /// <summary>Закачка воды, м³/сут.</summary>
        public double? WaterInjectionVol { get; set; }
    }
}
