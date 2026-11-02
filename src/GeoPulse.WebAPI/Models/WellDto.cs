using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models
{
    /// <summary>Краткие данные по скважине.</summary>
    public record WellDto
    {
        /// <summary>Код ствола в НПД (натуральный ключ, он же идентификатор ресурса в API).</summary>
        public int NpdWellBoreCode { get; init; }

        /// <summary>Название скважины.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Месторождение.</summary>
        public string Field { get; init; } = string.Empty;

        /// <summary>Тип скважины.</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>Проекция доменной модели в DTO.</summary>
        /// <param name="well">Доменная модель скважины.</param>
        public static WellDto FromModel(Well well) => new()
        {
            NpdWellBoreCode = well.NpdWellBoreCode,
            Name = well.Name,
            Field = well.Field,
            Type = well.Type
        };
    }
}
