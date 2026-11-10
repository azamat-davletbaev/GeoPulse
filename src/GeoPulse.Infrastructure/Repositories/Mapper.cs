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

        public static WellLocation ToWellLocation(WellEntity entity) => new()
        {
            WellId = entity.Id,
            NpdWellBoreCode = entity.NpdWellBoreCode,
            Name = entity.Name,
            Field = entity.Field,
            Type = entity.Type,
            Latitude = entity.WellLocation?.Latitude,
            Longitude = entity.WellLocation?.Longitude,
            TdMd = entity.WellTrajectoryMeta?.TdMd,
            TdTvd = entity.WellTrajectoryMeta?.TdTvd
        };

        public static WellTrajectory ToModel(WellTrajectoryEntity entity) => new()
        {
            Md = entity.Md,
            Tvd = entity.Tvd,
            Inclination = entity.Inclination,
            Azimuth = entity.Azimuth,
            Easting = entity.Easting,
            Northing = entity.Northing
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
    }
}
