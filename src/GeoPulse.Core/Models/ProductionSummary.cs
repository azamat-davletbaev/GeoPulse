namespace GeoPulse.Core.Models
{
    /// <summary>Суммарная добыча по всем скважинам за всё время.</summary>
    public class ProductionSummary
    {
        /// <summary>Всего нефти, м³.</summary>
        public double TotalOilVol { get; set; }

        /// <summary>Всего газа, м³.</summary>
        public double TotalGasVol { get; set; }

        /// <summary>Всего воды, м³.</summary>
        public double TotalWaterVol { get; set; }

        /// <summary>Всего закачано воды, м³.</summary>
        public double TotalWaterInjectionVol { get; set; }

        /// <summary>Всего часов работы.</summary>
        public double TotalOnStreamHrs { get; set; }

        /// <summary>Всего суток с данными.</summary>
        public int TotalDays { get; set; }
    }
}
