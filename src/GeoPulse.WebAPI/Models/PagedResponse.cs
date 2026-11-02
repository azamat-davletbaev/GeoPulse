namespace GeoPulse.WebAPI.Models
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

        /// <summary>Создаёт страницу результатов, рассчитывая метаданные пагинации.</summary>
        /// <param name="items">Элементы страницы.</param>
        /// <param name="totalCount">Всего записей по фильтру.</param>
        /// <param name="page">Номер страницы (с 1).</param>
        /// <param name="pageSize">Размер страницы.</param>
        public static PagedResponse<T> Create(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
        {
            var totalPages = pageSize <= 0 ? 0 : (totalCount + pageSize - 1) / pageSize;

            return new PagedResponse<T>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                HasNextPage = page < totalPages,
                HasPreviousPage = page > 1
            };
        }
    }
}
