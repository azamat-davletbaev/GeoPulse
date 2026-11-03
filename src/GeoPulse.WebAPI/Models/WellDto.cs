using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models
{
    /// <summary>Краткие данные по скважине.</summary>
    public record WellDto
    {
        /// <summary>Идентификатор скважины в БД.</summary>
        public int WellId { get; init; }

        /// <summary>Код ствола в НПД (натуральный ключ, он же идентификатор ресурса в API).</summary>
        public int NpdWellBoreCode { get; init; }

        /// <summary>Название скважины.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Месторождение.</summary>
        public string Field { get; init; } = string.Empty;

        /// <summary>Тип скважины.</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>Широта WGS84, градусы; null — координата не заполнена.</summary>
        public double? Latitude { get; init; }

        /// <summary>Долгота WGS84, градусы; null — координата не заполнена.</summary>
        public double? Longitude { get; init; }

        /// <summary>Проекция модели скважины с координатами в DTO.</summary>
        /// <param name="well">Скважина с координатами устья.</param>
        public static WellDto FromModel(WellLocation well) => new()
        {
            WellId = well.WellId,
            NpdWellBoreCode = well.NpdWellBoreCode,
            Name = well.Name,
            Field = well.Field,
            Type = well.Type,
            Latitude = well.Latitude,
            Longitude = well.Longitude
        };
    }
}
