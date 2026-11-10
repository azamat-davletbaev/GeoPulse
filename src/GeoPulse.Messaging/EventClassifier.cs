using System.Text.Json;
using GeoPulse.Core.Events;

namespace GeoPulse.Messaging
{
    /// <summary>
    /// Единый разбор сообщений топика production-daily для всех консьюмеров.
    /// Определяет тип и версию схемы события, десериализует данные и возвращает причину
    /// отбраковки вместо исключения: одна и та же ситуация помечается одинаково
    /// независимо от сервиса-читателя.
    /// </summary>
    public static class EventClassifier
    {
        /// <summary>Разбирает тело сообщения в <see cref="ClassifiedEvent"/>.</summary>
        /// <param name="raw">Тело сообщения (JSON).</param>
        public static ClassifiedEvent Classify(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ClassifiedEvent.Invalid(DlqReasons.EmptyMessage);
            }

            EventHeader header;
            try
            {
                header = JsonSerializer.Deserialize<EventHeader>(raw, MessagingJson.Options) ?? new EventHeader();
            }
            catch (JsonException)
            {
                return ClassifiedEvent.Invalid(DlqReasons.Malformed);
            }

            if (header.SchemaVersion != EventTypes.SchemaVersion)
            {
                return ClassifiedEvent.Invalid(DlqReasons.UnknownSchemaVersion);
            }

            switch (header.EventType)
            {
                case EventTypes.WellRegistered:
                    {
                        try
                        {
                            var evt = JsonSerializer.Deserialize<WellRegisteredEvent>(raw, MessagingJson.Options);
                            return evt?.Data is null
                                ? ClassifiedEvent.Invalid(DlqReasons.MissingData)
                                : new ClassifiedEvent(MessageKind.Well, evt.Data, null, null, null);
                        }
                        catch (JsonException)
                        {
                            return ClassifiedEvent.Invalid(DlqReasons.UnparseableWellEvent);
                        }
                    }

                case EventTypes.ProductionDaily:
                    {
                        try
                        {
                            var evt = JsonSerializer.Deserialize<DailyProductionEvent>(raw, MessagingJson.Options);
                            return evt?.Data is null
                                ? ClassifiedEvent.Invalid(DlqReasons.MissingData)
                                : new ClassifiedEvent(MessageKind.Daily, null, evt.Data, null, null);
                        }
                        catch (JsonException)
                        {
                            return ClassifiedEvent.Invalid(DlqReasons.UnparseableDailyEvent);
                        }
                    }

                case EventTypes.Trajectory:
                    {
                        try
                        {
                            var evt = JsonSerializer.Deserialize<TrajectoryEvent>(raw, MessagingJson.Options);
                            return evt?.Data is null
                                ? ClassifiedEvent.Invalid(DlqReasons.MissingData)
                                : new ClassifiedEvent(MessageKind.Trajectory, null, null, evt.Data, null);
                        }
                        catch (JsonException)
                        {
                            return ClassifiedEvent.Invalid(DlqReasons.UnparseableTrajectoryEvent);
                        }
                    }

                default:
                    return ClassifiedEvent.Invalid(DlqReasons.UnknownEventTypeFor(header.EventType));
            }
        }

        /// <summary>
        /// Извлекает eventType из тела сообщения для логов DLQ: значение из конверта,
        /// <c>unparseable</c> для битого JSON или <c>unknown</c> при отсутствии поля.
        /// </summary>
        public static string EventTypeOf(string raw)
        {
            try
            {
                var header = JsonSerializer.Deserialize<EventHeader>(raw, MessagingJson.Options);
                return string.IsNullOrWhiteSpace(header?.EventType) ? "unknown" : header.EventType;
            }
            catch (JsonException)
            {
                return "unparseable";
            }
        }
    }
}
