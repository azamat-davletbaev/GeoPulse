using GeoPulse.Core.Models;
using GeoPulse.Infrastructure.Entities;

namespace GeoPulse.Infrastructure.Repositories
{
    /// <summary>Маппинг доменных моделей ↔ EF-сущностей.</summary>
    internal static class Mapper
    {
        public static Well ToModel(WellEntity entity) => new()
        {
            Id = entity.Id,
            NpdWellBoreCode = entity.NpdWellBoreCode,
            Name = entity.Name,
            Field = entity.Field,
            Type = entity.Type
        };

        public static WellEntity ToEntity(Well model) => new()
        {
            Id = model.Id,
            NpdWellBoreCode = model.NpdWellBoreCode,
            Name = model.Name,
            Field = model.Field,
            Type = model.Type
        };

        public static DailyProduction ToModel(DailyProductionEntity entity) => new()
        {
            Id = entity.Id,
            WellId = entity.WellId,
            Date = entity.Date,
            OnStreamHrs = entity.OnStreamHrs,
            AvgDownholePressure = entity.AvgDownholePressure,
            AvgDownholeTemperature = entity.AvgDownholeTemperature,
            AvgDpTubing = entity.AvgDpTubing,
            AvgAnnulusPressure = entity.AvgAnnulusPressure,
            AvgChokeSize = entity.AvgChokeSize,
            AvgWhp = entity.AvgWhp,
            AvgWht = entity.AvgWht,
            DpChokeSize = entity.DpChokeSize,
            OilVol = entity.OilVol,
            GasVol = entity.GasVol,
            WaterVol = entity.WaterVol,
            WaterInjectionVol = entity.WaterInjectionVol
        };

        public static DailyProductionEntity ToEntity(DailyProduction model) => new()
        {
            Id = model.Id,
            WellId = model.WellId,
            Date = model.Date,
            OnStreamHrs = model.OnStreamHrs,
            AvgDownholePressure = model.AvgDownholePressure,
            AvgDownholeTemperature = model.AvgDownholeTemperature,
            AvgDpTubing = model.AvgDpTubing,
            AvgAnnulusPressure = model.AvgAnnulusPressure,
            AvgChokeSize = model.AvgChokeSize,
            AvgWhp = model.AvgWhp,
            AvgWht = model.AvgWht,
            DpChokeSize = model.DpChokeSize,
            OilVol = model.OilVol,
            GasVol = model.GasVol,
            WaterVol = model.WaterVol,
            WaterInjectionVol = model.WaterInjectionVol
        };
    }
}
