using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models
{
    /// <summary>Карточка скважины: справочные данные и покрытие суточной добычей.</summary>
    public record WellDetailsDto
    {
        /// <summary>Код ствола в НПД (натуральный ключ).</summary>
        public int NpdWellBoreCode { get; init; }

        /// <summary>Название скважины.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Месторождение.</summary>
        public string Field { get; init; } = string.Empty;

        /// <summary>Тип скважины.</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>Первая дата с данными добычи; null — данных нет.</summary>
        public DateOnly? FirstProductionDate { get; init; }

        /// <summary>Последняя дата с данными добычи; null — данных нет.</summary>
        public DateOnly? LastProductionDate { get; init; }

        /// <summary>Количество суточных записей добычи.</summary>
        public int ProductionDays { get; init; }

        /// <summary>Проекция доменной модели и покрытия в DTO.</summary>
        /// <param name="well">Доменная модель скважины.</param>
        /// <param name="coverage">Покрытие данными добычи; null — записей нет.</param>
        public static WellDetailsDto FromModel(Well well, ProductionCoverage? coverage) => new()
        {
            NpdWellBoreCode = well.NpdWellBoreCode,
            Name = well.Name,
            Field = well.Field,
            Type = well.Type,
            FirstProductionDate = coverage?.FirstDate,
            LastProductionDate = coverage?.LastDate,
            ProductionDays = coverage?.DaysCount ?? 0
        };
    }
}
