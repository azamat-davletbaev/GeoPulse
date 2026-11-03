namespace GeoPulse.Core.Events
{
    /// <summary>
    /// Единый реестр причин (reason) для заголовка DLQ-топика production-daily-dlq.
    /// Consumer и CHIngest используют одни и те же значения, чтобы одна и та же
    /// ситуация помечалась одинаково вне зависимости от сервиса.
    /// </summary>
    public static class DlqReasons
    {
        /// <summary>Скважина не найдена в PostgreSQL wells (Consumer).</summary>
        public const string WellNotFound = "well.not.found";

        /// <summary>Скважина не найдена в справочнике npdWellBoreCode → well_id (CHIngest).</summary>
        public const string UnknownWell = "unknown.well";

        /// <summary>Событие распознано, но не может быть обработано (fallback).</summary>
        public const string InvalidEvent = "invalid.event";

        /// <summary>Пустое или пробельное тело сообщения.</summary>
        public const string EmptyMessage = "empty.message";

        /// <summary>Тело сообщения не является валидным JSON.</summary>
        public const string Malformed = "unparseable.json";

        /// <summary>Неизвестная версия схемы события.</summary>
        public const string UnknownSchemaVersion = "unknown.schemaVersion";

        /// <summary>Поле data отсутствует (null).</summary>
        public const string MissingData = "missing.data";

        /// <summary>Событие well.registered не десериализуется в ожидаемую форму.</summary>
        public const string UnparseableWellEvent = "unparseable.well.event";

        /// <summary>Событие production.daily не десериализуется в ожидаемую форму.</summary>
        public const string UnparseableDailyEvent = "unparseable.daily.event";

        /// <summary>Событие well.trajectory не десериализуется в ожидаемую форму.</summary>
        public const string UnparseableTrajectoryEvent = "unparseable.trajectory.event";

        /// <summary>Префикс причины для неизвестного eventType.</summary>
        public const string UnknownEventType = "unknown.eventType";

        /// <summary>Причина для неизвестного eventType с конкретным значением.</summary>
        public static string UnknownEventTypeFor(string eventType) => $"{UnknownEventType}:{eventType}";
    }
}
