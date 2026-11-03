namespace GeoPulse.Core.Models
{
    /// <summary>Скважина с географическими координатами устья (JOIN wells + well_locations).</summary>
    public class WellLocation
    {
        /// <summary>Идентификатор скважины (PK wells.Id).</summary>
        public int WellId { get; set; }

        /// <summary>Код ствола в НПД (натуральный ключ).</summary>
        public int NpdWellBoreCode { get; set; }

        /// <summary>Имя ствола.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Месторождение.</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Тип скважины.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Широта WGS84, градусы; null — координата не заполнена.</summary>
        public double? Latitude { get; set; }

        /// <summary>Долгота WGS84, градусы; null — координата не заполнена.</summary>
        public double? Longitude { get; set; }

        /// <summary>Забой по стволу (Bottom Hole MD), м; null — траектория не загружена.</summary>
        public double? TdMd { get; set; }

        /// <summary>Забой по вертикали (Bottom Hole TVD), м; null — траектория не загружена.</summary>
        public double? TdTvd { get; set; }
    }
}
