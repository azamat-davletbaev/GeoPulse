using GeoPulse.Core.Events;

namespace GeoPulse.Messaging
{
    /// <summary>Тип распознанного входящего сообщения.</summary>
    public enum MessageKind
    {
        /// <summary>Событие well.registered.</summary>
        Well,

        /// <summary>Событие production.daily.</summary>
        Daily,

        /// <summary>Событие well.trajectory.</summary>
        Trajectory,

        /// <summary>Сообщение не распознано и подлежит отбраковке в DLQ.</summary>
        Invalid
    }

    /// <summary>
    /// Результат разбора сообщения: тип, десериализованные данные одного из типов
    /// и причина отбраковки (null для валидных событий).
    /// </summary>
    public sealed record ClassifiedEvent(
        MessageKind Kind,
        WellRegisteredData? Well,
        DailyProductionData? Daily,
        TrajectoryData? Trajectory,
        string? Error)
    {
        /// <summary>Нераспознанное сообщение с причиной отбраковки из <see cref="DlqReasons"/>.</summary>
        public static ClassifiedEvent Invalid(string reason) => new(MessageKind.Invalid, null, null, null, reason);
    }
}
