namespace GeoPulse.Core.Models
{
    /// <summary>Страница данных с метаданными пагинации.</summary>
    /// <typeparam name="T">Тип элемента страницы.</typeparam>
    public sealed class PagedResult<T>
    {
        /// <summary>Элементы текущей страницы.</summary>
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        /// <summary>Общее количество записей, удовлетворяющих фильтру.</summary>
        public int TotalCount { get; init; }

        /// <summary>Номер страницы (с 1).</summary>
        public int Page { get; init; }

        /// <summary>Размер страницы.</summary>
        public int PageSize { get; init; }

        /// <summary>Общее количество страниц.</summary>
        public int TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    }
}
