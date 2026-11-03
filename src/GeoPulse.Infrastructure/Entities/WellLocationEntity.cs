namespace GeoPulse.Infrastructure.Entities
{
    /// <summary>EF-сущность координат устья скважины (таблица <c>well_locations</c>).</summary>
    public class WellLocationEntity
    {
        /// <summary>PK и FK → wells.Id.</summary>
        public int WellId { get; set; }

        /// <summary>Широта WGS84, градусы.</summary>
        public double Latitude { get; set; }

        /// <summary>Долгота WGS84, градусы.</summary>
        public double Longitude { get; set; }

        /// <summary>Источник координат (файл WITSML и преобразование).</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Скважина (навигация).</summary>
        public WellEntity? Well { get; set; }
    }
}
