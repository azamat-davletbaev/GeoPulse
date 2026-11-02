namespace GeoPulse.Core.Models
{
    /// <summary>Параметры выборки суточной добычи: диапазон дат, страница, порядок сортировки.</summary>
    public sealed class DailyProductionQuery
    {
        /// <summary>Начало диапазона дат (включительно); null — без ограничения.</summary>
        public DateOnly? From { get; init; }

        /// <summary>Конец диапазона дат (включительно); null — без ограничения.</summary>
        public DateOnly? To { get; init; }

        /// <summary>Номер страницы (с 1).</summary>
        public int Page { get; init; } = 1;

        /// <summary>Размер страницы.</summary>
        public int PageSize { get; init; } = 100;

        /// <summary>Сначала новые даты (по убыванию), иначе по возрастанию.</summary>
        public bool NewestFirst { get; init; } = true;

        /// <summary>Сколько записей пропустить (SQL OFFSET).</summary>
        public int Skip => Page > 1 ? (Page - 1) * PageSize : 0;
    }
}
