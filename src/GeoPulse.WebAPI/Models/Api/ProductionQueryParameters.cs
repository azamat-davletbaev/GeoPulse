using System.ComponentModel.DataAnnotations;
using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Models.Api
{
    /// <summary>Query-параметры выборки суточной добычи скважины.</summary>
    public sealed class ProductionQueryParameters
    {
        /// <summary>Размер страницы по умолчанию.</summary>
        public const int DefaultPageSize = 100;

        /// <summary>Максимальный размер страницы.</summary>
        public const int MaxPageSize = 500;

        /// <summary>Начало диапазона дат (включительно), формат yyyy-MM-dd.</summary>
        public DateOnly? From { get; set; }

        /// <summary>Конец диапазона дат (включительно), формат yyyy-MM-dd.</summary>
        public DateOnly? To { get; set; }

        /// <summary>Номер страницы (с 1).</summary>
        [Range(1, int.MaxValue, ErrorMessage = "Параметр page должен быть не меньше 1.")]
        public int Page { get; set; } = 1;

        /// <summary>Размер страницы (1..500).</summary>
        [Range(1, MaxPageSize, ErrorMessage = "Параметр pageSize должен быть в диапазоне 1..500.")]
        public int PageSize { get; set; } = DefaultPageSize;

        /// <summary>Порядок по дате: desc (по умолчанию) или asc.</summary>
        public string? Sort { get; set; } = "desc";

        /// <summary>Проверяет согласованность параметров между собой (диапазон дат, значение sort).</summary>
        /// <returns>Ошибки в формате имя поля → сообщение; пустой словарь — параметры корректны.</returns>
        public IReadOnlyDictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);

            if (From is { } from && To is { } to && from > to)
            {
                errors["from"] = "Параметр from не может быть больше параметра to.";
            }

            if (!TryParseSort(Sort, out _))
            {
                errors["sort"] = "Параметр sort должен принимать значение asc или desc.";
            }

            return errors;
        }

        /// <summary>Строит доменный запрос (вызывать после успешной проверки <see cref="Validate"/>).</summary>
        public DailyProductionQuery ToQuery()
        {
            TryParseSort(Sort, out var newestFirst);

            return new DailyProductionQuery
            {
                From = From,
                To = To,
                Page = Page,
                PageSize = PageSize,
                NewestFirst = newestFirst
            };
        }

        private static bool TryParseSort(string? sort, out bool newestFirst)
        {
            newestFirst = true;

            if (string.IsNullOrWhiteSpace(sort))
            {
                return true;
            }

            if (string.Equals(sort, "desc", StringComparison.OrdinalIgnoreCase))
            {
                newestFirst = true;
                return true;
            }

            if (string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase))
            {
                newestFirst = false;
                return true;
            }

            return false;
        }
    }
}
