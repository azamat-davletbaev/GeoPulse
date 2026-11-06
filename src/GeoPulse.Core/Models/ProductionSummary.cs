namespace GeoPulse.Core.Models
{
    /// <summary>Суммарная добыча по всем скважинам за всё время (сумма помесячных агрегатов).</summary>
    public sealed record ProductionSummary
    {
        /// <summary>Всего нефти, м³.</summary>
        public double TotalOilVol { get; init; }

        /// <summary>Всего газа, м³.</summary>
        public double TotalGasVol { get; init; }

        /// <summary>Всего воды, м³.</summary>
        public double TotalWaterVol { get; init; }

        /// <summary>Всего закачано воды, м³.</summary>
        public double TotalWaterInjectionVol { get; init; }

        /// <summary>Всего часов работы.</summary>
        public double TotalOnStreamHrs { get; init; }

        /// <summary>Всего суток с данными.</summary>
        public int TotalDays { get; init; }
    }
}
