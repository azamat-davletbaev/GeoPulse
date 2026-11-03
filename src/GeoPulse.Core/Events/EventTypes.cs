namespace GeoPulse.Core.Events
{
    /// <summary>Допустимые типы событий и версия схемы.</summary>
    public static class EventTypes
    {
        /// <summary>Регистрация скважины.</summary>
        public const string WellRegistered = "well.registered";

        /// <summary>Суточная добыча.</summary>
        public const string ProductionDaily = "production.daily";

        /// <summary>Траектория ствола (well.trajectory).</summary>
        public const string Trajectory = "well.trajectory";

        /// <summary>Текущая версия схемы событий.</summary>
        public const int SchemaVersion = 1;
    }
}
