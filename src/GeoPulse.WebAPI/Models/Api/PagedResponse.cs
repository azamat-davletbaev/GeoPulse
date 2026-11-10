using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models.Api
{
    /// <summary>Страница результатов с метаданными пагинации.</summary>
    /// <typeparam name="T">Тип элемента страницы.</typeparam>
    public record PagedResponse<T>
    {
        /// <summary>Элементы текущей страницы.</summary>
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        /// <summary>Общее количество записей, удовлетворяющих фильтру.</summary>
        public int TotalCount { get; init; }

        /// <summary>Номер текущей страницы (с 1).</summary>
        public int Page { get; init; }

        /// <summary>Запрошенный размер страницы.</summary>
        public int PageSize { get; init; }

        /// <summary>Общее количество страниц.</summary>
        public int TotalPages { get; init; }

        /// <summary>Есть ли следующая страница.</summary>
        public bool HasNextPage { get; init; }

        /// <summary>Есть ли предыдущая страница.</summary>
        public bool HasPreviousPage { get; init; }

        /// <summary>
        /// Строит ответ по странице доменного уровня: метаданные пагинации берутся из
        /// <see cref="PagedResult{T}"/>, поэтому арифметика страниц живёт в одном месте.
        /// </summary>
        /// <typeparam name="TModel">Тип доменной модели.</typeparam>
        /// <param name="page">Страница доменных моделей.</param>
        /// <param name="items">Спроецированные элементы страницы.</param>
        public static PagedResponse<T> From<TModel>(PagedResult<TModel> page, IReadOnlyList<T> items) => new()
        {
            Items = items,
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalPages = page.TotalPages,
            HasNextPage = page.Page < page.TotalPages,
            HasPreviousPage = page.Page > 1
        };
    }
}
