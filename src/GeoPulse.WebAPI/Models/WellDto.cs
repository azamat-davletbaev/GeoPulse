namespace GeoPulse.WebAPI.Models
{
    /// <summary>Данные по скважине.</summary>
    public record WellDto
    {
        /// <summary>Идентификатор скважины.</summary>
        public int Id { get; init; }

        /// <summary>Код ствола в НПД.</summary>
        public int NpdWellBoreCode { get; init; }

        /// <summary>Название скважины.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Месторождение.</summary>
        public string Field { get; init; } = string.Empty;

        /// <summary>Тип скважины.</summary>
        public string Type { get; init; } = string.Empty;
    }
}
