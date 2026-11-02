using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models
{
    /// <summary>Суммарная добыча по всем скважинам за всё время.</summary>
    public record ProductionSummaryDto
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

        /// <summary>Проекция доменной модели в DTO.</summary>
        public static ProductionSummaryDto FromModel(ProductionSummary model) => new()
        {
            TotalOilVol = model.TotalOilVol,
            TotalGasVol = model.TotalGasVol,
            TotalWaterVol = model.TotalWaterVol,
            TotalWaterInjectionVol = model.TotalWaterInjectionVol,
            TotalOnStreamHrs = model.TotalOnStreamHrs,
            TotalDays = model.TotalDays
        };
    }
}
